$ErrorActionPreference = "Stop"

foreach ($f in "Interop.wgssSTU.dll", "wgssSTU.dll") {
    if (-not (Test-Path ".\lib\$f")) {
        Write-Host ""
        Write-Host "Missing .\lib\$f" -ForegroundColor Red
        Write-Host "Copy the x64 $f from the Wacom STU SDK into the lib folder:"
        Write-Host "  C:\Program Files (x86)\Wacom STU SDK\COM\bin\x64\$f"
        Write-Host ""
        exit 1
    }
}

dotnet --version
dotnet build -c Release

Write-Host ""
Write-Host "Build complete."
Write-Host "EXE: .\bin\Release\net7.0-windows\WacomStuMouseBridge.exe"
Write-Host "Keep wgssSTU.dll next to the EXE if you copy it elsewhere."
