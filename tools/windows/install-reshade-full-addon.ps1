[CmdletBinding()]
param(
    [switch]$DownloadOnly
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# ReShade's official full add-on support installer. Keep this version pinned
# so the package remains reproducible; update it when ReShade publishes a new release.
$version = "6.8.0"
$uri = "https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe"
$downloadDirectory = Join-Path $env:LOCALAPPDATA "FSR4GFX803\downloads"
$installer = Join-Path $downloadDirectory ("ReShade_Setup_" + $version + "_Addon.exe")

New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

# Windows PowerShell 5.1 may otherwise negotiate older TLS defaults.
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
} catch {
    # PowerShell 7 already uses modern TLS defaults.
}

Write-Host "Downloading the official ReShade $version full add-on support installer..."
Write-Host "Source: $uri"
Invoke-WebRequest -Uri $uri -OutFile $installer -UseBasicParsing

$file = Get-Item -LiteralPath $installer
if ($file.Length -lt 1MB) {
    Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
    throw "The downloaded installer is unexpectedly small. The download may have failed."
}

Write-Host "Downloaded: $($file.FullName)"
Write-Host "Note: ReShade's full add-on build is unsigned by design. The file came from reshade.me."

if (-not $DownloadOnly) {
    Write-Host "Launching the ReShade installer. Select your game's EXE and DirectX 12 when prompted."
    $process = Start-Process -FilePath $installer -Wait -PassThru
    Write-Host "ReShade setup closed with exit code $($process.ExitCode)."
}

exit 0
