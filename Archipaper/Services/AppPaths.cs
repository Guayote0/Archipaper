namespace Archipaper.Services;

public static class AppPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Archipaper");
    public static readonly string Settings = Path.Combine(Root, "settings.json");
    public static readonly string History = Path.Combine(Root, "history.json");
    public static readonly string Cache = Path.Combine(Root, "cache");
    public static readonly string CandidateCache = Path.Combine(Cache, "candidates");
    public static readonly string CaptionCache = Path.Combine(Cache, "captions");
    public static readonly string Approved = Path.Combine(Root, "approved");
    public static readonly string Removed = Path.Combine(Root, "removed");
    public static readonly string ReviewQueue = Path.Combine(Root, "review-queue.json");
    public static readonly string ApprovedMetadata = Path.Combine(Root, "approved-images.json");
    public static readonly string Log = Path.Combine(Root, "archipaper.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(CandidateCache);
        Directory.CreateDirectory(CaptionCache);
        Directory.CreateDirectory(Approved);
        Directory.CreateDirectory(Removed);
    }
}
