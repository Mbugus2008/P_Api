# deploy.ps1 — Deploy Parcel API + Flutter APKs to production server
# Usage: .\deploy.ps1 [-ApiOnly] [-ApkOnly] [-SkipBuild]

param(
    [switch]$ApiOnly,
    [switch]$ApkOnly,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$Server = "Administrator@nav.trimline.co.ke"
$AppPool = "Parcel"
$ApiProject = "D:\Projects2\Parcel\ParcelAPI"
$FlutterProject = "D:\Projects2\Parcel\ParcelApp"
$RemotePath = "D:/Parcel"
$RemoteUpdates = "$RemotePath/wwwroot/updates"

function Write-Step { param($msg) Write-Host "`n>>> $msg" -ForegroundColor Cyan }

# ============ API ============
if (-not $ApkOnly) {
    if (-not $SkipBuild) {
        Write-Step "Building API..."
        Push-Location $ApiProject
        dotnet publish -c Release -o publish
        Pop-Location
    }

    Write-Step "Stopping app pool..."
    ssh $Server "C:\Windows\System32\inetsrv\appcmd stop apppool /apppool.name:$AppPool"

    Write-Step "Deploying API DLL..."
    scp "$ApiProject\publish\ParcelAPI.dll" "${Server}:$RemotePath/ParcelAPI.dll"

    Write-Step "Starting app pool..."
    ssh $Server "C:\Windows\System32\inetsrv\appcmd start apppool /apppool.name:$AppPool"

    Write-Host "API deployed." -ForegroundColor Green
}

# ============ FLUTTER APKs ============
if (-not $ApiOnly) {
    if (-not $SkipBuild) {
        Write-Step "Building Flutter APKs..."
        Push-Location $FlutterProject
        flutter build apk --release
        flutter build apk --split-per-abi
        Pop-Location
    }

    $ApkDir = "$FlutterProject\build\app\outputs\flutter-apk"

    Write-Step "Deploying universal APK..."
    scp "$ApkDir\app-release.apk" "${Server}:$RemotePath/wwwroot/app-release.apk"

    Write-Step "Deploying ABI-split APKs..."
    scp "$ApkDir\app-arm64-v8a-release.apk" "${Server}:$RemoteUpdates/app-arm64-v8a-release.apk"
    scp "$ApkDir\app-armeabi-v7a-release.apk" "${Server}:$RemoteUpdates/app-armeabi-v7a-release.apk"
    scp "$ApkDir\app-x86_64-release.apk" "${Server}:$RemoteUpdates/app-x86_64-release.apk"

    Write-Host "APKs deployed." -ForegroundColor Green
}

Write-Host "`nDeploy complete." -ForegroundColor Green
