$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Archipaper\Archipaper.csproj"
$release = Join-Path $PSScriptRoot "release"

Write-Host "Building Archipaper for Windows..." -ForegroundColor Cyan
dotnet restore $project
dotnet publish $project -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o $release

Write-Host ""
Write-Host "Done: $release\Archipaper.exe" -ForegroundColor Green

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
  $candidate = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
  if (Test-Path $candidate) { $iscc = Get-Item $candidate }
}

if ($iscc) {
  Write-Host "Creating the Archipaper installer..." -ForegroundColor Cyan
  & $iscc (Join-Path $PSScriptRoot "installer.iss")
  Write-Host "Done: $PSScriptRoot\installer-output\Archipaper-Setup.exe" -ForegroundColor Green
} else {
  Write-Host "Inno Setup was not found; the portable Archipaper.exe build is complete." -ForegroundColor Yellow
}
