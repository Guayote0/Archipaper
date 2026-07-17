namespace Archipaper.Models;

public sealed record HistoryEntry(string WallpaperId, string FilePath, string MonitorId, DateTimeOffset AppliedAt);
