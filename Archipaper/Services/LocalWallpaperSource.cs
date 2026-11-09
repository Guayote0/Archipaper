using Archipaper.Models;
namespace Archipaper.Services;

public sealed class LocalWallpaperSource
{
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<WallpaperItem> Scan(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];
        folder = Path.GetFullPath(folder);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var images = new List<WallpaperItem>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", new EnumerationOptions
            { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System }))
            {
                if (!ImageValidation.Extensions.Contains(Path.GetExtension(path))) continue;
                seen.Add(path);
                try
                {
                    var file = new FileInfo(path);
                    if (_cache.TryGetValue(path, out var cached) && cached.Size == file.Length && cached.Modified == file.LastWriteTimeUtc)
                    { if (cached.Image is not null) images.Add(cached.Image); continue; }
                    WallpaperItem? image = null;
                    try
                    {
                        var info = ImageValidation.Inspect(path);
                        image = new(path, path, Path.GetFileNameWithoutExtension(path), "Local collection", info.Width, info.Height);
                        images.Add(image);
                    }
                    catch (Exception ex) { AppLog.Error(ex); }
                    _cache[path] = new(file.Length, file.LastWriteTimeUtc, image, false);
                }
                catch (Exception ex) { AppLog.Error(ex); }
            }
        }
        catch (Exception ex) { AppLog.Error(ex); }
        var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        foreach (var key in _cache.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !seen.Contains(x)).ToList()) _cache.Remove(key);
        return images;
    }
    public void ValidateForUse(string path)
    {
        var file = new FileInfo(path);
        if (_cache.TryGetValue(path, out var entry) && entry.Validated && entry.Size == file.Length && entry.Modified == file.LastWriteTimeUtc) return;
        try { ImageValidation.Inspect(path, decode: true); if (entry is not null) _cache[path] = entry with { Validated = true }; }
        catch { if (entry is not null) _cache[path] = entry with { Image = null }; throw; }
    }
    private sealed record Entry(long Size, DateTime Modified, WallpaperItem? Image, bool Validated);
}
