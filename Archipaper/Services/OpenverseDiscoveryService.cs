using System.Net.Http.Headers;
using System.Text.Json;
using Archipaper.Models;

namespace Archipaper.Services;

public sealed class OpenverseDiscoveryService : IDisposable
{
    private const string Api = "https://api.openverse.org/v1/images/";
    private readonly HttpClient _http;

    public OpenverseDiscoveryService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Archipaper", "1.1"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(desktop-wallpaper-app)"));
    }

    public async Task<IReadOnlyList<OnlineCandidate>> SearchAsync(DiscoveryRequest request, int limit, CancellationToken token)
    {
        var query = request.QueryText;
        var url = Api + "?q=" + Uri.EscapeDataString(query)
            + "&page_size=" + Math.Clamp(limit, 1, 20)
            + "&mature=false";

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
                var source = Value(item, "source");
                var provider = Value(item, "provider");
                if (source.Contains("wikimedia", StringComparison.OrdinalIgnoreCase)
                    || provider.Contains("wikimedia", StringComparison.OrdinalIgnoreCase)) continue;

                var width = IntValue(item, "width");
                var height = IntValue(item, "height");
                if (Math.Min(width, height) < 1200 || Math.Max(width, height) < 1800) continue;

                var originalUrl = Value(item, "url");
                var previewUrl = Value(item, "thumbnail");
                var sourcePage = Value(item, "foreign_landing_url");
                var license = FormatLicense(Value(item, "license"), Value(item, "license_version"));
                if (string.IsNullOrWhiteSpace(originalUrl) || string.IsNullOrWhiteSpace(previewUrl)
                    || string.IsNullOrWhiteSpace(sourcePage) || string.IsNullOrWhiteSpace(license)) continue;

                var title = Value(item, "title");
                if (string.IsNullOrWhiteSpace(title)) title = request.DisplayLabel;
                var architect = request.Architect;
                results.Add(new OnlineCandidate
                {
                    Id = "openverse:" + Value(item, "id"),
                    DiscoveryProvider = "openverse",
                    SourceName = "Openverse · " + DisplaySource(source, provider),
                    Title = title,
                    ArchitectOrCategory = request.DisplayLabel,
                    Architect = architect,
                    ProjectName = ArchitectureMetadata.CleanProjectName(title, architect),
                    Artist = Value(item, "creator") is { Length: > 0 } creator ? creator : "Unknown contributor",
                    License = license,
                    LicenseUrl = Value(item, "license_url"),
                    SourcePageUrl = sourcePage,
                    OriginalUrl = originalUrl,
                    PreviewUrl = previewUrl,
                    Width = width,
                    Height = height
                });
            }
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

    private static string Value(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.ToString().Trim() : "";

    private static int IntValue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string FormatLicense(string license, string version)
    {
        if (string.IsNullOrWhiteSpace(license)) return "";
        var name = license.Equals("pdm", StringComparison.OrdinalIgnoreCase)
            ? "Public Domain Mark"
            : license.Equals("cc0", StringComparison.OrdinalIgnoreCase)
                ? "CC0"
                : "CC " + license.ToUpperInvariant().Replace('-', ' ');
        return string.IsNullOrWhiteSpace(version) ? name : $"{name} {version}";
    }

    private static string DisplaySource(string source, string provider)
    {
        var value = string.IsNullOrWhiteSpace(source) ? provider : source;
        if (string.IsNullOrWhiteSpace(value)) return "external collection";
        return string.Join(" ", value.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    }

    public void Dispose() => _http.Dispose();
}
