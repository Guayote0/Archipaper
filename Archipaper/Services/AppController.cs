using System.Drawing;
using System.Windows;
using Archipaper.Models;
using Archipaper.Views;
using Forms = System.Windows.Forms;

namespace Archipaper.Services;

public sealed class AppController : IDisposable
{
    private readonly JsonStore _store;
    private readonly RotationService _rotation;
    private readonly ReviewQueueService _reviewQueue;
    private readonly Forms.NotifyIcon _tray;
    private MainWindow? _window;

    private AppController(JsonStore store, RotationService rotation, ReviewQueueService reviewQueue)
    {
        _store = store;
        _rotation = rotation;
        _reviewQueue = reviewQueue;
        _tray = new Forms.NotifyIcon
        {
            Text = "Archipaper",
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
    }

    public static async Task<AppController> CreateAsync()
    {
        AppPaths.EnsureCreated();
        var store = new JsonStore();
        var settings = await store.LoadAsync(AppPaths.Settings, () => new AppSettings());
        var history = await store.LoadAsync(AppPaths.History, () => new List<HistoryEntry>());
        var reviewQueue = await ReviewQueueService.CreateAsync(store);
        return new AppController(store, new RotationService(settings, history, store), reviewQueue);
    }

    public void Start(bool showWindow)
    {
        _rotation.StartTimer();
        if (showWindow) ShowWindow();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Change wallpaper now", null, async (_, _) => await _rotation.RotateAsync());
        menu.Items.Add("Open Archipaper", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _window?.CloseForExit();
            Application.Current.Shutdown();
        });
        return menu;
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow(_rotation, _reviewQueue, SaveSettingsAsync);
            _window.Closed += (_, _) => _window = null;
        }
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async Task SaveSettingsAsync()
    {
        await _store.SaveAsync(AppPaths.Settings, _rotation.Settings);
        StartupService.SetEnabled(_rotation.Settings.StartWithWindows);
        _rotation.StartTimer();
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _rotation.Dispose();
        _reviewQueue.Dispose();
    }
}
