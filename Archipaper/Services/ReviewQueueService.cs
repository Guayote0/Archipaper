using System.Security.Cryptography;
using System.Text;
using Archipaper.Models;

namespace Archipaper.Services;

public sealed class ReviewQueueService : IDisposable
{
    private readonly JsonStore _store;
    private readonly WikimediaDiscoveryService _discovery = new();
    private readonly Random _random = new();
    private List<OnlineCandidate> _queue;
    private List<ApprovedImageMetadata> _approved;

    public IReadOnlyList<OnlineCandidate> Pending => _queue.Where(x => x.State == ReviewState.Pending).ToList();
    public IReadOnlyList<ApprovedImageMetadata> Approved => _approved.Where(x => x.IsAvailable).OrderByDescending(x => x.ApprovedAt).ToList();

    private ReviewQueueService(JsonStore store, List<OnlineCandidate> queue, List<ApprovedImageMetadata> approved)
    {
        _store = store;
        _queue = queue;
        _approved = approved;
    }

    public static async Task<ReviewQueueService> CreateAsync(JsonStore store)
    {
        var queue = await store.LoadAsync(AppPaths.ReviewQueue, () => new List<OnlineCandidate>());
        var approved = await store.LoadAsync(AppPaths.ApprovedMetadata, () => new List<ApprovedImageMetadata>());
        return new ReviewQueueService(store, queue, approved);
    }

    public OnlineCandidate? Current() => Pending.FirstOrDefault();

    public async Task<int> DiscoverAsync(AppSettings settings, CancellationToken token)
    {
        var choices = new List<string>();
        choices.AddRange(settings.EnabledCategories);
        choices.AddRange(settings.PreferredArchitects);
        choices.AddRange(settings.BoostedArchitects);
        choices.AddRange(settings.BoostedArchitects);
        if (choices.Count == 0) choices.Add("contemporary architecture");

        var subjects = choices.OrderBy(_ => _random.Next()).Distinct().Take(4).ToList();
        var known = _queue.Select(x => x.Id).Concat(_approved.Select(x => x.Id)).ToHashSet();
        var added = 0;
        foreach (var subject in subjects)
        {
            foreach (var candidate in await _discovery.SearchAsync(subject, 10, token))
            {
                if (!known.Add(candidate.Id)) continue;
                candidate.PreviewFilePath = Path.Combine(AppPaths.CandidateCache, SafeName(candidate.Id, ".jpg"));
                try { await _discovery.DownloadAsync(candidate.PreviewUrl, candidate.PreviewFilePath, token); }
                catch (Exception ex) { AppLog.Error(ex); continue; }
                _queue.Add(candidate);
                added++;
            }
        }
        await SaveQueueAsync();
        return added;
    }

    public async Task ApproveAsync(OnlineCandidate candidate, CancellationToken token)
    {
        var extension = ExtensionFromUrl(candidate.OriginalUrl);
        var path = Path.Combine(AppPaths.Approved, SafeName(candidate.Id, extension));
        var temp = path + ".download";
        await _discovery.DownloadAsync(candidate.OriginalUrl, temp, token);
        File.Move(temp, path, true);
        candidate.State = ReviewState.Approved;
        _approved.Add(new ApprovedImageMetadata
        {
            Id = candidate.Id, FilePath = path, Title = candidate.Title, Artist = candidate.Artist,
            License = candidate.License, LicenseUrl = candidate.LicenseUrl,
            SourcePageUrl = candidate.SourcePageUrl, ApprovedAt = DateTimeOffset.Now
        });
        await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved);
        await SaveQueueAsync();
    }

    public async Task RejectAsync(OnlineCandidate candidate)
    {
        candidate.State = ReviewState.Rejected;
        if (File.Exists(candidate.PreviewFilePath)) File.Delete(candidate.PreviewFilePath);
        await SaveQueueAsync();
    }

    public async Task ToggleFavoriteAsync(ApprovedImageMetadata image)
    {
        image.IsFavorite = !image.IsFavorite;
        await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved);
    }

    public async Task RemoveApprovedAsync(ApprovedImageMetadata image)
    {
        if (File.Exists(image.FilePath))
        {
            var destination = Path.Combine(AppPaths.Removed, Path.GetFileName(image.FilePath));
            File.Move(image.FilePath, destination, true);
            image.FilePath = destination;
        }
        image.IsAvailable = false;
        await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved);
    }

    private Task SaveQueueAsync()
    {
        _queue = _queue.TakeLast(1000).ToList();
        return _store.SaveAsync(AppPaths.ReviewQueue, _queue);
    }

    private static string SafeName(string value, string extension)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..20];
        return hash + extension;
    }

    private static string ExtensionFromUrl(string url)
    {
        var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" ? ext : ".jpg";
    }

    public void Dispose() => _discovery.Dispose();
}
