# migrate_to_main.ps1
# Deploys the current ParcelAPI build + APK/version manifest + dashboard to the
# MAIN server (which also hosts NAV/BC), so nav.trimline.co.ke can be retired.
#
# It will prompt for the Windows Administrator password ONCE (typed locally).
# Nothing is changed on the nav server.
#
# Usage:  pwsh -ExecutionPolicy Bypass -File .\migrate_to_main.ps1
# Options:
#   -WhatIf          show what would be copied, change nothing
#   -SkipPublish     reuse the existing .\publish folder
#   -SkipAppPool     do not stop/start the app pool on main

param(
    [string]$RemoteHost = "main.trimline.co.ke",
    [string]$RemoteUser = "Administrator",
    [string]$RemoteSitePath = "D:\Parcel",
    [string]$RemoteAppFolder = "D:\Parcel\ParcelApp",
    [string]$AppPool = "Parcel",
    [string]$NavHostOverride = "localhost",
    [switch]$WhatIf,
    [switch]$SkipPublish,
    [switch]$SkipAppPool,
    [switch]$SkipAppFiles
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# ---------------------------------------------------------------- credentials
$cred = Get-Credential -UserName $RemoteUser -Message "Windows password for $RemoteUser@$RemoteHost"
if (-not $cred) { throw "No credentials supplied." }
$plain = $cred.GetNetworkCredential().Password

# ---------------------------------------------------------------- publish
if (-not $SkipPublish) {
    Step "Publishing API (Release)"
    & dotnet publish (Join-Path $root "ParcelAPI.csproj") -c Release -o (Join-Path $root "publish")
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

$publishPath = Join-Path $root "publish"
if (-not (Test-Path $publishPath)) { throw "publish folder not found: $publishPath" }

$apkPath = Join-Path $root "..\ParcelApp\build\app\outputs\flutter-apk\app-release.apk"
$apkPath = (Resolve-Path $apkPath).Path
$versionJsonPath = Join-Path $root "app_version.json"

Step "Artifacts"
Write-Host "  API publish : $publishPath ($((Get-ChildItem $publishPath -Recurse -File).Count) files)"
Write-Host "  APK         : $apkPath ($([math]::Round((Get-Item $apkPath).Length / 1MB, 1)) MB)"
Write-Host "  manifest    : $versionJsonPath"

# ---------------------------------------------------------------- remote session
Step "Opening WinRM session to $RemoteHost"
$trusted = (Get-Item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue).Value
if ($trusted -notlike "*$RemoteHost*") {
    $newTrusted = if ([string]::IsNullOrWhiteSpace($trusted)) { $RemoteHost } else { "$trusted,$RemoteHost" }
    Write-Host "  adding $RemoteHost to local TrustedHosts"
    Set-Item WSMan:\localhost\Client\TrustedHosts -Value $newTrusted -Force
}

$session = New-PSSession -ComputerName $RemoteHost -Credential $cred -Authentication Negotiate
if (-not $session) { throw "Could not open a remote session to $RemoteHost" }
Write-Host "  connected: $((Invoke-Command -Session $session { hostname }))"

try {
    if (-not $WhatIf) {
        # ------------------------------------------------------------ app pool down
        if (-not $SkipAppPool) {
            Step "Stopping app pool '$AppPool' on $RemoteHost"
            Invoke-Command -Session $session -ScriptBlock {
                param($pool)
                & "$env:SystemRoot\System32\inetsrv\appcmd.exe" stop apppool /apppool.name:$pool
            } -ArgumentList $AppPool
        }

        # ------------------------------------------------------------ copy API + wwwroot
        Step "Copying API + wwwroot to $RemoteSitePath"
        $credPath = New-PSDrive -Name MainDeploy -PSProvider FileSystem `
            -Root "\\$RemoteHost\D$" -Credential $cred -Scope Script
        try {
            $target = "MainDeploy:\Parcel"
            if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
            Copy-Item -Path (Join-Path $publishPath "*") -Destination $target -Recurse -Force
            Write-Host "  API files copied"
        }
        finally {
            Remove-PSDrive -Name MainDeploy -ErrorAction SilentlyContinue
        }

        # ------------------------------------------------------------ APK + manifest
        if ($SkipAppFiles) {
            Step "Skipping APK/manifest copy (API-only migration)"
        }
        else {
            Step "Copying APK + version manifest to $RemoteAppFolder"
            $credPath2 = New-PSDrive -Name MainDeploy2 -PSProvider FileSystem `
                -Root "\\$RemoteHost\D$" -Credential $cred -Scope Script
            try {
                $appFolder = "MainDeploy2:\Parcel\ParcelApp"
                if (-not (Test-Path $appFolder)) { New-Item -ItemType Directory -Path $appFolder -Force | Out-Null }
                Copy-Item $apkPath (Join-Path $appFolder "ParcelApp.apk") -Force
                Copy-Item $apkPath (Join-Path $appFolder ("parcel-v" + ((Get-Content $versionJsonPath | ConvertFrom-Json).version) + ".apk")) -Force
                Copy-Item $versionJsonPath (Join-Path $appFolder "app_version.json") -Force
                Write-Host "  APK + manifest copied"
            }
            finally {
                Remove-PSDrive -Name MainDeploy2 -ErrorAction SilentlyContinue
            }
        }

        # ------------------------------------------------------------ NAV host override
        if ([string]::IsNullOrWhiteSpace($NavHostOverride)) {
            Step "Skipping appsettings change (NavHostOverride empty — using the Clients row host)"
        }
        else {
            Step "Setting Nav:HostOverride=$NavHostOverride in $RemoteSitePath\appsettings.json"
            Invoke-Command -Session $session -ScriptBlock {
                param($sitePath, $override)
                $file = Join-Path $sitePath "appsettings.json"
                $json = Get-Content $file -Raw | ConvertFrom-Json
                if (-not $json.PSObject.Properties.Name.Contains("Nav")) {
                    $json | Add-Member -MemberType NoteProperty -Name Nav -Value ([pscustomobject]@{})
                }
                if ($json.Nav.PSObject.Properties.Name.Contains("HostOverride")) {
                    $json.Nav.HostOverride = $override
                } else {
                    $json.Nav | Add-Member -MemberType NoteProperty -Name HostOverride -Value $override
                }
                $json | ConvertTo-Json -Depth 10 | Set-Content $file -Encoding UTF8
                "  appsettings.json updated"
            } -ArgumentList $RemoteSitePath, $NavHostOverride
        }

        # ------------------------------------------------------------ app pool up
        if (-not $SkipAppPool) {
            Step "Starting app pool '$AppPool'"
            Invoke-Command -Session $session -ScriptBlock {
                param($pool)
                & "$env:SystemRoot\System32\inetsrv\appcmd.exe" start apppool /apppool.name:$pool
            } -ArgumentList $AppPool
        }
    }
    else {
        Write-Host "`n(WhatIf — nothing was changed)"
    }

    # ------------------------------------------------------------ verify
    Step "Verifying $RemoteHost"
    Start-Sleep -Seconds 5
    $base = "https://$RemoteHost:4013"
    $hdr = @{ 'X-Client-Identifier' = 'REMBOCLASIC' }

    foreach ($probe in @(
            @{ name = 'health';   url = "$base/api/health";   method = 'GET';  headers = @{} },
            @{ name = 'users';    url = "$base/api/Parcel/nav/users"; method = 'POST'; headers = $hdr; body = '{"PageSize":5}' },
            @{ name = 'summary';  url = "$base/api/dashboard/summary"; method = 'GET'; headers = $hdr },
            @{ name = 'parcels';  url = "$base/api/Parcel/Parcels"; method = 'POST'; headers = $hdr; body = '{"PageSize":5}' }
        )) {
        try {
            $args = @{ Uri = $probe.url; Method = $probe.method; SkipCertificateCheck = $true; TimeoutSec = 120 }
            if ($probe.headers.Count) { $args.Headers = $probe.headers }
            if ($probe.body) { $args.Body = $probe.body; $args.ContentType = 'application/json' }
            $r = Invoke-WebRequest @args
            $snippet = if ($probe.method -eq 'HEAD') { "len=$($r.Headers['Content-Length'])" } else { $r.Content.Substring(0, [Math]::Min(110, $r.Content.Length)) }
            Write-Host ("  {0,-8} HTTP {1}  {2}" -f $probe.name, $r.StatusCode, $snippet)
        }
        catch {
            Write-Host ("  {0,-8} FAILED: {1}" -f $probe.name, $_.Exception.Message) -ForegroundColor Red
        }
    }
}
finally {
    Remove-PSSession $session -ErrorAction SilentlyContinue
}

Step "Done"
Write-Host @"
Next steps (only when you are ready to cut over):
  1. All probes above must be green (especially 'users' and 'summary').
  2. Point DNS nav.trimline.co.ke -> 51.89.234.110 (main).
     Devices keep working unchanged: the app and the update manifest both use nav.trimline.co.ke.
  3. Retest https://nav.trimline.co.ke:4013/api/health and /dashboard.html
  4. Only then power off the old nav server.
"@ -ForegroundColor Yellow
