# Builds a release zip: self-contained single-file x64 EXE, WITHOUT Wacom's proprietary DLLs.
#   .\release.ps1            -> uses <Version> from the csproj
#   .\release.ps1 -Version 1.0.1
param([string]$Version)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not $Version) {
    $Version = ([xml](Get-Content .\WacomStuMouseBridge.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

$name = "wacom-bridge-v$Version-win-x64"
$out = ".\release\$name"
$zip = ".\release\$name.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

dotnet publish -c Release -r win-x64 --self-contained true -o $out `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Wacom's DLLs are proprietary: users copy them from their own STU SDK install.
Remove-Item "$out\wgssSTU.dll", "$out\Interop.wgssSTU.dll" -ErrorAction SilentlyContinue

Copy-Item ..\CHANGELOG.md $out
@"
wacom-bridge v$Version
======================

Sign on a Wacom STU-430 and draw on mouse-driven web signature canvases.
Full guide: https://github.com/wartle/wacom-bridge

BEFORE FIRST RUN - copy two files from the Wacom STU SDK into this folder:

  C:\Program Files (x86)\Wacom STU SDK\COM\bin\x64\Interop.wgssSTU.dll
  C:\Program Files (x86)\Wacom STU SDK\COM\bin\x64\wgssSTU.dll

They are Wacom's proprietary files and are not included in this download.
Get the SDK from https://developer.wacom.com/

Then run WacomStuMouseBridge.exe. No .NET install or admin rights needed.
Keep this folder somewhere writable (not C:\Program Files) so calibration
can be saved to wacom-bridge.conf.

Keys: F8/F9 signature box corners, F4/F5/F6 website Cancel/Clear/Save,
      F10 enable/disable, F7 wipe pad, Esc quit.
"@ | Set-Content "$out\README.txt" -Encoding UTF8

Compress-Archive -Path $out -DestinationPath $zip

Write-Host ""
Write-Host "Release zip: $zip"
Get-ChildItem $out | Format-Table Name, Length -AutoSize
