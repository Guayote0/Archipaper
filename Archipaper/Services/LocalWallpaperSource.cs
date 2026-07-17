using System.Windows.Media.Imaging;
using Archipaper.Models;

namespace Archipaper.Services;

public sealed class LocalWallpaperSource
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp" };

    public IReadOnlyList<WallpaperItem> Scan(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];
        var result = new List<WallpaperItem>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                     .Where(p => Extensions.Contains(Path.GetExtension(p))))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                result.Add(new WallpaperItem(path, path, Path.GetFileNameWithoutExtension(path), "Local collection", frame.PixelWidth, frame.PixelHeight));
            }
            catch (Exception ex) { AppLog.Error(ex); }
        }
        return result;
    }
}
