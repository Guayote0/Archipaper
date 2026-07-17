namespace Archipaper.Models;

public sealed class AppSettings
{
    public int RotationMinutes { get; set; } = 60;
    public bool StartWithWindows { get; set; } = true;
    public bool RotateAutomatically { get; set; } = true;
    public bool UseDifferentImagePerMonitor { get; set; } = true;
    public bool AvoidRecentImages { get; set; } = true;
    public int RecentImageLimit { get; set; } = 40;
    public string LocalImageFolder { get; set; } = "";
    public List<string> EnabledCategories { get; set; } =
        ["Buildings", "Interiors", "Details", "Drawings", "Models", "Parametric Architecture"];
    public List<string> PreferredArchitects { get; set; } =
        ["Steven Holl", "Enric Miralles", "Carlo Scarpa", "Allied Works", "Renzo Piano", "Gaudí", "Santiago Calatrava", "Louis Kahn"];
    public List<string> BoostedArchitects { get; set; } = ["Steven Holl", "Enric Miralles"];
}
