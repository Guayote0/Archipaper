using Archipaper.Models;
using Microsoft.Win32;
using System.Windows.Threading;

namespace Archipaper.Services;

public sealed class RotationService : IDisposable, IAsyncDisposable
{
    private readonly JsonStore _store;
    private readonly RotationSchedule _schedule;
    private readonly List<HistoryEntry> _history;
    private readonly Func<IWallpaperDesktop> _createDesktop;
    private readonly LocalWallpaperSource _source = new();
    private readonly CaptionedWallpaperService _captions = new();
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IWallpaperDesktop? _desktop;
    private DispatcherTimer? _timer;
    private AppSettings _active;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private bool _busy;
    private volatile bool _disposed;
    public AppSettings Settings { get; }
    public event EventHandler<string>? StatusChanged;

    public RotationService(AppSettings settings, List<HistoryEntry> history, JsonStore store, RotationSchedule schedule,
        Func<IWallpaperDesktop>? createDesktop = null)
    {
        Settings = settings; _active = settings.Snapshot(); _history = history.TakeLast(500).ToList();
        _store = store; _schedule = schedule; _createDesktop = createDesktop ?? (() => new DesktopWallpaperService());
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
                _timer.Tick += OnTick;
                _ready.TrySetResult(dispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { _ready.TrySetException(ex); AppLog.Error(ex); }
            finally { _desktop?.Dispose(); _stopped.TrySetResult(); }
        }) { IsBackground = true, Name = "Archipaper rotation" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.DisplaySettingsChanged += OnEnvironmentChanged;
        SystemEvents.TimeChanged += OnEnvironmentChanged;
    }
    public Task StartTimerAsync(bool rotateNow = false)
    {
        var snapshot = Settings.Snapshot();
        return DispatchAsync(async () =>
        {
            _active = snapshot;
            _schedule.Configure(snapshot.RotationMinutes, DateTimeOffset.UtcNow);
            await CheckAsync(rotateNow);
        });
    }
    public Task RotateAsync() => DispatchAsync(() => CheckAsync(true));
    private async Task DispatchAsync(Func<Task> action)
    {
        if (_disposed) return;
        var dispatcher = await _ready.Task.ConfigureAwait(false);
        if (!_disposed) await dispatcher.InvokeAsync(action).Task.Unwrap().ConfigureAwait(false);
    }
    private async void OnTick(object? sender, EventArgs e) => await CheckAsync(false);
    private void OnPower(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Resume) OnEnvironmentChanged(sender, EventArgs.Empty); }
    private async void OnEnvironmentChanged(object? sender, EventArgs e)
    {
        try { await DispatchAsync(() => CheckAsync(false)); }
        catch (Exception ex) { if (!_disposed) AppLog.Error(ex); }
    }
    private async Task CheckAsync(bool manual)
    {
        if (_disposed || _busy) return;
        _busy = true; _timer!.Stop();
        var globalFailure = false;
        try
        {
            var now = DateTimeOffset.UtcNow;
            _schedule.Configure(_active.RotationMinutes, now);
            _desktop ??= _createDesktop();
            _monitors = _desktop.GetMonitors();
            foreach (var monitor in _monitors) _schedule.Ensure(monitor.Id, now);
            var targets = _monitors.Where(m => manual || (_active.RotateAutomatically && _schedule.IsDue(m.Id, now))).ToList();
            if (targets.Count == 0) return;
            var settings = _active;
            var images = await Task.Run(() =>
            {
                var local = settings.WallpaperSourceMode is AppSettings.LocalOnly or AppSettings.ApprovedAndLocal ? _source.Scan(settings.LocalImageFolder) : [];
                var approved = settings.WallpaperSourceMode is AppSettings.ApprovedOnly or AppSettings.ApprovedAndLocal ? _source.Scan(AppPaths.Approved) : [];
                return local.Concat(approved).DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
            });
            var metadata = await _store.LoadAsync(AppPaths.ApprovedMetadata, () => new List<ApprovedImageMetadata>());
            var byPath = metadata.Where(x => x.IsAvailable).DistinctBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
            var favorites = byPath.Values.Where(x => x.IsFavorite).Select(x => x.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var recent = _history.TakeLast(settings.RecentImageLimit).Select(x => x.WallpaperId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var used = _monitors.Except(targets).Select(m => _history.LastOrDefault(h => h.MonitorId == m.Id)?.WallpaperId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            WallpaperItem? shared = null;
            var successful = 0;
            foreach (var monitor in targets)
            {
                if (_disposed) break;
                try
                {
                    var current = _history.LastOrDefault(h => h.MonitorId == monitor.Id)?.WallpaperId;
                    WallpaperItem? selected = null;
                    string? path = null;
                    for (var attempt = 0; attempt < 3 && path is null; attempt++)
                    {
                        selected = !settings.UseDifferentImagePerMonitor && shared is not null ? shared :
                            WallpaperSelector.Select(images, monitor.IsPortrait, current, used, recent, favorites,
                                settings.UseDifferentImagePerMonitor, settings.AvoidRecentImages, Random.Shared);
                        if (selected is null) break;
                        try
                        {
                            var item = selected;
                            path = await Task.Run(() =>
                            {
                                _source.ValidateForUse(item.FilePath);
                                return byPath.TryGetValue(item.FilePath, out var info) ? _captions.Create(item.FilePath, info, monitor) : item.FilePath;
                            });
                        }
                        catch (Exception ex)
                        {
                            AppLog.Error(ex); images.RemoveAll(x => x.Id == selected.Id);
                            if (shared?.Id == selected.Id) shared = null;
                        }
                    }
                    if (path is null || selected is null) throw new IOException("No usable wallpaper images in the selected collection.");
                    if (_disposed) break;
                    _desktop ??= _createDesktop();
                    _desktop.Set(monitor.Id, path);
                    var applied = DateTimeOffset.UtcNow;
                    _history.Add(new(selected.Id, selected.FilePath, monitor.Id, applied));
                    _schedule.Succeeded(monitor.Id, applied);
                    used.Add(selected.Id); recent.Add(selected.Id); shared ??= selected; successful++;
                    AppLog.Info($"Wallpaper applied: {monitor.Width}x{monitor.Height}, {Path.GetFileName(selected.FilePath)}.");
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex); _schedule.Failed(monitor.Id, DateTimeOffset.UtcNow);
                    _desktop?.Dispose(); _desktop = null;
                }
            }
            if (_history.Count > 500) _history.RemoveRange(0, _history.Count - 500);
            await _store.SaveAsync(AppPaths.History, _history);
            StatusChanged?.Invoke(this, successful == targets.Count ? $"Wallpaper changed at {DateTime.Now:t}." :
                $"Updated {successful}/{targets.Count} monitors. Check your collection; automatic rotation will retry failed monitors.");
        }
        catch (Exception ex)
        {
            globalFailure = true; AppLog.Error(ex); _desktop?.Dispose(); _desktop = null;
            StatusChanged?.Invoke(this, "Wallpaper change failed. Automatic rotation will retry; see the log for details.");
        }
        finally
        {
            try { await _store.SaveAsync(AppPaths.RotationState, _schedule); }
            catch (Exception ex) { AppLog.Error(ex); }
            _busy = false;
            if (_disposed) Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.ApplicationIdle);
            else if (_active.RotateAutomatically)
            {
                var now = DateTimeOffset.UtcNow;
                var next = globalFailure || _monitors.Count == 0 ? now.AddMinutes(1) : _monitors.Min(m => _schedule.Ensure(m.Id, now).DueAt);
                _timer.Interval = next > now ? next - now : TimeSpan.FromMilliseconds(1);
                _timer.Start();
            }
        }
    }
    public string MonitorSummary()
    {
        try
        {
            using var desktop = _createDesktop();
            return string.Join("   ·   ", desktop.GetMonitors().Select((m, i) => $"Monitor {i + 1}: {m.Width:N0} × {m.Height:N0} ({(m.IsPortrait ? "portrait" : "landscape")})"));
        }
        catch (Exception ex) { AppLog.Error(ex); return "Monitor information temporarily unavailable."; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.DisplaySettingsChanged -= OnEnvironmentChanged;
        SystemEvents.TimeChanged -= OnEnvironmentChanged;
        _ = StopAsync();
    }
    private async Task StopAsync()
    {
        try
        {
            var dispatcher = await _ready.Task.ConfigureAwait(false);
            await dispatcher.InvokeAsync(() =>
            {
                _timer!.Stop();
                if (!_busy) dispatcher.BeginInvokeShutdown(DispatcherPriority.ApplicationIdle);
            });
        }
        catch (Exception ex) { AppLog.Error(ex); }
    }
    public async ValueTask DisposeAsync() { Dispose(); await _stopped.Task.ConfigureAwait(false); }
}
