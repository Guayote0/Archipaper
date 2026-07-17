namespace Archipaper.Models;

public enum ReviewState { Pending, Approved, Rejected }

public sealed class OnlineCandidate
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string ArchitectOrCategory { get; set; } = "";
    public string Artist { get; set; } = "Unknown contributor";
    public string License { get; set; } = "";
    public string LicenseUrl { get; set; } = "";
    public string SourcePageUrl { get; set; } = "";
    public string OriginalUrl { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public string PreviewFilePath { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public ReviewState State { get; set; } = ReviewState.Pending;
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.Now;
}

public sealed class ApprovedImageMetadata
{
    public string Id { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string License { get; set; } = "";
    public string LicenseUrl { get; set; } = "";
    public string SourcePageUrl { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsAvailable { get; set; } = true;

    public string DisplayTitle => (IsFavorite ? "★  " : "") + Title;
}
