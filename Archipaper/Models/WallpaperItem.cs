namespace Archipaper.Models;

public sealed record WallpaperItem(
    string Id,
    string FilePath,
    string Title,
    string Credit,
    int Width,
    int Height)
{
    public bool IsPortrait => Height > Width;
}
