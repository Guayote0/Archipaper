namespace Archipaper.Models;

public sealed class AppSettings
{
    public const string ApprovedOnly = "Approved";
    public const string LocalOnly = "Local";
    public const string ApprovedAndLocal = "Both";

    public int RotationMinutes { get; set; } = 60;
    public bool StartWithWindows { get; set; } = true;
    public bool RotateAutomatically { get; set; } = true;
    public bool UseDifferentImagePerMonitor { get; set; } = true;
    public bool AvoidRecentImages { get; set; } = true;
    public int RecentImageLimit { get; set; } = 40;
    public string WallpaperSourceMode { get; set; } = ApprovedAndLocal;
    public string LocalImageFolder { get; set; } = "";
    public bool SearchWikimedia { get; set; } = true;
    public bool SearchOpenverse { get; set; }
    public bool SearchLibraryOfCongress { get; set; } = true;
    public bool StrictArchitectSearch { get; set; } = true;
    public List<string> EnabledCategories { get; set; } =
        ["Buildings", "Interiors", "Details", "Drawings", "Models"];
    public List<string> AvailableArchitects { get; set; } =
        ["Steven Holl", "Enric Miralles", "Carlo Scarpa", "Allied Works", "Renzo Piano", "Gaudí", "Santiago Calatrava", "Louis Kahn"];
    public List<string> PreferredArchitects { get; set; } =
        ["Steven Holl", "Enric Miralles", "Carlo Scarpa", "Allied Works", "Renzo Piano", "Gaudí", "Santiago Calatrava", "Louis Kahn"];
}
