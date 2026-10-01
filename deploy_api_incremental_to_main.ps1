# deploy_api_incremental_to_main.ps1
# Incremental API deployment to the main server. Copies only changed/new files
# from the local publish output to the live site (C:\Services\Parcel), instead
# of re-uploading everything.
#
# Order of operations:
#   1. dotnet publish (unless -SkipPublish)
#   2. open WinRM session, stop the IIS app pool
#   3. compare local publish output with the live folder (size + timestamp)
#      and copy only the differing files through the C$ admin share
#   4. start the app pool
#   5. probe the health endpoint
#
# You will be prompted for the Windows Administrator password for the main server.
#
# Usage: pwsh -ExecutionPolicy Bypass -File .\deploy_api_incremental_to_main.ps1
#        pwsh -ExecutionPolicy Bypass -File .\deploy_api_incremental_to_main.ps1 -SkipPublish
#        pwsh -ExecutionPolicy Bypass -File .\deploy_api_incremental_to_main.ps1 -WhatIf

param(
    [string]$RemoteHost = "main.trimline.co.ke",
    [string]$RemoteUser = "Administrator",
    [string]$RemoteSitePath = "C:\Services\Parcel",
    [string]$AppPool = "Parcel",
    [string]$ProjectPath = "D:\Projects2\Parcel\ParcelAPI",
    [string]$PublishPath = "D:\Projects2\Parcel\ParcelAPI\publish",
    [string]$HealthUrl = "https://nav.trimline.co.ke:4013/api/Health",
    [switch]$SkipPublish,
    [switch]$SkipAppPool,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

# ------------------------------------------------------------------ 1. publish
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

# ------------------------------------------------------------------ 2. plan
Step "Comparing local publish output with $RemoteSitePath"
$localFiles = Get-ChildItem $PublishPath -File -Recurse |
    Where-Object { $_.FullName -notmatch '\\Logs\\' } |
    ForEach-Object {
        [pscustomobject]@{
            Rel  = $_.FullName.Substring($PublishPath.Length).TrimStart('\')
            Size = $_.Length
            Time = $_.LastWriteTimeUtc.ToString('o')
        }
    }

$cred = Get-Credential -UserName $RemoteUser -Message "Windows password for $RemoteUser@$RemoteHost"
if (-not $cred) { throw "No credentials supplied" }

$trusted = (Get-Item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue).Value
if ($trusted -notlike "*$RemoteHost*") {
    $new = if ([string]::IsNullOrWhiteSpace($trusted)) { $RemoteHost } else { "$trusted,$RemoteHost" }
    Set-Item WSMan:\localhost\Client\TrustedHosts -Value $new -Force
}

$session = New-PSSession -ComputerName $RemoteHost -Credential $cred -Authentication Negotiate
try {
    $remoteMap = Invoke-Command -Session $session -ScriptBlock {
        param($root)
        if (-not (Test-Path $root)) { return @() }
        Get-ChildItem $root -File -Recurse |
            Where-Object { $_.FullName -notmatch '\\Logs\\' -and $_.Name -ne 'app_offline.htm' } |
            ForEach-Object {
                [pscustomobject]@{
                    Rel  = $_.FullName.Substring($root.Length).TrimStart('\')
                    Size = $_.Length
                    Time = $_.LastWriteTimeUtc.ToString('o')
                }
            }
    } -ArgumentList $RemoteSitePath

    $remoteByRel = @{}
    foreach ($f in $remoteMap) { $remoteByRel[$f.Rel] = $f }

    $toCopy = @()
    foreach ($f in $localFiles) {
        $r = $remoteByRel[$f.Rel]
        if (-not $r) {
            $toCopy += $f
        }
        elseif ($r.Size -ne $f.Size) {
            $toCopy += $f
        }
        elseif ([datetime]$r.Time -lt [datetime]$f.Time) {
            $toCopy += $f
        }
    }

    Write-Host ("  local files : {0}" -f $localFiles.Count)
    Write-Host ("  remote files: {0}" -f $remoteMap.Count)
    Write-Host ("  to copy     : {0}" -f $toCopy.Count)
    $toCopy | Select-Object -First 30 | ForEach-Object { Write-Host "    $($_.Rel) ($($_.Size) bytes)" }
    if ($toCopy.Count -gt 30) { Write-Host "    ... and $($toCopy.Count - 30) more" }

    if ($toCopy.Count -eq 0) { Write-Host "  nothing to do" -ForegroundColor Green; return }
    if ($WhatIf) { Write-Host "`nWhatIf: skipping copy/app pool changes" -ForegroundColor Yellow; return }

    # -------------------------------------------------------------- 3. stop pool
    if (-not $SkipAppPool) {
        Step "Stopping IIS app pool '$AppPool'"
        Invoke-Command -Session $session -ScriptBlock {
            param($pool)
            & "$env:SystemRoot\System32\inetsrv\appcmd.exe" stop apppool /apppool.name:$pool | Out-Null
        } -ArgumentList $AppPool
    }

    # -------------------------------------------------------------- 4. copy
    Step "Copying $($toCopy.Count) file(s) to $RemoteSitePath"
    $drive = New-PSDrive -Name ApiDrop -PSProvider FileSystem -Root "\\$RemoteHost\C$" -Credential $cred -Scope Script
    try {
        $root = "ApiDrop:" + ($RemoteSitePath -replace '^C:', '')
        foreach ($f in $toCopy) {
            $dest = Join-Path $root $f.Rel
            $destDir = Split-Path $dest -Parent
            if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
            Copy-Item (Join-Path $PublishPath $f.Rel) $dest -Force
        }
        Write-Host "  copied $($toCopy.Count) file(s)"
    }
    finally { Remove-PSDrive -Name ApiDrop -ErrorAction SilentlyContinue }

    # -------------------------------------------------------------- 5. start pool
    if (-not $SkipAppPool) {
        Step "Starting IIS app pool '$AppPool'"
        Invoke-Command -Session $session -ScriptBlock {
            param($pool)
            & "$env:SystemRoot\System32\inetsrv\appcmd.exe" start apppool /apppool.name:$pool | Out-Null
        } -ArgumentList $AppPool
    }
}
finally { Remove-PSSession $session -ErrorAction SilentlyContinue }

# ------------------------------------------------------------------ 6. probe
Step "Probing $HealthUrl"
Start-Sleep -Seconds 6
foreach ($try in 1..6) {
    try {
        $r = Invoke-WebRequest -Uri $HealthUrl -TimeoutSec 20 -SkipCertificateCheck
        Write-Host "  HTTP $($r.StatusCode) - API is up" -ForegroundColor Green
        break
    }
    catch {
        Write-Host "  attempt $try failed: $($_.Exception.Message)"
        Start-Sleep -Seconds 5
    }
}
