using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;
using Archipaper.Models;

namespace Archipaper.Services;

public sealed class LibraryOfCongressDiscoveryService : IDisposable
{
    private const string PhotosApi = "https://www.loc.gov/photos/";
    private readonly HttpClient _http;

    public LibraryOfCongressDiscoveryService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Archipaper", "0.8"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(desktop-wallpaper-app)"));
    }

    public async Task<IReadOnlyList<OnlineCandidate>> SearchAsync(string subject, int limit, CancellationToken token)
    {
        var query = QueryFor(subject);
        var url = PhotosApi + "?fo=json&c=" + Math.Clamp(limit, 1, 15)
            + "&fa=online-format:image"
            + "&q=" + Uri.EscapeDataString(query);

        using var response = await _http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!json.RootElement.TryGetProperty("results", out var items)) return [];

        var results = new List<OnlineCandidate>();
        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var candidate = CandidateFromItem(item, subject, allowDetailFetch: false);
                if (candidate is null && Uri.TryCreate(Value(item, "url"), UriKind.Absolute, out var itemUri))
                {
                    candidate = await CandidateFromDetailAsync(itemUri, subject, token);
                }

                if (candidate is not null) results.Add(candidate);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { AppLog.Error(ex); }
        }
        return results;
    }

    public async Task DownloadAsync(string url, string destination, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, token);
    }

    private async Task<OnlineCandidate?> CandidateFromDetailAsync(Uri itemUri, string subject, CancellationToken token)
    {
        var builder = new UriBuilder(itemUri) { Query = "fo=json" };
        using var response = await _http.GetAsync(builder.Uri, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        return json.RootElement.TryGetProperty("item", out var item)
            ? CandidateFromItem(item, subject, allowDetailFetch: true)
            : null;
    }

    private static OnlineCandidate? CandidateFromItem(JsonElement item, string subject, bool allowDetailFetch)
    {
        var image = BestImage(item);
        if (string.IsNullOrWhiteSpace(image.Url)) return null;

        var title = Value(item, "title");
        if (string.IsNullOrWhiteSpace(title)) title = subject;

        var rights = FirstNonBlank(Value(item, "rights_advisory"), Value(item, "rights_information"), NestedValue(item, "item", "rights_information"));
        if (string.IsNullOrWhiteSpace(rights)) rights = "See Library of Congress source page";

        var creator = Creator(item);
        var id = Value(item, "id");
        if (string.IsNullOrWhiteSpace(id)) id = Value(item, "url");
        id = id.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);

        var sourcePage = FirstNonBlank(Value(item, "url"), Value(item, "link"), id);
        if (sourcePage.StartsWith("//", StringComparison.Ordinal)) sourcePage = "https:" + sourcePage;
        if (sourcePage.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            sourcePage = "https://" + sourcePage[7..];

        return new OnlineCandidate
        {
            Id = "loc:" + id,
            DiscoveryProvider = "loc",
            SourceName = "Library of Congress · Drawings",
            Title = title,
            ArchitectOrCategory = subject + " drawings",
            Architect = "",
            ProjectName = ArchitectureMetadata.CleanProjectName(title, ""),
            Artist = string.IsNullOrWhiteSpace(creator) ? "Library of Congress contributor" : creator,
            License = rights,
            LicenseUrl = "https://www.loc.gov/rr/print/res/rights.html",
            SourcePageUrl = sourcePage,
            OriginalUrl = image.Url,
            PreviewUrl = image.Url,
            Width = image.Width,
            Height = image.Height
        };
    }

    private static LocImage BestImage(JsonElement item)
    {
        var options = new List<LocImage>();
        AddImageUrls(options, item);
        if (item.TryGetProperty("item", out var nested))
        {
            AddImageUrls(options, nested);
            AddService(options, nested, "service_larger");
            AddService(options, nested, "service_high");
            AddService(options, nested, "service_medium");
        }
        AddService(options, item, "service_larger");
        AddService(options, item, "service_high");
        AddService(options, item, "service_medium");

        return options
            .Where(x => !x.Url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Width * x.Height)
            .ThenByDescending(x => ScoreUrl(x.Url))
            .FirstOrDefault();
    }

    private static void AddImageUrls(List<LocImage> options, JsonElement item)
    {
        if (!item.TryGetProperty("image_url", out var images) || images.ValueKind is not JsonValueKind.Array) return;
        foreach (var image in images.EnumerateArray())
        {
            var url = image.GetString()?.Trim() ?? "";
            if (url.Length == 0) continue;
            options.Add(ParseImage(url));
        }
    }

    private static void AddService(List<LocImage> options, JsonElement item, string property)
    {
        var url = Value(item, property);
        if (url.Length == 0) return;
        options.Add(ParseImage(url));
    }

    private static LocImage ParseImage(string url)
    {
        if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
        var width = MatchDimension(url, "w");
        var height = MatchDimension(url, "h");
        if (width == 0 && url.Contains("_150px", StringComparison.OrdinalIgnoreCase)) width = 150;
        if (height == 0 && url.Contains("_150px", StringComparison.OrdinalIgnoreCase)) height = 150;
        return new LocImage(url, width, height);
    }

    private static int MatchDimension(string value, string name)
    {
        var match = Regex.Match(value, @"(?:#|&)" + Regex.Escape(name) + @"=(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var dimension) ? dimension : 0;
    }

    private static int ScoreUrl(string url)
    {
        if (url.Contains("service_larger", StringComparison.OrdinalIgnoreCase)) return 5000;
        if (url.EndsWith("v.jpg", StringComparison.OrdinalIgnoreCase)) return 4000;
        if (url.Contains("service_high", StringComparison.OrdinalIgnoreCase)) return 3000;
        if (url.EndsWith("r.jpg", StringComparison.OrdinalIgnoreCase)) return 2000;
        if (url.Contains("service_medium", StringComparison.OrdinalIgnoreCase)) return 1000;
        return url.Contains("_150px", StringComparison.OrdinalIgnoreCase) ? 1 : 100;
    }

    private static string QueryFor(string subject)
    {
        var lower = subject.ToLowerInvariant();
        if (lower.Contains("drawing") || lower.Contains("sketch"))
            return "architectural drawing sketch";
        return subject + " architectural drawing sketch";
    }

    private static string Creator(JsonElement item)
    {
        if (item.TryGetProperty("item", out var nested))
        {
            var nestedCreator = Creator(nested);
            if (nestedCreator.Length > 0) return nestedCreator;
        }
        if (item.TryGetProperty("contributor", out var contributors) && contributors.ValueKind is JsonValueKind.Array)
        {
            var first = contributors.EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(first)) return TitleCaseName(first);
        }
        if (item.TryGetProperty("contributors", out var contributorObjects) && contributorObjects.ValueKind is JsonValueKind.Array)
        {
            var first = contributorObjects.EnumerateArray()
                .Select(x => x.ValueKind is JsonValueKind.Object ? Value(x, "title") : x.GetString())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
        }
        if (item.TryGetProperty("creators", out var creators) && creators.ValueKind is JsonValueKind.Array)
        {
            var architect = creators.EnumerateArray()
                .FirstOrDefault(x => Value(x, "role").Contains("architect", StringComparison.OrdinalIgnoreCase));
            var value = architect.ValueKind is JsonValueKind.Object ? Value(architect, "title") : "";
            if (value.Length == 0)
            {
                value = creators.EnumerateArray()
                    .Select(x => x.ValueKind is JsonValueKind.Object ? Value(x, "title") : x.GetString())
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
            }
            if (value.Length > 0) return value.Trim();
        }
        return "";
    }

    private static string TitleCaseName(string value)
    {
        return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Length == 0 ? x : char.ToUpperInvariant(x[0]) + x[1..]));
    }

    private static string NestedValue(JsonElement element, string parent, string child) =>
        element.TryGetProperty(parent, out var nested) ? Value(nested, child) : "";

    private static string Value(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.ToString().Trim() : "";

    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";

    public void Dispose() => _http.Dispose();

    private readonly record struct LocImage(string Url, int Width, int Height);
}
