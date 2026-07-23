using System.Security.Cryptography;
using System.Text;
using Archipaper.Models;

namespace Archipaper.Services;

public sealed class ReviewQueueService : IDisposable
{
    private readonly JsonStore _store;
    private readonly WikimediaDiscoveryService _wikimedia = new();
    private readonly OpenverseDiscoveryService _openverse = new();
    private readonly LibraryOfCongressDiscoveryService _libraryOfCongress = new();
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
        var requests = BuildRequests(settings);
        var known = _queue.Select(x => x.Id).Concat(_approved.Select(x => x.Id)).ToHashSet();
        var added = 0;
        foreach (var request in requests)
        {
            var candidates = new List<OnlineCandidate>();
            if (settings.SearchWikimedia)
            {
                try { candidates.AddRange(await _wikimedia.SearchAsync(request, 10, token)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { AppLog.Error(ex); }
            }
            if (settings.SearchOpenverse)
            {
                try { candidates.AddRange(await _openverse.SearchAsync(request, 10, token)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { AppLog.Error(ex); }
            }
            if (settings.SearchLibraryOfCongress
                && request.Category.Equals("Drawings", StringComparison.OrdinalIgnoreCase))
            {
                try { candidates.AddRange(await _libraryOfCongress.SearchAsync(request, 10, token)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { AppLog.Error(ex); }
            }

            foreach (var candidate in candidates)
            {
                if (!known.Add(candidate.Id)) continue;
                candidate.PreviewFilePath = Path.Combine(AppPaths.CandidateCache, SafeName(candidate.Id, ".jpg"));
                try { await DownloadAsync(candidate, candidate.PreviewUrl, candidate.PreviewFilePath, token); }
                catch (Exception ex) { AppLog.Error(ex); continue; }
                _queue.Add(candidate);
                added++;
            }
        }
        await SaveQueueAsync();
        return added;
    }

    private IReadOnlyList<DiscoveryRequest> BuildRequests(AppSettings settings)
    {
        var categories = settings.EnabledCategories
            .Where(x => x is "Buildings" or "Interiors" or "Details" or "Drawings" or "Models")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (categories.Count == 0) categories.Add("Buildings");

        var architects = settings.PreferredArchitects
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var requests = new List<DiscoveryRequest>();

        foreach (var architect in architects)
        {
            var category = categories[_random.Next(categories.Count)];
            requests.Add(new DiscoveryRequest(architect, category, settings.StrictArchitectSearch));
        }

        if (!settings.StrictArchitectSearch || architects.Count == 0)
        {
            foreach (var category in categories)
                requests.Add(new DiscoveryRequest("", category, false));
        }

        if (requests.Count == 0)
            requests.Add(new DiscoveryRequest("", "Buildings", false));

        return requests
            .OrderBy(_ => _random.Next())
            .Take(12)
            .ToList();
    }

    public async Task<int> ImportLocalFilesAsync(IEnumerable<string> filePaths)
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp" };
        var knownIds = _approved.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = 0;

        foreach (var sourcePath in filePaths.Where(File.Exists))
        {
            var extension = Path.GetExtension(sourcePath);
            if (!supported.Contains(extension)) continue;

            string hash;
            using (var stream = File.OpenRead(sourcePath))
            using (var sha = SHA256.Create())
                hash = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();

            var id = "import:" + hash;
            if (!knownIds.Add(id)) continue;

            var destination = Path.Combine(AppPaths.Approved, SafeName(id, extension.ToLowerInvariant()));
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                File.Copy(sourcePath, destination, false);

            var title = Path.GetFileNameWithoutExtension(sourcePath);
            _approved.Add(new ApprovedImageMetadata
            {
                Id = id,
                FilePath = destination,
                Title = title,
                ProjectName = title,
                SourceName = "Imported collection",
                License = "User-provided image",
                ApprovedAt = DateTimeOffset.Now
            });
            imported++;
        }

        if (imported > 0)
            await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved);
        return imported;
    }

    public async Task<bool> ApproveAsync(OnlineCandidate candidate, CancellationToken token)
    {
        var extension = ExtensionFromUrl(candidate.OriginalUrl);
        var path = Path.Combine(AppPaths.Approved, SafeName(candidate.Id, extension));
        var temp = path + ".download";
        var fullResolution = true;
        try
        {
            await DownloadAsync(candidate, candidate.OriginalUrl, temp, token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && File.Exists(candidate.PreviewFilePath))
        {
            AppLog.Error(ex);
            File.Copy(candidate.PreviewFilePath, temp, true);
            fullResolution = false;
        }
        File.Move(temp, path, true);
        candidate.State = ReviewState.Approved;
        _approved.Add(new ApprovedImageMetadata
        {
            Id = candidate.Id, FilePath = path, Title = candidate.Title, Artist = candidate.Artist,
            Architect = candidate.Architect, ProjectName = candidate.ProjectName,
            License = candidate.License, LicenseUrl = candidate.LicenseUrl,
            SourcePageUrl = candidate.SourcePageUrl, SourceName = candidate.SourceName,
            ApprovedAt = DateTimeOffset.Now
        });
        await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved);
        await SaveQueueAsync();
        return fullResolution;
    }

    public async Task SkipAsync(OnlineCandidate candidate)
    {
        if (!_queue.Remove(candidate)) return;
        _queue.Add(candidate);
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

    private Task DownloadAsync(OnlineCandidate candidate, string url, string destination, CancellationToken token) =>
        candidate.DiscoveryProvider.Equals("openverse", StringComparison.OrdinalIgnoreCase)
            ? _openverse.DownloadAsync(url, destination, token)
            : candidate.DiscoveryProvider.Equals("loc", StringComparison.OrdinalIgnoreCase)
                ? _libraryOfCongress.DownloadAsync(url, destination, token)
                : _wikimedia.DownloadAsync(url, destination, token);

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

    public void Dispose()
    {
        _wikimedia.Dispose();
        _openverse.Dispose();
        _libraryOfCongress.Dispose();
    }
}
