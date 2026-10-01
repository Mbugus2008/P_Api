# publish_app_to_main.ps1
# Publishes the Android APK + update manifest to the Parcel API site on the
# main server, so in-app updates work again (the old nav server is gone and
# C:\Services\Parcel\ParcelApp was missing the .apk).
#
# Copies:
#   ParcelApp\build\app\outputs\flutter-apk\app-release.apk
#       -> C:\Services\Parcel\ParcelApp\ParcelApp.apk
#       -> C:\Services\Parcel\ParcelApp\parcel-v1.0.43.apk   (versioned copy)
#   ParcelAPI\app_version.json
#       -> C:\Services\Parcel\ParcelApp\app_version.json
#
# Program.cs serves the physical folder <ContentRoot>\ParcelApp at URL /ParcelApp,
# which is why the files live there (not in wwwroot).
#
# You will be prompted for the Windows Administrator password for the main server.
#
# Usage: pwsh -ExecutionPolicy Bypass -File .\publish_app_to_main.ps1
#        pwsh -ExecutionPolicy Bypass -File .\publish_app_to_main.ps1 -VerifyOnly

param(
    [string]$MainHost = "main.trimline.co.ke",
    [string]$MainUser = "Administrator",
    [string]$RemoteAppFolder = "C:\Services\Parcel\ParcelApp",
    [string]$ApkPath = "D:\Projects2\Parcel\ParcelApp\build\app\outputs\flutter-apk\app-release.apk",
    [string]$ManifestPath = "D:\Projects2\Parcel\ParcelAPI\app_version.json",
    [int]$Port = 4013,
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"
function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

function Verify {
    Step "Verifying the live update endpoint"
    try {
        $m = Invoke-RestMethod -Uri "https://nav.trimline.co.ke:$Port/api/AppUpdate/android" -TimeoutSec 60
        Write-Host "  manifest: version=$($m.version) code=$($m.versionCode) size=$($m.apkSize)"
        Write-Host "  sha256  : $($m.apkSha256)"
        Write-Host "  download: $($m.downloadUrl)"
    }
    catch { Write-Host "  manifest ERROR: $($_.Exception.Message)" -ForegroundColor Red }

    try {
        $h = Invoke-WebRequest -Uri "https://nav.trimline.co.ke:$Port/ParcelApp/ParcelApp.apk" -Method Head -TimeoutSec 60
        $len = $h.Headers.'Content-Length' -join ''
        Write-Host "  apk HEAD: $($h.StatusCode)  content-length=$len" -ForegroundColor Green
    }
    catch { Write-Host "  apk HEAD ERROR: $($_.Exception.Message)" -ForegroundColor Red }
}

if ($VerifyOnly) { Verify; return }

# ------------------------------------------------------------------- local checks
if (-not (Test-Path $ApkPath)) { throw "APK not found: $ApkPath" }
if (-not (Test-Path $ManifestPath)) { throw "Manifest not found: $ManifestPath" }

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$apkInfo = Get-Item $ApkPath
$apkHash = (Get-FileHash $ApkPath -Algorithm SHA256).Hash.ToLower()

Write-Host "  apk      : $($apkInfo.FullName)"
Write-Host "  size     : $($apkInfo.Length)  (manifest says $($manifest.apkSize))"
Write-Host "  sha256   : $apkHash  (manifest says $($manifest.apkSha256))"

if ($apkInfo.Length -ne $manifest.apkSize) { throw "apkSize in app_version.json does not match the APK" }
if ($apkHash -ne $manifest.apkSha256.ToLower()) { throw "apkSha256 in app_version.json does not match the APK" }

# ------------------------------------------------------------------- credentials
$mainCred = Get-Credential -UserName $MainUser -Message "Windows password for $MainUser@$MainHost"
if (-not $mainCred) { throw "No credentials supplied for $MainHost" }

$trusted = (Get-Item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue).Value
if ($trusted -notlike "*$MainHost*") {
    $new = if ([string]::IsNullOrWhiteSpace($trusted)) { $MainHost } else { "$trusted,$MainHost" }
    Set-Item WSMan:\localhost\Client\TrustedHosts -Value $new -Force
}

Step "Copying APK + manifest to $MainHost`:$RemoteAppFolder"
$drive = New-PSDrive -Name AppDrop -PSProvider FileSystem -Root "\\$MainHost\C$" -Credential $mainCred -Scope Script
try {
    $target = "AppDrop:" + ($RemoteAppFolder -replace '^C:', '')
    if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
    Copy-Item $ApkPath (Join-Path $target "ParcelApp.apk") -Force
    Copy-Item $ApkPath (Join-Path $target ("parcel-v" + $manifest.version + ".apk")) -Force
    Copy-Item $ManifestPath (Join-Path $target "app_version.json") -Force
    Write-Host "  copied: ParcelApp.apk, parcel-v$($manifest.version).apk, app_version.json"
}
finally { Remove-PSDrive -Name AppDrop -ErrorAction SilentlyContinue }

Step "Confirming the files on the server"
$session = New-PSSession -ComputerName $MainHost -Credential $mainCred -Authentication Negotiate
try {
    Invoke-Command -Session $session -ScriptBlock {
        param($folder)
        Get-ChildItem $folder -File |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 6 Name, Length, LastWriteTime |
            Format-Table -AutoSize | Out-String
        $apk = Join-Path $folder 'ParcelApp.apk'
        if (Test-Path $apk) { Write-Output ("remote sha256: " + (Get-FileHash $apk -Algorithm SHA256).Hash.ToLower()) }
    } -ArgumentList $RemoteAppFolder
}
finally { Remove-PSSession $session -ErrorAction SilentlyContinue }

Verify
