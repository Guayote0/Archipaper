# Archipaper

Archipaper is a native Windows wallpaper application designed for architecture imagery and mixed-orientation, multi-monitor setups.

## Current build (1.0)

- Native Windows 10/11 desktop application
- Independent wallpaper assignment per monitor
- Automatic landscape/portrait image matching
- Hourly rotation by default, with selectable intervals
- Local high-resolution JPG, PNG, and BMP image collections
- Explicit rotation source: approved collection, chosen folder, or both
- Recent-image avoidance
- Preferences and history saved safely in `%LOCALAPPDATA%\Archipaper`
- Notification-area controls and optional Windows startup
- Crash logging without taking down the rotation scheduler
- Online discovery from Wikimedia Commons without an API key
- Experimental Openverse discovery across additional openly licensed collections
- Experimental Library of Congress discovery focused on architectural drawings and sketches
- Source switches for Wikimedia Commons, Openverse, and Library of Congress drawings
- Semi-automatic approval/rejection queue with local previews
- Non-destructive queue skipping
- Full-resolution downloads only after approval
- Cached-preview fallback when a full-resolution download is unavailable
- Editable architect and project-name captions for approved online wallpapers
- Smaller, more discreet wallpaper captions
- Main-window credit for Iván Castro
- Source, contributor, license, and license-link preservation
- Exact-name architect-only search mode
- Selectable architecture categories without the former Parametric Architecture category
- Reliable weighted discovery that performs additional searches and requests more results for featured architects
- Persistent preferred-architect checklist with add-more support
- Approved-image thumbnail gallery with favorites and source links
- Recoverable removal from wallpaper rotation
- Active monitor resolution/orientation summary
- Single-instance protection and silent background startup
- Branded executable icon and optional per-user installer

The selected architects are built in, with additional discovery weighting for Steven Holl and Enric Miralles. Names remain available after they are unchecked and can be selected again later. Online discovery and review are isolated from the wallpaper engine, so local and previously approved wallpapers continue to rotate when the network is unavailable.

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

Archipaper does not upload images or send analytics. Online discovery queries Wikimedia Commons and, when enabled, Openverse and the Library of Congress. It downloads only previews until the user approves an image. Source and contributor/license attribution are retained locally for every approved image. Openverse aggregates third-party metadata, and Library of Congress records can have item-specific rights advisories, so users should verify the license or rights note on each original source page.
