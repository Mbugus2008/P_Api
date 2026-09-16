# migrate_cert_to_main.ps1
# Copies the nav.trimline.co.ke TLS certificate to main.trimline.co.ke and binds it,
# so that EXISTING apps (which use https://nav.trimline.co.ke:4013) keep working
# after DNS is pointed at main.
#
# You will be prompted for:
#   1. a temporary password that protects the .pfx while it is moved (type anything, it is discarded after)
#   2. the Windows Administrator password for main.trimline.co.ke
#
# Usage: pwsh -ExecutionPolicy Bypass -File .\migrate_cert_to_main.ps1
#        pwsh -ExecutionPolicy Bypass -File .\migrate_cert_to_main.ps1 -VerifyOnly

param(
    [string]$NavHost = "nav.trimline.co.ke",
    [string]$NavUser = "Administrator",
    [string]$MainHost = "main.trimline.co.ke",
    [string]$MainUser = "Administrator",
    [string]$CertHostname = "nav.trimline.co.ke",
    [int]$Port = 4013,
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"
function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }

if ($VerifyOnly) {
    Step "Verifying which certificate main presents per hostname"
    & pwsh -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check_sni_cert.ps1")
    return
}

# ------------------------------------------------------------------ inputs
$pfxPasswordPlain = Read-Host -Prompt "Temporary PFX password (any value, discarded afterwards)" -AsSecureString
$pfxPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($pfxPasswordPlain))
$mainCred = Get-Credential -UserName $MainUser -Message "Windows password for $MainUser@$MainHost"
if (-not $mainCred) { throw "No credentials supplied for $MainHost" }
$pfxLocal = Join-Path $env:TEMP "nav-$([Guid]::NewGuid().ToString('N')).pfx"

try {
    # -------------------------------------------------- export on nav (over SSH)
    Step "Exporting the $CertHostname certificate from $NavHost"
    $exportCmd = @"
`$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { `$_.Subject -like '*CN=$CertHostname*' } | Select-Object -First 1
if (-not `$cert) { Write-Output 'NOT_FOUND'; exit 1 }
`$pw = ConvertTo-SecureString -String '$pfxPassword' -AsPlainText -Force
Export-PfxCertificate -Cert `$cert -FilePath C:\Windows\Temp\nav-cert.pfx -Password `$pw | Out-Null
Write-Output ('EXPORTED thumbprint=' + `$cert.Thumbprint)
"@
    $exportEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($exportCmd))
    $exportResult = & ssh "$NavUser@$NavHost" "powershell -NoProfile -EncodedCommand $exportEncoded"
    $exportResult | ForEach-Object { "  $_" }
    if (($exportResult -join " ") -notmatch 'EXPORTED') { throw "Certificate export failed on $NavHost" }

    Step "Downloading the .pfx"
    & scp "$NavUser@${NavHost}:C:/Windows/Temp/nav-cert.pfx" $pfxLocal
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $pfxLocal)) { throw "Could not download the .pfx" }
    "  saved: $pfxLocal ($([math]::Round((Get-Item $pfxLocal).Length/1KB,1)) KB)"

    # -------------------------------------------------- upload + import on main
    Step "Uploading and importing the certificate on $MainHost"
    $trusted = (Get-Item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue).Value
    if ($trusted -notlike "*$MainHost*") {
        $new = if ([string]::IsNullOrWhiteSpace($trusted)) { $MainHost } else { "$trusted,$MainHost" }
        Set-Item WSMan:\localhost\Client\TrustedHosts -Value $new -Force
    }
    $session = New-PSSession -ComputerName $MainHost -Credential $mainCred -Authentication Negotiate

    try {
        # push the pfx via the admin share
        $drive = New-PSDrive -Name CertDrop -PSProvider FileSystem -Root "\\$MainHost\C$" -Credential $mainCred -Scope Script
        try {
            Copy-Item $pfxLocal "CertDrop:\Windows\Temp\nav-cert.pfx" -Force
            "  copied to \\$MainHost\C$\Windows\Temp\nav-cert.pfx"
        }
        finally { Remove-PSDrive -Name CertDrop -ErrorAction SilentlyContinue }

        Invoke-Command -Session $session -ScriptBlock {
            param($pfxPath, $pw, $hostname, $port)
            Import-Module WebAdministration -ErrorAction SilentlyContinue
            $secure = ConvertTo-SecureString -String $pw -AsPlainText -Force
            $imported = Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $secure
            $thumb = $imported.Thumbprint
            Write-Output "  imported thumbprint=$thumb subject=$($imported.Subject)"

            # bind the hostname with SNI, reusing the existing 4013 binding as a template
            $existing = Get-WebBinding -Port $port | Where-Object { $_.bindingInformation -notlike "*$hostname*" } | Select-Object -First 1
            $ipPart = "*"
            if ($existing) {
                $ipPart = ($existing.bindingInformation -split ':')[0]
            }
            $bind = Get-WebBinding -Name (Get-Website | Select-Object -First 1).Name -Port $port -HostHeader $hostname -ErrorAction SilentlyContinue
            if (-not $bind) {
                New-WebBinding -Name (Get-Website | Select-Object -First 1).Name -Protocol https -Port $port -HostHeader $hostname -SslFlags 1 | Out-Null
                Write-Output "  created https binding for $hostname`:$port (SNI)"
            }
            else {
                Write-Output "  binding for $hostname already present"
            }

            # point that binding at the imported certificate
            $site = (Get-Website | Select-Object -First 1).Name
            $bindInfo = "${ipPart}:$port`:$hostname"
            & netsh http delete sslcert hostnameport="$hostname`:$port" 2>$null | Out-Null
            $appid = "{4dc3e181-e14b-4a21-b022-59fc669b0914}"
            & netsh http add sslcert hostnameport="$hostname`:$port" certhash=$thumb appid=$appid certstorename=MY | Out-Null
            Write-Output "  sslcert bound for $hostname`:$port"
            Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue
        } -ArgumentList "C:\Windows\Temp\nav-cert.pfx", $pfxPassword, $CertHostname, $Port
    }
    finally { Remove-PSSession $session -ErrorAction SilentlyContinue }
}
finally {
    if (Test-Path $pfxLocal) { Remove-Item $pfxLocal -Force }
    # remove the pfx left on the nav box
    & ssh "$NavUser@$NavHost" "powershell -NoProfile -Command `"Remove-Item C:\Windows\Temp\nav-cert.pfx -Force -ErrorAction SilentlyContinue`"" 2>$null | Out-Null
}

Step "Verifying"
& pwsh -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check_sni_cert.ps1")
