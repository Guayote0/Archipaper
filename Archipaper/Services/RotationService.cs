using Archipaper.Models;

namespace Archipaper.Services;

public sealed class RotationService : IDisposable
{
    private readonly DesktopWallpaperService _desktop;
    private readonly LocalWallpaperSource _source;
    private readonly CaptionedWallpaperService _captions;
    private readonly JsonStore _store;
    private readonly SemaphoreSlim _rotationLock = new(1, 1);
    private readonly Random _random = new();
    private System.Threading.Timer? _timer;
    private List<HistoryEntry> _history;

    public AppSettings Settings { get; }
    public IReadOnlyList<HistoryEntry> History => _history.AsEnumerable().Reverse().Take(100).ToList();
    public event EventHandler<string>? StatusChanged;

    public RotationService(AppSettings settings, List<HistoryEntry> history, JsonStore store)
    {
        Settings = settings;
        _history = history;
        _store = store;
        _desktop = new DesktopWallpaperService();
        _source = new LocalWallpaperSource();
        _captions = new CaptionedWallpaperService();
    }

    public void StartTimer()
    {
        _timer?.Dispose();
        if (!Settings.RotateAutomatically) return;
        var interval = TimeSpan.FromMinutes(Math.Clamp(Settings.RotationMinutes, 5, 1440));
        _timer = new System.Threading.Timer(async _ => await RotateAsync(), null, interval, interval);
    }

    public async Task RotateAsync()
    {
        if (!await _rotationLock.WaitAsync(0)) return;
        try
        {
            var localImages = _source.Scan(Settings.LocalImageFolder);
            var approvedImages = _source.Scan(AppPaths.Approved);
            var approvedMetadata = await _store.LoadAsync(AppPaths.ApprovedMetadata, () => new List<ApprovedImageMetadata>());
            var favoritePaths = approvedMetadata.Where(x => x.IsAvailable && x.IsFavorite)
                .Select(x => x.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var images = localImages.Concat(approvedImages).ToList();
            images.AddRange(approvedImages.Where(x => favoritePaths.Contains(x.FilePath)));
            if (images.Count == 0)
            {
                StatusChanged?.Invoke(this, "Choose a local folder or approve an online image to begin.");
                return;
            }

            var recent = _history.TakeLast(Settings.RecentImageLimit).Select(x => x.WallpaperId).ToHashSet();
            var monitors = _desktop.GetMonitors();
            WallpaperItem? shared = null;
            foreach (var monitor in monitors)
            {
                var candidates = images.Where(x => x.IsPortrait == monitor.IsPortrait).ToList();
                if (candidates.Count == 0) candidates = images.ToList();
                var fresh = candidates.Where(x => !recent.Contains(x.Id)).ToList();
                if (Settings.AvoidRecentImages && fresh.Count > 0) candidates = fresh;

                var selected = Settings.UseDifferentImagePerMonitor || shared is null
                    ? candidates[_random.Next(candidates.Count)]
                    : shared;
                shared ??= selected;
                var metadata = approvedMetadata.FirstOrDefault(x => x.IsAvailable &&
                    string.Equals(x.FilePath, selected.FilePath, StringComparison.OrdinalIgnoreCase));
                var wallpaperPath = metadata is null
                    ? selected.FilePath
                    : _captions.Create(selected.FilePath, metadata, monitor);
                _desktop.Set(monitor.Id, wallpaperPath);
                _history.Add(new HistoryEntry(selected.Id, selected.FilePath, monitor.Id, DateTimeOffset.Now));
                recent.Add(selected.Id);
            }

            _history = _history.TakeLast(500).ToList();
            await _store.SaveAsync(AppPaths.History, _history);
            StatusChanged?.Invoke(this, $"Wallpaper changed at {DateTime.Now:t}.");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            StatusChanged?.Invoke(this, "Could not change the wallpaper. See the log for details.");
        }
        finally { _rotationLock.Release(); }
    }

    public string MonitorSummary()
    {
        try
        {
            var monitors = _desktop.GetMonitors();
            if (monitors.Count == 0) return "No active monitors detected.";
            var descriptions = monitors.Select((m, i) =>
                $"Monitor {i + 1}: {m.Width:N0} × {m.Height:N0} ({(m.IsPortrait ? "portrait" : "landscape")})");
            return string.Join("   ·   ", descriptions);
        }
        catch (Exception ex) { AppLog.Error(ex); return "Monitor information will appear when running on Windows."; }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _desktop.Dispose();
        _rotationLock.Dispose();
    }
}
