<#
.SYNOPSIS
    Builds and signs WifiQrScanner.msix for manual upload to the Microsoft Store.
    Mirrors the GitHub Actions release workflow exactly.
#>

$ErrorActionPreference = "Stop"
$ProjectRoot = Resolve-Path "$PSScriptRoot\.."
$PublishDir  = "$ProjectRoot\publish"
$StageDir    = "$ProjectRoot\msix-stage"
$OutputMsix  = "$ProjectRoot\WifiQrScanner.msix"

# ── 1. Bump version in manifest ───────────────────────────────────────────────
Write-Host "[1/5] Bumping version..." -ForegroundColor Cyan
$manifestPath = "$ProjectRoot\Package.appxmanifest"
$content = [System.IO.File]::ReadAllText($manifestPath)
$current = [regex]::Match($content, '<Identity\b[^>]*\bVersion="([^"]+)"').Groups[1].Value
$parts = $current.Split('.')
$parts[2] = [string]([int]$parts[2] + 1)
$parts[3] = '0'
$newVer = $parts -join '.'
$content = [regex]::Replace($content, '(<Identity\b[^>]*\bVersion=")[^"]*(")', "`${1}$newVer`${2}")
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, $content, $utf8NoBom)
Write-Host "  $current → $newVer" -ForegroundColor Gray

# ── 2. Build ───────────────────────────────────────────────────────────────────
Write-Host "[2/5] Building release..." -ForegroundColor Cyan
dotnet publish "$ProjectRoot\WifiQrScanner.csproj" `
    -c Release -r win-x64 --no-self-contained `
    -p:PublishSingleFile=false `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# ── 2. Stage layout ───────────────────────────────────────────────────────────
Write-Host "[3/5] Staging package layout..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $StageDir -Force | Out-Null
Copy-Item "$PublishDir\*" $StageDir -Recurse -Force
Copy-Item "$ProjectRoot\Package.appxmanifest" "$StageDir\AppxManifest.xml" -Force
New-Item -ItemType Directory -Path "$StageDir\Assets" -Force | Out-Null
Copy-Item "$ProjectRoot\Assets\*.png" "$StageDir\Assets\" -Force

# ── 3. Pack ────────────────────────────────────────────────────────────────────
Write-Host "[4/5] Packing MSIX..." -ForegroundColor Cyan
$makeappx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter makeappx.exe |
    Where-Object { $_.FullName -match 'x64' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $makeappx) { throw "makeappx.exe not found. Install the Windows SDK." }

& $makeappx pack /d $StageDir /p $OutputMsix /nv /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed." }

# ── 4. Self-signed cert (Store re-signs during certification) ─────────────────
Write-Host "[5/6] Generating self-signed cert..." -ForegroundColor Cyan
$publisher = "CN=607692FF-94E1-4964-80CA-A77E2F2FBCFF"
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $publisher `
    -KeyUsage DigitalSignature `
    -FriendlyName "WifiQrScanner" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$pwd = ConvertTo-SecureString -String "ci" -Force -AsPlainText
$pfxPath = "$ProjectRoot\sign.pfx"
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pwd | Out-Null

# ── 5. Sign ────────────────────────────────────────────────────────────────────
Write-Host "[6/6] Signing..." -ForegroundColor Cyan
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe |
    Where-Object { $_.FullName -match 'x64' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw "signtool.exe not found. Install the Windows SDK." }

& $signtool sign /fd SHA256 /f $pfxPath /p "ci" $OutputMsix
if ($LASTEXITCODE -ne 0) { throw "Signing failed." }

# Cleanup temp files
Remove-Item $pfxPath -Force
Remove-Item $StageDir -Recurse -Force

Write-Host ""
Write-Host "  Done: $OutputMsix" -ForegroundColor Green
Write-Host "  Upload at: https://partner.microsoft.com/dashboard" -ForegroundColor Green
Write-Host ""
