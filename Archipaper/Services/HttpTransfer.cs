using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
namespace Archipaper.Services;

public static class HttpTransfer
{
    public static HttpClient CreateClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 8 * 1024 * 1024,
        DefaultRequestHeaders = { UserAgent = { new ProductInfoHeaderValue("Archipaper", "1.4") } }
    };
    public static string UrlKey(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new InvalidDataException("Only HTTP(S) URLs are allowed.");
        return new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
    }
    public static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMinutes(5);
            throw new ProviderRateLimitException(delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1));
        }
        response.EnsureSuccessStatusCode();
    }
    public static async Task DownloadAsync(HttpClient http, string url, string destination, long maxBytes, TimeSpan timeout, CancellationToken token)
    {
        url = UrlKey(url);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            EnsureSuccess(response);
            if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Download is too large.");
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not null && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && type != "application/octet-stream") throw new InvalidDataException("Server returned a non-image response.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > maxBytes) throw new InvalidDataException("Download is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                }
                if (total == 0) throw new InvalidDataException("Empty download.");
            }
            File.Move(temp, destination, true);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested) { throw new TimeoutException("Download timed out.", ex); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public sealed class ProviderRateLimitException(TimeSpan retryAfter) : HttpRequestException("Image provider rate limit reached.")
{ public TimeSpan RetryAfter { get; } = retryAfter; }
