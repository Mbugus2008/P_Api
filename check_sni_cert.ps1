param(
    [string]$Ip = "51.89.234.110",
    [int]$Port = 4013,
    [string[]]$Sni = @("nav.trimline.co.ke", "main.trimline.co.ke")
)

foreach ($name in $Sni) {
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect($Ip, $Port)
        # Validate against the SNI name we send — this proves what a client
        # connecting to that hostname would see.
        $ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, ({ $true }))
        $ssl.AuthenticateAsClient($name, $null, [System.Security.Authentication.SslProtocols]::Tls12, $false)
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)
        Write-Host "=== SNI $name (via $Ip`:$Port)" -ForegroundColor Cyan
        Write-Host "  Subject : $($cert.Subject)"
        Write-Host "  Issuer  : $($cert.Issuer)"
        Write-Host "  Validity: $($cert.NotBefore.ToString('yyyy-MM-dd')) -> $($cert.NotAfter.ToString('yyyy-MM-dd'))"
        $san = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' }
        if ($san) { Write-Host "  SAN     : $($san.Format($false))" } else { Write-Host "  SAN     : (none)" }

        # Does the name actually match the presented certificate?
        $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
        $policyErrors = [System.Net.Security.SslPolicyErrors]::None
        $built = $chain.Build($cert)
        $nameMismatch = -not ($cert.Subject -like "*$name*" -or ($san -and $san.Format($false) -like "*$name*"))
        Write-Host "  NameMatch: $(-not $nameMismatch)" -ForegroundColor ($(if ($nameMismatch) { 'Red' } else { 'Green' }))
        $ssl.Dispose(); $tcp.Close()
    }
    catch { Write-Host "=== SNI $name : ERROR $($_.Exception.Message)" -ForegroundColor Red }
}
