using System.Security.Cryptography;
using System.Text;
using Archipaper.Models;
namespace Archipaper.Services;

public sealed class ReviewQueueService : IDisposable, IAsyncDisposable
{
    private readonly JsonStore _store;
    private readonly IImageProvider[] _providers = [new WikimediaDiscoveryService(), new OpenverseDiscoveryService(), new LibraryOfCongressDiscoveryService()];
    private readonly HttpClient _http = HttpTransfer.CreateClient();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<OnlineCandidate> _queue;
    private readonly List<ApprovedImageMetadata> _approved;
    private readonly DiscoveryState _state;
    private readonly DiscoveryService _discovery;
    private readonly Dictionary<string, DateTimeOffset> _failedPreviews = new();
    private Task? _disposal;
    public string LastDiscoveryMessage { get; private set; } = "";
    public string LastImportMessage { get; private set; } = "";
    public IReadOnlyList<OnlineCandidate> Pending => _queue.Where(x => x.State == ReviewState.Pending).ToList();
    public IReadOnlyList<ApprovedImageMetadata> Approved => _approved.Where(x => x.IsAvailable).OrderByDescending(x => x.ApprovedAt).ToList();
    private ReviewQueueService(JsonStore store, List<OnlineCandidate> queue, List<ApprovedImageMetadata> approved, DiscoveryState state)
    {
        _store = store; _queue = queue; _approved = approved; _state = state;
        state.Seen ??= new(); state.Pages ??= new(); _discovery = new(_providers);
        foreach (var item in queue) Remember(item.Id, item.OriginalUrl);
        foreach (var item in approved) Remember(item.Id, item.OriginalUrl);
        var approvedIds = approved.Select(x => x.Id).ToHashSet();
        foreach (var item in queue.Where(x => approvedIds.Contains(x.Id))) item.State = ReviewState.Approved;
    }
    public static async Task<ReviewQueueService> CreateAsync(JsonStore store) => new(store,
        await store.LoadAsync(AppPaths.ReviewQueue, () => new List<OnlineCandidate>()),
        await store.LoadAsync(AppPaths.ApprovedMetadata, () => new List<ApprovedImageMetadata>()),
        await store.LoadAsync(AppPaths.DiscoveryState, () => new DiscoveryState()));
    public OnlineCandidate? Current() => _queue.FirstOrDefault(x => x.State == ReviewState.Pending);

    public async Task<int> DiscoverAsync(AppSettings settings, CancellationToken token)
    {
        settings = settings.Snapshot();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            var target = Math.Min(24, 96 - Pending.Count);
            if (target <= 0) { LastDiscoveryMessage = "Review pending images first (96-image queue limit)."; return 0; }
            var result = await _discovery.DiscoverAsync(settings, _state, target, async (candidate, ct) =>
            {
                if (_failedPreviews.TryGetValue(candidate.PreviewUrl, out var until) && until > DateTimeOffset.UtcNow) return false;
                candidate.PreviewFilePath = Path.Combine(AppPaths.CandidateCache, SafeName(candidate.Id, ".image"));
                try { await ValidatedDownloadAsync(candidate.PreviewUrl, candidate.PreviewFilePath, false, ct); _queue.Add(candidate); return true; }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { AppLog.Error(ex); _failedPreviews[candidate.PreviewUrl] = DateTimeOffset.UtcNow.AddMinutes(5); return false; }
            }, linked.Token);
            LastDiscoveryMessage = result.Message; return result.Added;
        }
        finally
        {
            try { await SaveQueueAsync(); await _store.SaveAsync(AppPaths.DiscoveryState, _state); }
            finally { _gate.Release(); }
        }
    }

    public async Task<int> ImportLocalFilesAsync(IEnumerable<string> paths)
    {
        var files = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await _gate.WaitAsync(_lifetime.Token);
        var imported = 0; var duplicates = 0; var failed = 0;
        try
        {
            await PopulateHashesAsync();
            foreach (var source in files)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                var temp = Path.Combine(AppPaths.CandidateCache, Guid.NewGuid().ToString("N") + ".import");
                try
                {
                    var prepared = await Task.Run(() =>
                    {
                        if (!ImageValidation.Extensions.Contains(Path.GetExtension(source)) || new FileInfo(source).Length > ImageValidation.MaxBytes)
                            throw new InvalidDataException("Unsupported or oversized image.");
                        File.Copy(source, temp); // Validate/hash an immutable snapshot of the source.
                        return (Info: ImageValidation.Inspect(temp, true, true), Hash: ContentHash(temp));
                    }, _lifetime.Token);
                    var existing = _approved.FirstOrDefault(x => x.ContentHash == prepared.Hash);
                    if (existing is not null && existing.IsAvailable && File.Exists(existing.FilePath)) { duplicates++; continue; }
                    var id = existing?.Id ?? "import:" + prepared.Hash;
                    var destination = Path.Combine(AppPaths.Approved, SafeName(id, prepared.Info.Extension));
                    File.Move(temp, destination, true);
                    var title = Path.GetFileNameWithoutExtension(source);
                    var item = existing ?? new ApprovedImageMetadata { Id = id, Title = title, ProjectName = title, SourceName = "Imported collection", License = "User-provided image" };
                    item.ContentHash = prepared.Hash; item.FilePath = destination; item.IsAvailable = true; item.ApprovedAt = DateTimeOffset.UtcNow;
                    if (existing is null) _approved.Add(item);
                    await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved); imported++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { AppLog.Error(ex); failed++; }
                finally { DeleteCacheFile(temp); }
            }
            LastImportMessage = $"Imported {imported}; {duplicates} duplicates; {failed} unsupported, unreadable or undersized files.";
            return imported;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ApproveAsync(OnlineCandidate candidate, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            if (candidate.State != ReviewState.Pending || _approved.Any(x => x.Id == candidate.Id && x.IsAvailable && File.Exists(x.FilePath))) return true;
            await PopulateHashesAsync();
            var temp = Path.Combine(AppPaths.CandidateCache, SafeName(candidate.Id, ".original"));
            var fullResolution = true;
            ImageInfo info;
            try
            {
                if (HttpTransfer.UrlKey(candidate.OriginalUrl) == HttpTransfer.UrlKey(candidate.PreviewUrl) && File.Exists(candidate.PreviewFilePath))
                {
                    info = await Task.Run(() => ImageValidation.Inspect(candidate.PreviewFilePath, true, true), linked.Token);
                    await Task.Run(() => CopyAtomically(candidate.PreviewFilePath, temp), linked.Token);
                }
                else info = await ValidatedDownloadAsync(candidate.OriginalUrl, temp, true, linked.Token);
            }
            catch (Exception ex) when (!linked.IsCancellationRequested && File.Exists(candidate.PreviewFilePath))
            {
                AppLog.Error(ex);
                info = await Task.Run(() => ImageValidation.Inspect(candidate.PreviewFilePath, true, true), linked.Token);
                await Task.Run(() => CopyAtomically(candidate.PreviewFilePath, temp), linked.Token); fullResolution = false;
            }
            var hash = await Task.Run(() => ContentHash(temp), linked.Token);
            var existing = _approved.FirstOrDefault(x => x.ContentHash == hash);
            if (existing is null || !existing.IsAvailable || !File.Exists(existing.FilePath))
            {
                var id = existing?.Id ?? candidate.Id;
                var path = Path.Combine(AppPaths.Approved, SafeName(id, info.Extension));
                await Task.Run(() => CopyAtomically(temp, path), linked.Token);
                var image = existing ?? new ApprovedImageMetadata
                {
                    Id = id, Title = candidate.Title, Artist = candidate.Artist, Architect = candidate.Architect, ProjectName = candidate.ProjectName,
                    License = candidate.License, LicenseUrl = candidate.LicenseUrl, SourcePageUrl = candidate.SourcePageUrl, SourceName = candidate.SourceName
                };
                image.ContentHash = hash; image.OriginalUrl = candidate.OriginalUrl; image.FilePath = path;
                image.IsAvailable = true; image.ApprovedAt = DateTimeOffset.UtcNow;
                if (existing is null) _approved.Add(image);
                try { await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved); }
                catch { if (existing is null) _approved.Remove(image); throw; }
            }
            candidate.State = ReviewState.Approved; Remember(candidate.Id, candidate.OriginalUrl);
            await SaveQueueAsync(); await _store.SaveAsync(AppPaths.DiscoveryState, _state);
            DeleteCacheFile(temp); DeleteCacheFile(candidate.PreviewFilePath);
            return fullResolution;
        }
        finally { _gate.Release(); }
    }
    public Task SkipAsync(OnlineCandidate item) => MutateAsync(async () =>
    { if (!_queue.Remove(item)) return; _queue.Add(item); await SaveQueueAsync(); });
    public Task RejectAsync(OnlineCandidate item) => MutateAsync(async () =>
    {
        item.State = ReviewState.Rejected; Remember(item.Id, item.OriginalUrl);
        await SaveQueueAsync(); await _store.SaveAsync(AppPaths.DiscoveryState, _state); DeleteCacheFile(item.PreviewFilePath);
    });
    public Task ToggleFavoriteAsync(ApprovedImageMetadata item) => MutateAsync(async () =>
    { item.IsFavorite = !item.IsFavorite; await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved); });
    public Task RemoveApprovedAsync(ApprovedImageMetadata item) => MutateAsync(async () =>
    {
        if (File.Exists(item.FilePath))
        {
            var destination = Path.Combine(AppPaths.Removed, Path.GetFileName(item.FilePath));
            File.Move(item.FilePath, destination, true); item.FilePath = destination;
        }
        item.IsAvailable = false; await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved);
    });
    private async Task MutateAsync(Func<Task> action)
    { await _gate.WaitAsync(_lifetime.Token); try { await action(); } finally { _gate.Release(); } }
    private Task SaveQueueAsync()
    {
        var settled = _queue.Where(x => x.State != ReviewState.Pending).TakeLast(1000).ToHashSet();
        _queue.RemoveAll(x => x.State != ReviewState.Pending && !settled.Contains(x));
        return _store.SaveAsync(AppPaths.ReviewQueue, _queue);
    }
    private async Task<ImageInfo> ValidatedDownloadAsync(string url, string path, bool fullSize, CancellationToken token)
    {
        if (File.Exists(path))
        {
            try { return await Task.Run(() => ImageValidation.Inspect(path, fullSize, true), token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { AppLog.Error(ex); DeleteCacheFile(path); }
        }
        await HttpTransfer.DownloadAsync(_http, url, path, fullSize ? ImageValidation.MaxBytes : 16 * 1024 * 1024, TimeSpan.FromSeconds(fullSize ? 45 : 10), token);
        try { return await Task.Run(() => ImageValidation.Inspect(path, fullSize, true), token); }
        catch { DeleteCacheFile(path); throw; }
    }
    private async Task PopulateHashesAsync()
    {
        var missing = _approved.Where(x => string.IsNullOrEmpty(x.ContentHash) && File.Exists(x.FilePath)).ToList();
        foreach (var image in missing)
        {
            try { image.ContentHash = await Task.Run(() => ContentHash(image.FilePath), _lifetime.Token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { AppLog.Error(ex); }
        }
        if (missing.Count > 0) await _store.SaveAsync(AppPaths.ApprovedMetadata, _approved);
    }
    private void Remember(string id, string url)
    {
        _state.Seen.Add(id);
        if (string.IsNullOrWhiteSpace(url)) return;
        try { _state.Seen.Add(HttpTransfer.UrlKey(url)); } catch (InvalidDataException) { }
    }
    private static string ContentHash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string SafeName(string value, string extension) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24] + extension;
    private static void CopyAtomically(string source, string destination)
    {
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try { File.Copy(source, temp); File.Move(temp, destination, true); }
        finally { DeleteCacheFile(temp); }
    }
    private static void DeleteCacheFile(string path)
    { try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { AppLog.Error(ex); } }
    public void Dispose()
    { if (_disposal is not null) return; _lifetime.Cancel(); _disposal = DisposeWhenIdleAsync(); }
    private async Task DisposeWhenIdleAsync()
    {
        await _gate.WaitAsync();
        try { foreach (var provider in _providers) provider.Dispose(); _http.Dispose(); }
        finally { _gate.Release(); }
    }
    public async ValueTask DisposeAsync() { Dispose(); await _disposal!; }
}
