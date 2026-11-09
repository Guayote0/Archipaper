using System.Net;
using System.Text.Json;
using Archipaper.Models;

namespace Archipaper.Services;

public sealed class WikimediaDiscoveryService : IImageProvider
{
    private const string Api = "https://commons.wikimedia.org/w/api.php";
    private readonly HttpClient _http;

    public string Id => "wikimedia";
    public string Name => "Wikimedia Commons";
    public bool IsEnabled(AppSettings settings, DiscoveryRequest request) => settings.SearchWikimedia;
    public WikimediaDiscoveryService(HttpClient? http = null) => _http = http ?? HttpTransfer.CreateClient();

    public async Task<IReadOnlyList<OnlineCandidate>> SearchAsync(DiscoveryRequest request, int limit, CancellationToken token, int page = 0)
    {
        var results = new List<OnlineCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var queryText in request.QueryVariants)
        {
            var search = queryText + " filetype:bitmap";
            var url = Api + "?action=query&generator=search&gsrnamespace=6&gsrlimit=" + Math.Clamp(limit, 1, 50)
                + "&gsroffset=" + (page * Math.Clamp(limit, 1, 50))
                + "&gsrsearch=" + Uri.EscapeDataString(search)
                + "&prop=imageinfo&iiprop=url|size|mime|extmetadata&iiurlwidth=1200&format=json&formatversion=2&origin=*";

            using var response = await _http.GetAsync(url, token);
            HttpTransfer.EnsureSuccess(response);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            if (json.RootElement.TryGetProperty("error", out var error)) throw new InvalidDataException("Wikimedia API error: " + Value(error, "code"));
            if (!json.RootElement.TryGetProperty("query", out var apiQuery)
                || !apiQuery.TryGetProperty("pages", out var pages)) continue;

            foreach (var resultPage in pages.EnumerateArray())
            {
                try
                {
                    var id = resultPage.GetProperty("pageid").GetInt64().ToString();
                    if (!seen.Add(id)) continue;
                    var info = resultPage.GetProperty("imageinfo")[0];
                    var mime = Value(info, "mime");
                    var width = info.GetProperty("width").GetInt32();
                    var height = info.GetProperty("height").GetInt32();
                    if (mime is not ("image/jpeg" or "image/png" or "image/bmp")
                        || Math.Min(width, height) < 1200 || Math.Max(width, height) < 1800) continue;
                    var metadata = info.GetProperty("extmetadata");
                    var license = Meta(metadata, "LicenseShortName");
                    if (string.IsNullOrWhiteSpace(license)) continue;

                    var title = Value(resultPage, "title").Replace("File:", "", StringComparison.OrdinalIgnoreCase);
                    results.Add(new OnlineCandidate
                    {
                        Id = id,
                        DiscoveryProvider = "wikimedia",
                        SourceName = "Wikimedia Commons",
                        Title = title,
                        ArchitectOrCategory = request.DisplayLabel,
                        Architect = request.Architect,
                        ProjectName = ArchitectureMetadata.CleanProjectName(title, request.Architect),
                        Artist = Clean(Meta(metadata, "Artist")),
                        License = Clean(license),
                        LicenseUrl = Clean(Meta(metadata, "LicenseUrl")),
                        SourcePageUrl = Value(info, "descriptionurl"),
                        OriginalUrl = Value(info, "url"),
                        PreviewUrl = string.IsNullOrWhiteSpace(Value(info, "thumburl")) ? Value(info, "url") : Value(info, "thumburl"),
                        Width = width,
                        Height = height
                    });
                    if (results.Count >= limit) return results;
                }
                catch (Exception ex) { AppLog.Error(ex); }
            }
        }
        return results;
    }


    private static string Meta(JsonElement metadata, string name) =>
        metadata.TryGetProperty(name, out var item) && item.TryGetProperty("value", out var value) ? value.GetString() ?? "" : "";

    private static string Value(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ToString() : "";

    private static string Clean(string value) => WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " ")).Trim();

    public void Dispose() => _http.Dispose();
}
