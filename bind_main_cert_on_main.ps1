# bind_main_cert_on_main.ps1
# Fixes: https://main.trimline.co.ke:4013 was presenting the nav.trimline.co.ke
# certificate, so the mobile app (hard-coded to main.trimline.co.ke) failed TLS
# hostname validation on every request -> "Unable to login right now",
# no parcels/batches pulled, no update check.
#
# What it does (on main.trimline.co.ke only):
#   1. finds the existing CN=main.trimline.co.ke certificate in the machine store
#      (the same one already serving :443, expires 2026-12-11)
#   2. adds an IIS HTTPS binding for host main.trimline.co.ke on port 4013 (SNI)
#   3. binds that hostname:port in http.sys to the main certificate
#   4. verifies what nav. and main. now present on 4013
#
# The existing nav.trimline.co.ke binding is left untouched, so devices that
# have not updated yet keep working.
#
# Usage: pwsh -ExecutionPolicy Bypass -File .\bind_main_cert_on_main.ps1
#        pwsh -ExecutionPolicy Bypass -File .\bind_main_cert_on_main.ps1 -VerifyOnly

param(
    [string]$MainHost = "main.trimline.co.ke",
    [string]$MainUser = "Administrator",
    [int]$Port = 4013,
    [string]$CertHostname = "main.trimline.co.ke",
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"
function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

if ($VerifyOnly) {
    & pwsh -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check_sni_cert.ps1")
    return
}

# ---------------------------------------------------------------- credentials
$mainCred = Get-Credential -UserName $MainUser -Message "Windows password for $MainUser@$MainHost"
if (-not $mainCred) { throw "No credentials supplied for $MainHost" }

$trusted = (Get-Item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue).Value
if ($trusted -notlike "*$MainHost*") {
    $new = if ([string]::IsNullOrWhiteSpace($trusted)) { $MainHost } else { "$trusted,$MainHost" }
    Set-Item WSMan:\localhost\Client\TrustedHosts -Value $new -Force
    "  added $MainHost to TrustedHosts"
}

Step "Binding the $CertHostname certificate on $MainHost`:$Port"
$session = New-PSSession -ComputerName $MainHost -Credential $mainCred -Authentication Negotiate
try {
    Invoke-Command -Session $session -ScriptBlock {
        param($CertHostname, $Port)

        Import-Module WebAdministration -ErrorAction SilentlyContinue

        # 1. locate the certificate for this hostname (prefer the latest)
        $cert = Get-ChildItem Cert:\LocalMachine\My |
            Where-Object {
                $_.Subject -like "*CN=$CertHostname*" -or
                ($_.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' } |
                    ForEach-Object { $_.Format($false) }) -match [regex]::Escape($CertHostname)
            } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if (-not $cert) { throw "No certificate for $CertHostname found in LocalMachine\My" }
        Write-Output "  using cert: $($cert.Subject)  exp $($cert.NotAfter.ToString('yyyy-MM-dd'))  thumbprint $($cert.Thumbprint)"

        # 2. find the site that currently serves this port
        $siteName = $null
        foreach ($site in Get-Website) {
            foreach ($b in $site.Bindings.Collection) {
                if ($b.bindingInformation -match ":$Port(:|$)") { $siteName = $site.Name; break }
            }
            if ($siteName) { break }
        }
        if (-not $siteName) { throw "No IIS site found with a binding on port $Port" }
        Write-Output "  target site: $siteName"

        # 3. add the SNI host binding (reuse the IP part of an existing 4013 binding)
        $existing = Get-WebBinding -Name $siteName | Where-Object { $_.bindingInformation -match ":$Port" } | Select-Object -First 1
        $ipPart = "*"
        if ($existing) { $ipPart = ($existing.bindingInformation -split ':')[0] }
        if (-not $ipPart) { $ipPart = "*" }

        $bind = Get-WebBinding -Name $siteName -Port $Port -HostHeader $CertHostname -ErrorAction SilentlyContinue
        if ($bind) {
            Write-Output "  binding for $CertHostname`:$Port already present"
        }
        else {
            New-WebBinding -Name $siteName -Protocol https -Port $Port -HostHeader $CertHostname -SslFlags 1 | Out-Null
            Write-Output "  created https binding for $CertHostname`:$Port (SNI)"
        }

        # 4. bind hostname:port to the certificate in http.sys
        $appid = "{4dc3e181-e14b-4a21-b022-59fc669b0914}"   # IIS
        & netsh http delete sslcert hostnameport="$CertHostname`:$Port" 2>$null | Out-Null
        & netsh http add sslcert hostnameport="$CertHostname`:$Port" certhash=$($cert.Thumbprint) appid=$appid certstorename=MY | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "netsh http add sslcert failed (exit $LASTEXITCODE)" }
        Write-Output "  http.sys sslcert bound for $CertHostname`:$Port"

        # 5. what does http.sys now have for this port?
        Write-Output "  -- netsh http show sslcert on port $Port --"
        (netsh http show sslcert) -split "`r?`n" | Select-String -Pattern ":$Port\b|Hash|Hostname" | ForEach-Object { "    $_" }
    } -ArgumentList $CertHostname, $Port
}
finally { Remove-PSSession $session -ErrorAction SilentlyContinue }

Step "Verifying (this reads the certificate each hostname now presents)"
& pwsh -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check_sni_cert.ps1")
