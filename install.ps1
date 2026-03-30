# WiFi QR Scanner — Installer
# Usage (run as Administrator):
#   irm https://raw.githubusercontent.com/YOUR_USERNAME/wifi-qr-scanner/main/install.ps1 | iex

param(
    [string]$Repo = "mkster/wifi-qr-scanner",
    [string]$InstallDir = "$env:ProgramFiles\WifiQrScanner"
)

$ErrorActionPreference = "Stop"

# ── Elevation check ────────────────────────────────────────────────────────────
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Relaunching as Administrator..." -ForegroundColor Yellow
    Start-Process powershell "-ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

Write-Host ""
Write-Host "  WiFi QR Scanner — Installer" -ForegroundColor Cyan
Write-Host "  ─────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

# ── Download latest release ────────────────────────────────────────────────────
Write-Host "[1/4] Fetching latest release..." -ForegroundColor Gray
$release  = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest"
$asset    = $release.assets | Where-Object { $_.name -eq "WifiQrScanner.zip" } | Select-Object -First 1

if (-not $asset) {
    Write-Host "Could not find WifiQrScanner.zip in the latest release." -ForegroundColor Red
    exit 1
}

$version  = $release.tag_name
$zipPath  = "$env:TEMP\WifiQrScanner.zip"
$extractPath = "$env:TEMP\WifiQrScanner_install"

Write-Host "[2/4] Downloading $version..." -ForegroundColor Gray
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing

# ── Extract ────────────────────────────────────────────────────────────────────
Write-Host "[3/4] Installing to $InstallDir..." -ForegroundColor Gray
Remove-Item $extractPath -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force
Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
Move-Item $extractPath $InstallDir -Force
Get-ChildItem $InstallDir -Recurse | Unblock-File

# ── Start Menu shortcut ────────────────────────────────────────────────────────
Write-Host "[4/4] Creating Start Menu shortcut..." -ForegroundColor Gray
$lnkPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\WiFi QR Scanner.lnk"
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut($lnkPath)
$lnk.TargetPath       = "$InstallDir\WifiQrScanner.exe"
$lnk.WorkingDirectory = $InstallDir
$lnk.Description      = "Scan a WiFi QR code and connect automatically"
$lnk.IconLocation     = "$InstallDir\WifiQrScanner.exe"
$lnk.Save()

# ── Cleanup ────────────────────────────────────────────────────────────────────
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "  Installed $version successfully. Launching..." -ForegroundColor Green
Write-Host ""

Start-Process "$InstallDir\WifiQrScanner.exe"
