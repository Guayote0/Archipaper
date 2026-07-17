using System.Text.RegularExpressions;

namespace Archipaper.Services;

public static class ArchitectureMetadata
{
    public static string CleanProjectName(string title, string architect = "")
    {
        var clean = Path.GetFileNameWithoutExtension(title)
            .Replace('_', ' ')
            .Replace(" - ", " ")
            .Replace(" – ", " ")
            .Replace(" — ", " ")
            .Trim();

        if (!string.IsNullOrWhiteSpace(architect))
        {
            clean = Regex.Replace(clean, Regex.Escape(architect), "", RegexOptions.IgnoreCase).Trim();
            clean = Regex.Replace(clean, @"^[\s\-\–\—,;:]+|[\s\-\–\—,;:]+$", "").Trim();
        }

        clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();
        return string.IsNullOrWhiteSpace(clean) ? Path.GetFileNameWithoutExtension(title).Replace('_', ' ') : clean;
    }
}
