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
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (File.Exists(AppPaths.Log) && new FileInfo(AppPaths.Log).Length > 2 * 1024 * 1024) File.Move(AppPaths.Log, AppPaths.Log + ".1", true);
                File.AppendAllText(AppPaths.Log, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* Logging must never crash wallpaper rotation. */ }
    }
}
