# deploy_to_main.ps1
# One-shot deployment to the main server (single password prompt):
#   1. dotnet publish the API (unless -SkipPublish)
#   2. incremental API copy: only changed/new files go to C:\Services\Parcel,
#      with the IIS app pool stopped during the copy
#   3. APK + update manifest copy to C:\Services\Parcel\ParcelApp
#   4. verification: health endpoint, update manifest, APK HEAD, remote hash
#
# Usage: pwsh -ExecutionPolicy Bypass -File .\deploy_to_main.ps1
#        pwsh -ExecutionPolicy Bypass -File .\deploy_to_main.ps1 -SkipPublish
#        pwsh -ExecutionPolicy Bypass -File .\deploy_to_main.ps1 -SkipApi
#        pwsh -ExecutionPolicy Bypass -File .\deploy_to_main.ps1 -SkipApk

param(
    [string]$RemoteHost = "main.trimline.co.ke",
    [string]$RemoteUser = "Administrator",
    [string]$RemoteSitePath = "C:\Services\Parcel",
    [string]$RemoteAppFolder = "C:\Services\Parcel\ParcelApp",
    [string]$AppPool = "Parcel",
    [string]$ProjectPath = "D:\Projects2\Parcel\ParcelAPI",
    [string]$PublishPath = "D:\Projects2\Parcel\ParcelAPI\publish",
    [string]$ApkPath = "D:\Projects2\Parcel\ParcelApp\build\app\outputs\flutter-apk\app-release.apk",
    [string]$ManifestPath = "D:\Projects2\Parcel\ParcelAPI\app_version.json",
    [string]$PublicOrigin = "https://nav.trimline.co.ke:4013",
    [string]$BackupRoot = "C:\Parcel_backups",
    [string]$Password = "",
    [switch]$SkipBackup,
    [switch]$SkipPublish,
    [switch]$SkipApi,
    [switch]$SkipApk
)

$ErrorActionPreference = "Stop"
function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

# ------------------------------------------------------------------ 1. publish
if (-not $SkipApi) {
    if (-not $SkipPublish) {
        Step "Publishing API to $PublishPath"
        Push-Location $ProjectPath
        try {
            dotnet publish ParcelAPI.csproj -c Release -o $PublishPath --nologo -v q | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
        }
        finally { Pop-Location }
    }
    if (-not (Test-Path $PublishPath)) { throw "Publish folder not found: $PublishPath" }
}

# ------------------------------------------------------------------ 2. APK checks
if (-not $SkipApk) {
    Step "Checking the APK against the manifest"
    if (-not (Test-Path $ApkPath)) { throw "APK not found: $ApkPath" }
    if (-not (Test-Path $ManifestPath)) { throw "Manifest not found: $ManifestPath" }
    $manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
    $apkInfo = Get-Item $ApkPath
    $apkHash = (Get-FileHash $ApkPath -Algorithm SHA256).Hash.ToLower()
    Write-Host "  version   : $($manifest.version)+$($manifest.versionCode)"
    Write-Host "  apk size  : $($apkInfo.Length)  (manifest $($manifest.apkSize))"
    Write-Host "  apk sha256: $apkHash"
    if ($apkInfo.Length -ne $manifest.apkSize) { throw "apkSize in app_version.json does not match the APK" }
    if ($apkHash -ne $manifest.apkSha256.ToLower()) { throw "apkSha256 in app_version.json does not match the APK" }
}

# ------------------------------------------------------------------ 3. session
if ([string]::IsNullOrWhiteSpace($Password)) {
    $cred = Get-Credential -UserName $RemoteUser -Message "Windows password for $RemoteUser@$RemoteHost"
}
else {
    $secure = ConvertTo-SecureString -String $Password -AsPlainText -Force
    $cred = New-Object System.Management.Automation.PSCredential($RemoteUser, $secure)
}
if (-not $cred) { throw "No credentials supplied" }

$trusted = (Get-Item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue).Value
if ($trusted -notlike "*$RemoteHost*") {
    $new = if ([string]::IsNullOrWhiteSpace($trusted)) { $RemoteHost } else { "$trusted,$RemoteHost" }
    Set-Item WSMan:\localhost\Client\TrustedHosts -Value $new -Force
}

$session = New-PSSession -ComputerName $RemoteHost -Credential $cred -Authentication Negotiate
if (-not $session) { throw "Could not open a remote session to $RemoteHost" }
Write-Host "  connected: $((Invoke-Command -Session $session { hostname }))"

$drive = New-PSDrive -Name Deploy -PSProvider FileSystem -Root "\\$RemoteHost\C$" -Credential $cred -Scope Script
try {
    # -------------------------------------------------------------- 4. API files
    if (-not $SkipApi) {
        Step "Comparing API files with $RemoteSitePath"
        # ParcelApp\* is excluded: it is managed exclusively by the APK step
        # below (a stale APK sitting in an old publish folder must never
        # overwrite the published one).
        $localFiles = Get-ChildItem $PublishPath -File -Recurse |
            Where-Object { $_.FullName -notmatch '\\Logs\\' -and $_.FullName -notmatch '\\ParcelApp\\' } |
            ForEach-Object {
                [pscustomobject]@{
                    Rel  = $_.FullName.Substring($PublishPath.Length).TrimStart('\')
                    Size = $_.Length
                    Time = $_.LastWriteTimeUtc
                }
            }

        $remoteMap = Invoke-Command -Session $session -ScriptBlock {
            param($root)
            if (-not (Test-Path $root)) { return @() }
            Get-ChildItem $root -File -Recurse |
                Where-Object { $_.FullName -notmatch '\\Logs\\' -and $_.Name -ne 'app_offline.htm' } |
                ForEach-Object {
                    [pscustomobject]@{
                        Rel  = $_.FullName.Substring($root.Length).TrimStart('\')
                        Size = $_.Length
                        Time = $_.LastWriteTimeUtc
                    }
                }
        } -ArgumentList $RemoteSitePath

        $remoteByRel = @{}
        foreach ($f in $remoteMap) { $remoteByRel[$f.Rel] = $f }

        $toCopy = @()
        foreach ($f in $localFiles) {
            $r = $remoteByRel[$f.Rel]
            if (-not $r -or $r.Size -ne $f.Size -or $r.Time -lt $f.Time) { $toCopy += $f }
        }

        Write-Host ("  local: {0}  remote: {1}  to copy: {2}" -f $localFiles.Count, $remoteMap.Count, $toCopy.Count)
        $toCopy | Select-Object -First 25 | ForEach-Object { Write-Host "    + $($_.Rel)" }
        if ($toCopy.Count -gt 25) { Write-Host "    ... and $($toCopy.Count - 25) more" }

    }
    else {
        $toCopy = @()
    }

    $needBackup = -not $SkipBackup
    $needApiCopy = $toCopy.Count -gt 0

    if ($needBackup -or $needApiCopy) {
        # The live app locks its DLLs, so the app pool is stopped for both the
        # backup and the copy; the finally block always brings it back up.
        Step "Stopping app pool '$AppPool'"
        Invoke-Command -Session $session -ScriptBlock {
            param($pool)
            & "$env:SystemRoot\System32\inetsrv\appcmd.exe" stop apppool /apppool.name:$pool | Out-Null
        } -ArgumentList $AppPool

        try {
            if ($needBackup) {
                Step "Backing up the live API to $BackupRoot"
                Invoke-Command -Session $session -ScriptBlock {
                    param($sitePath, $backupRoot)
                    if (-not (Test-Path $backupRoot)) { New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null }

                    # drop any zero-byte leftovers from failed backup attempts
                    Get-ChildItem $backupRoot -Filter 'Parcel_api_backup_*.zip' -ErrorAction SilentlyContinue |
                        Where-Object { $_.Length -eq 0 } |
                        Remove-Item -Force -ErrorAction SilentlyContinue

                    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
                    $zip = Join-Path $backupRoot "Parcel_api_backup_$stamp.zip"

                    # Everything from the site root except Logs; plus ParcelApp metadata
                    # (app_version.json), but never the multi-megabyte APK files.
                    $paths = @()
                    $paths += (Get-ChildItem $sitePath | Where-Object { $_.Name -notin @('Logs', 'ParcelApp') } | ForEach-Object { $_.FullName })
                    $appFolder = Join-Path $sitePath 'ParcelApp'
                    if (Test-Path $appFolder) {
                        $paths += (Get-ChildItem $appFolder -File | Where-Object { $_.Extension -ne '.apk' } | ForEach-Object { $_.FullName })
                    }
                    if ($paths.Count -eq 0) { Write-Output '  nothing to back up'; return }

                    Compress-Archive -Path $paths -DestinationPath $zip -CompressionLevel Optimal -Force
                    $size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
                    Write-Output "  backup created: $zip ($size MB)"

                    # keep only the 10 most recent backups
                    Get-ChildItem $backupRoot -Filter 'Parcel_api_backup_*.zip' |
                        Sort-Object LastWriteTime -Descending |
                        Select-Object -Skip 10 |
                        Remove-Item -Force -ErrorAction SilentlyContinue
                } -ArgumentList $RemoteSitePath, $BackupRoot
            }

            if ($needApiCopy) {
                Step "Copying API files"
                $root = "Deploy:" + ($RemoteSitePath -replace '^C:', '')
                foreach ($f in $toCopy) {
                    $dest = Join-Path $root $f.Rel
                    $destDir = Split-Path $dest -Parent
                    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
                    Copy-Item (Join-Path $PublishPath $f.Rel) $dest -Force
                }
                Write-Host "  copied $($toCopy.Count) file(s)"
            }
        }
        finally {
            Step "Starting app pool '$AppPool'"
            Invoke-Command -Session $session -ScriptBlock {
                param($pool)
                & "$env:SystemRoot\System32\inetsrv\appcmd.exe" start apppool /apppool.name:$pool | Out-Null
            } -ArgumentList $AppPool
        }
    }
    else {
        Write-Host "  API is already up to date" -ForegroundColor Green
    }

    # -------------------------------------------------------------- 5. APK files
    if (-not $SkipApk) {
        Step "Copying APK + manifest to $RemoteAppFolder"
        $appRoot = "Deploy:" + ($RemoteAppFolder -replace '^C:', '')
        if (-not (Test-Path $appRoot)) { New-Item -ItemType Directory -Path $appRoot -Force | Out-Null }
        Copy-Item $ApkPath (Join-Path $appRoot "ParcelApp.apk") -Force
        Copy-Item $ApkPath (Join-Path $appRoot ("parcel-v" + $manifest.version + ".apk")) -Force
        Copy-Item $ManifestPath (Join-Path $appRoot "app_version.json") -Force
        Write-Host "  copied: ParcelApp.apk, parcel-v$($manifest.version).apk, app_version.json"

        $remoteApkHash = (Invoke-Command -Session $session -ScriptBlock {
            param($folder)
            $apk = Join-Path $folder 'ParcelApp.apk'
            if (Test-Path $apk) { (Get-FileHash $apk -Algorithm SHA256).Hash.ToLower() } else { 'MISSING' }
        } -ArgumentList $RemoteAppFolder)
        Write-Host "  remote sha256: $remoteApkHash"
        if ($remoteApkHash -ne $apkHash) { throw "Remote APK hash does not match the local APK" }

        # tidy old versioned apks (keep the two newest)
        Invoke-Command -Session $session -ScriptBlock {
            param($folder, $keep)
            Get-ChildItem $folder -Filter 'parcel-v*.apk' |
                Sort-Object LastWriteTime -Descending |
                Select-Object -Skip $keep |
                Remove-Item -Force -ErrorAction SilentlyContinue
        } -ArgumentList $RemoteAppFolder, 2
    }
}
finally {
    Remove-PSDrive -Name Deploy -ErrorAction SilentlyContinue
    Remove-PSSession $session -ErrorAction SilentlyContinue
}

# ------------------------------------------------------------------ 6. verify
Step "Verifying live endpoints"
Start-Sleep -Seconds 6

foreach ($try in 1..8) {
    try {
        $h = Invoke-WebRequest -Uri "$PublicOrigin/api/Health" -TimeoutSec 20
        Write-Host "  health: HTTP $($h.StatusCode)" -ForegroundColor Green
        break
    }
    catch {
        Write-Host "  health attempt ${try}: $($_.Exception.Message)"
        Start-Sleep -Seconds 5
    }
}

try {
    $resp = Invoke-RestMethod -Uri "$PublicOrigin/api/AppUpdate/android" -TimeoutSec 60
    $m = if ($resp.contents) { $resp.contents } else { $resp }
    Write-Host "  manifest: version=$($m.version) code=$($m.versionCode) size=$($m.apkSize)"
    Write-Host "  sha256  : $($m.apkSha256)"
    Write-Host "  download: $($m.downloadUrl)"
}
catch { Write-Host "  manifest ERROR: $($_.Exception.Message)" -ForegroundColor Red }

try {
    $apkHead = Invoke-WebRequest -Uri "$PublicOrigin/ParcelApp/ParcelApp.apk" -Method Head -TimeoutSec 60
    Write-Host "  apk HEAD: $($apkHead.StatusCode)  content-length=$(($apkHead.Headers.'Content-Length') -join '')" -ForegroundColor Green
}
catch { Write-Host "  apk HEAD ERROR: $($_.Exception.Message)" -ForegroundColor Red }

Write-Host "`nDeployment finished." -ForegroundColor Cyan
