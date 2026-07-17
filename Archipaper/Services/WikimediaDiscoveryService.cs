using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Archipaper.Models;

namespace Archipaper.Services;

public sealed class WikimediaDiscoveryService : IDisposable
{
    private const string Api = "https://commons.wikimedia.org/w/api.php";
    private readonly HttpClient _http;

    public WikimediaDiscoveryService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Archipaper", "0.8"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(desktop-wallpaper-app)"));
    }

    public async Task<IReadOnlyList<OnlineCandidate>> SearchAsync(string subject, int limit, CancellationToken token)
    {
        var search = $"{subject} architecture photograph filetype:bitmap";
        var url = Api + "?action=query&generator=search&gsrnamespace=6&gsrlimit=" + limit
            + "&gsrsearch=" + Uri.EscapeDataString(search)
            + "&prop=imageinfo&iiprop=url|size|mime|extmetadata&iiurlwidth=1200&format=json&formatversion=2&origin=*";

        using var response = await _http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!json.RootElement.TryGetProperty("query", out var query)
            || !query.TryGetProperty("pages", out var pages)) return [];

        var results = new List<OnlineCandidate>();
        foreach (var page in pages.EnumerateArray())
        {
            try
            {
                var info = page.GetProperty("imageinfo")[0];
                var mime = Value(info, "mime");
                var width = info.GetProperty("width").GetInt32();
                var height = info.GetProperty("height").GetInt32();
                if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    || Math.Min(width, height) < 1200 || Math.Max(width, height) < 1800) continue;
                var metadata = info.GetProperty("extmetadata");
                var license = Meta(metadata, "LicenseShortName");
                if (string.IsNullOrWhiteSpace(license)) continue;

                var title = Value(page, "title").Replace("File:", "", StringComparison.OrdinalIgnoreCase);
                var architect = "";
                results.Add(new OnlineCandidate
                {
                    Id = page.GetProperty("pageid").GetInt64().ToString(),
                    DiscoveryProvider = "wikimedia",
                    SourceName = "Wikimedia Commons",
                    Title = title,
                    ArchitectOrCategory = subject,
                    Architect = architect,
                    ProjectName = ArchitectureMetadata.CleanProjectName(title, architect),
                    Artist = Clean(Meta(metadata, "Artist")),
                    License = Clean(license),
                    LicenseUrl = Clean(Meta(metadata, "LicenseUrl")),
                    SourcePageUrl = Value(info, "descriptionurl"),
                    OriginalUrl = Value(info, "url"),
                    PreviewUrl = Value(info, "thumburl"),
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

    private static string Meta(JsonElement metadata, string name) =>
        metadata.TryGetProperty(name, out var item) && item.TryGetProperty("value", out var value) ? value.GetString() ?? "" : "";

    private static string Value(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ToString() : "";

    private static string Clean(string value) => WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " ")).Trim();

    public void Dispose() => _http.Dispose();
}
