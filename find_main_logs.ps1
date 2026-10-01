# find_main_logs.ps1
# Connects to the main server and reports where ParcelAPI lives and where its
# log files are. Password is typed locally at the prompt.
#
# Usage: pwsh -ExecutionPolicy Bypass -File .\find_main_logs.ps1

param(
    [string]$RemoteHost = "main.trimline.co.ke",
    [string]$RemoteUser = "Administrator",
    [int]$Days = 14
)

$ErrorActionPreference = "Stop"

$cred = Get-Credential -UserName $RemoteUser -Message "Windows password for $RemoteUser@$RemoteHost"
if (-not $cred) { throw "No credentials supplied." }

$trusted = (Get-Item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue).Value
if ($trusted -notlike "*$RemoteHost*") {
    $new = if ([string]::IsNullOrWhiteSpace($trusted)) { $RemoteHost } else { "$trusted,$RemoteHost" }
    Set-Item WSMan:\localhost\Client\TrustedHosts -Value $new -Force
}

$session = New-PSSession -ComputerName $RemoteHost -Credential $cred -Authentication Negotiate
try {
    Invoke-Command -Session $session -ScriptBlock {
        param($Days)

        Write-Output ("PowerShell version: " + $PSVersionTable.PSVersion.ToString())
        Write-Output ("Computer: " + $env:COMPUTERNAME)

        Write-Output ""
        Write-Output "--- IIS physical paths (appcmd list vdir) ---"
        $appcmd = "$env:SystemRoot\System32\inetsrv\appcmd.exe"
        if (Test-Path $appcmd) {
            & $appcmd list vdir /text:physicalPath
        }
        else {
            Write-Output "  (appcmd not found)"
        }

        Write-Output ""
        Write-Output "--- D:\ contents ---"
        Get-ChildItem D:\ -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("  " + $_.FullName) }

        Write-Output ""
        Write-Output "--- C:\inetpub contents ---"
        if (Test-Path C:\inetpub) {
            Get-ChildItem C:\inetpub -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("  " + $_.FullName) }
        }
        else { Write-Output "  (no C:\inetpub)" }

        Write-Output ""
        Write-Output "--- *.log files modified in the last $Days days (searched under D:\ and C:\inetpub) ---"
        $cutoff = (Get-Date).AddDays(-$Days)
        $found = 0
        foreach ($root in @('D:\', 'C:\inetpub', 'C:\ProgramData')) {
            if (-not (Test-Path $root)) { continue }
            $list = & cmd.exe /c "dir /s /b `"$root`*.log`" 2>nul"
            foreach ($path in $list) {
                if (-not $path) { continue }
                try {
                    $item = Get-Item $path -ErrorAction Stop
                    if ($item.LastWriteTime -ge $cutoff) {
                        Write-Output ("  {0}  |  {1:N1} MB  |  {2:yyyy-MM-dd HH:mm}" -f $item.FullName, ($item.Length / 1MB), $item.LastWriteTime)
                        $found++
                    }
                }
                catch { }
            }
        }
        if ($found -eq 0) { Write-Output "  (none found)" }

        Write-Output ""
        Write-Output "--- ParcelAPI.dll locations ---"
        foreach ($root in @('D:\', 'C:\inetpub', 'C:\')) {
            if (-not (Test-Path $root)) { continue }
            $list = & cmd.exe /c "dir /s /b `"$root`ParcelAPI.dll`" 2>nul"
            foreach ($path in $list) { if ($path) { Write-Output ("  " + $path) } }
        }
    } -ArgumentList $Days
}
finally {
    Remove-PSSession $session -ErrorAction SilentlyContinue
}
