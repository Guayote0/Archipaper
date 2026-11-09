using Archipaper.Models;
namespace Archipaper.Services;
public interface IImageProvider : IDisposable
{
    string Id { get; }
    string Name { get; }
    bool IsEnabled(AppSettings settings, DiscoveryRequest request);
    Task<IReadOnlyList<OnlineCandidate>> SearchAsync(DiscoveryRequest request, int limit, CancellationToken token, int page = 0);
}
