# Archipaper

Archipaper is a native Windows wallpaper application designed for architecture imagery and mixed-orientation, multi-monitor setups.

## Current build (0.3)

- Native Windows 10/11 desktop application
- Independent wallpaper assignment per monitor
- Automatic landscape/portrait image matching
- Hourly rotation by default, with selectable intervals
- Local high-resolution JPG, PNG, and BMP image collections
- Recent-image avoidance
- Preferences and history saved safely in `%LOCALAPPDATA%\Archipaper`
- Notification-area controls and optional Windows startup
- Crash logging without taking down the rotation scheduler
- Online discovery from Wikimedia Commons without an API key
- Semi-automatic approval/rejection queue with local previews
- Full-resolution downloads only after approval
- Source, contributor, license, and license-link preservation
- Selectable architecture categories
- Weighted discovery for Steven Holl and Enric Miralles
- Editable preferred and boosted architect lists
- Approved-image library with favorites and source links
- Recoverable removal from wallpaper rotation
- Recent wallpaper history
- Active monitor resolution/orientation summary
- Single-instance protection and silent background startup
- Branded executable icon and optional per-user installer

The selected architects are built in, with additional discovery weighting for Steven Holl and Enric Miralles. Online discovery and review are isolated from the wallpaper engine, so local and previously approved wallpapers continue to rotate when the network is unavailable.

## Build on Windows

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Open PowerShell in this project folder.
3. Run `powershell -ExecutionPolicy Bypass -File .\build.ps1`.
4. Open `release\Archipaper.exe`. If Inno Setup is installed, the script also creates `installer-output\Archipaper-Setup.exe`.

No administrator access is required. Windows may show a SmartScreen warning until the application is code-signed.

## Development

```powershell
dotnet restore .\Archipaper\Archipaper.csproj
dotnet run --project .\Archipaper\Archipaper.csproj
```

## Privacy and credits

Archipaper does not upload images or send analytics. Online discovery queries Wikimedia Commons and downloads only previews until the user approves an image. Source and contributor/license attribution are retained locally for every approved image.
