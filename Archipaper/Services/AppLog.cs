namespace Archipaper.Services;

public static class AppLog
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(Exception exception) => Write("ERROR", exception.ToString());

    private static void Write(string level, string message)
    {
        try
        {
            AppPaths.EnsureCreated();
            lock (Gate)
                File.AppendAllText(AppPaths.Log, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
        }
        catch { /* Logging must never crash wallpaper rotation. */ }
    }
}
