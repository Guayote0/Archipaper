using System.Runtime.InteropServices;

namespace Archipaper.Services;

public sealed class DesktopWallpaperService : IDisposable
{
    private readonly IDesktopWallpaper _desktop = (IDesktopWallpaper)new DesktopWallpaperCom();

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var count = _desktop.GetMonitorDevicePathCount();
        var result = new List<MonitorInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var id = _desktop.GetMonitorDevicePathAt(i);
            _desktop.GetMonitorRECT(id, out var bounds);
            result.Add(new MonitorInfo(id, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        }
        return result;
    }

    public void Set(string monitorId, string imagePath)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("Wallpaper image was not found.", imagePath);
        _desktop.SetPosition(DesktopWallpaperPosition.Fill);
        _desktop.SetWallpaper(monitorId, Path.GetFullPath(imagePath));
    }

    public void Dispose()
    {
        if (Marshal.IsComObject(_desktop)) Marshal.FinalReleaseComObject(_desktop);
    }
}

public sealed record MonitorInfo(string Id, int Width, int Height)
{
    public bool IsPortrait => Height > Width;
}

internal enum DesktopWallpaperPosition { Center, Tile, Stretch, Fit, Fill, Span }

[ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
internal class DesktopWallpaperCom { }

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
    void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
    void GetMonitorDevicePathCount(out uint count);
    void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out NativeRect displayRect);
    void SetBackgroundColor(uint color);
    uint GetBackgroundColor();
    void SetPosition(DesktopWallpaperPosition position);
    DesktopWallpaperPosition GetPosition();
    void SetSlideshow(IntPtr items);
    IntPtr GetSlideshow();
    void SetSlideshowOptions(int options, uint slideshowTick);
    void GetSlideshowOptions(out int options, out uint slideshowTick);
    void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorId, int direction);
    int GetStatus();
    [return: MarshalAs(UnmanagedType.Bool)] bool Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect { public int Left, Top, Right, Bottom; }

internal static class DesktopWallpaperExtensions
{
    public static uint GetMonitorDevicePathCount(this IDesktopWallpaper desktop)
    {
        desktop.GetMonitorDevicePathCount(out var count);
        return count;
    }

    public static string GetMonitorDevicePathAt(this IDesktopWallpaper desktop, uint index)
    {
        desktop.GetMonitorDevicePathAt(index, out var id);
        return id;
    }
}
