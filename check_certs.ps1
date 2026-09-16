foreach ($h in 'main.trimline.co.ke', 'nav.trimline.co.ke') {
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect($h, 4013)
        $ssl = New-Object System.Net.Security.SslStream($tcp.GetStream(), $false, ({ $true }))
        $ssl.AuthenticateAsClient($h)
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)
        Write-Host "=== $h" -ForegroundColor Cyan
        Write-Host "  Subject : $($cert.Subject)"
        Write-Host "  Issuer  : $($cert.Issuer)"
        Write-Host "  Validity: $($cert.NotBefore.ToString('yyyy-MM-dd')) -> $($cert.NotAfter.ToString('yyyy-MM-dd'))"
        $san = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' }
        if ($san) { Write-Host "  SAN     : $($san.Format($false))" } else { Write-Host "  SAN     : (none)" }
        $ssl.Dispose(); $tcp.Close()
    }
    catch { Write-Host "=== $h : ERROR $($_.Exception.Message)" -ForegroundColor Red }
}
