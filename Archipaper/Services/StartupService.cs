using Microsoft.Win32;

namespace Archipaper.Services;

public static class StartupService
{
    private const string KeyName = "Archipaper";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true)
            ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        if (enabled)
            key.SetValue(KeyName, $"\"{Environment.ProcessPath}\" --background");
        else
            key.DeleteValue(KeyName, false);
    }
}
