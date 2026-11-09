using Archipaper.Models;
namespace Archipaper.Services;

public static class WallpaperSelector
{
    public static WallpaperItem? Select(IReadOnlyList<WallpaperItem> images, bool portrait, string? current,
        ISet<string> used, ISet<string> recent, ISet<string> favorites, bool different, bool avoidRecent, Random random)
    {
        var choices = images.DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
        choices = Prefer(choices, x => !different || !used.Contains(x.Id));
        choices = Prefer(choices, x => x.IsPortrait == portrait);
        choices = Prefer(choices, x => !string.Equals(x.Id, current, StringComparison.OrdinalIgnoreCase));
        if (avoidRecent) choices = Prefer(choices, x => !recent.Contains(x.Id));
        if (choices.Count == 0) return null;
        var ticket = random.Next(choices.Sum(x => favorites.Contains(x.FilePath) ? 2 : 1));
        foreach (var item in choices) if ((ticket -= favorites.Contains(item.FilePath) ? 2 : 1) < 0) return item;
        return choices[^1];
    }
    private static List<WallpaperItem> Prefer(List<WallpaperItem> items, Func<WallpaperItem, bool> predicate)
    {
        var preferred = items.Where(predicate).ToList();
        return preferred.Count > 0 ? preferred : items;
    }
}
