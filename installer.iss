#define MyAppName "Archipaper"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Archipaper"
#define MyAppExeName "Archipaper.exe"

[Setup]
AppId={{A7D824CB-DA91-4E47-9CE2-313D23B6CE01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Archipaper
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=installer-output
OutputBaseFilename=Archipaper-Setup
SetupIconFile=Archipaper\Assets\archipaper.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "release\Archipaper.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Archipaper"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\Archipaper"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Open Archipaper"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
