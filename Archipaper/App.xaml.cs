global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using System.IO;
global using System.Net.Http;
using System.Windows;
using Archipaper.Services;

namespace Archipaper;

public partial class App : Application
{
    private AppController? _controller;
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\Archipaper.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Archipaper is already running in the notification area.", "Archipaper");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error(args.Exception);
            MessageBox.Show("Archipaper encountered a problem and recorded it in the log.", "Archipaper");
            args.Handled = true;
        };

        _controller = await AppController.CreateAsync();
        _controller.Start(!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
