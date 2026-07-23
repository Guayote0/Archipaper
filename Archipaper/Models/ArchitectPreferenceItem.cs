namespace Archipaper.Models;

public sealed class ArchitectPreferenceItem
{
    public string Name { get; init; } = "";
    public bool IsEnabled { get; set; }
    public bool IsBoosted { get; set; }
}
