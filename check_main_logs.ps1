# check_main_logs.ps1
# Reads the ParcelAPI log files on the main server and summarises errors.
# You will be prompted once for the Windows Administrator password (typed locally).
#
# Usage:
#   pwsh -ExecutionPolicy Bypass -File .\check_main_logs.ps1
#   pwsh -ExecutionPolicy Bypass -File .\check_main_logs.ps1 -Days 7 -Show 40
#   pwsh -ExecutionPolicy Bypass -File .\check_main_logs.ps1 -AppErrorsOnly

param(
    [string]$RemoteHost = "main.trimline.co.ke",
    [string]$RemoteUser = "Administrator",
    [string]$LogPath = "D:\Parcel\Logs",
    [int]$Days = 3,
    [int]$Show = 25,
    [switch]$AppErrorsOnly
)

$ErrorActionPreference = "Stop"

$cred = Get-Credential -UserName $RemoteUser -Message "Windows password for $RemoteUser@$RemoteHost"
if (-not $cred) { throw "No credentials supplied." }

$trusted = (Get-Item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue).Value
if ($trusted -notlike "*$RemoteHost*") {
    $new = if ([string]::IsNullOrWhiteSpace($trusted)) { $RemoteHost } else { "$trusted,$RemoteHost" }
    Write-Host "Adding $RemoteHost to local TrustedHosts..."
    Set-Item WSMan:\localhost\Client\TrustedHosts -Value $new -Force
}

$session = New-PSSession -ComputerName $RemoteHost -Credential $cred -Authentication Negotiate
try {
    Invoke-Command -Session $session -ScriptBlock {
        param($LogPath, $Days, $Show, $AppErrorsOnly)

        function Find-LogFolder {
            param([string]$Preferred)

            $candidates = New-Object System.Collections.Generic.List[string]
            $candidates.Add($Preferred)

            # Ask IIS for the Parcel site's physical path
            $appcmd = "$env:SystemRoot\System32\inetsrv\appcmd.exe"
            if (Test-Path $appcmd) {
                Write-Output "=== IIS VIRTUAL DIRECTORIES ==="
                $paths = & $appcmd list vdir /text:physicalPath 2>$null
                foreach ($p in $paths) {
                    if ($p) { Write-Output ("  " + $p) }
                    if ($p -and $p -match 'Parcel') {
                        $candidates.Add((Join-Path $p 'Logs'))
                        $parent = Split-Path $p -Parent
                        if ($parent) { $candidates.Add((Join-Path $parent 'Logs')) }
                    }
                }
            }

            # Common locations as a last resort
            foreach ($root in @('D:\', 'C:\inetpub', 'C:\')) {
                if (Test-Path $root) {
                    $dirs = Get-ChildItem $root -ErrorAction SilentlyContinue | Where-Object { $_.PSIsContainer }
                    foreach ($d in $dirs) {
                        if ($d.Name -match 'Parcel|Logs') {
                            $candidates.Add($d.FullName)
                            if ($d.Name -match 'Parcel') { $candidates.Add((Join-Path $d.FullName 'Logs')) }
                        }
                    }
                }
            }

            foreach ($c in $candidates) {
                if ($c -and (Test-Path $c)) {
                    $logs = Get-ChildItem $c -ErrorAction SilentlyContinue | Where-Object {
                        (-not $_.PSIsContainer) -and ($_.Name -like '*.log' -or $_.Name -like '*.txt')
                    }
                    if ($logs -and $logs.Count -gt 0) { return @($c, $logs) }
                }
            }
            return @($null, @())
        }

        $found = Find-LogFolder -Preferred $LogPath
        $folder = $found[0]
        $logs = @($found[1])

        if (-not $folder) {
            Write-Output ""
            Write-Output "No log folder with .log files was found."
            Write-Output "Search folders listed above — re-run with -LogPath '<folder>'."
            return
        }

        Write-Output ""
        Write-Output "=== USING LOG FOLDER: $folder ==="

        $since = (Get-Date).AddDays(-$Days)
        $files = $logs | Where-Object { $_.LastWriteTime -ge $since } | Sort-Object LastWriteTime -Descending

        Write-Output "=== LOG FILES (last $Days days) ==="
        foreach ($f in $files) {
            Write-Output ("  {0,-32} {1,8:N1} MB   last write {2:yyyy-MM-dd HH:mm}" -f $f.Name, ($f.Length / 1MB), $f.LastWriteTime)
        }
        if (-not $files) {
            Write-Output "  (no files modified in the last $Days days — showing the newest anyway)"
            $files = $logs | Sort-Object LastWriteTime -Descending | Select-Object -First 3
            foreach ($f in $files) {
                Write-Output ("  {0,-32} {1,8:N1} MB   last write {2:yyyy-MM-dd HH:mm}" -f $f.Name, ($f.Length / 1MB), $f.LastWriteTime)
            }
        }
        if (-not $files) { return }

        $pattern = if ($AppErrorsOnly) { 'APP ERROR' } else { '\[ERR\]|\[FTL\]|APP ERROR|Unhandled|SOAP|Fault' }

        Write-Output ""
        Write-Output "=== ERROR COUNTS PER FILE ==="
        $totalErrors = 0
        foreach ($f in $files) {
            $hits = @(Select-String -Path $f.FullName -Pattern $pattern -ErrorAction SilentlyContinue)
            $totalErrors += $hits.Count
            Write-Output ("  {0,-32} {1} matches" -f $f.Name, $hits.Count)
        }
        Write-Output "  TOTAL: $totalErrors"

        if ($totalErrors -eq 0) { return }

        Write-Output ""
        Write-Output "=== TOP ERROR MESSAGES ==="
        $all = $files | ForEach-Object { Select-String -Path $_.FullName -Pattern $pattern -ErrorAction SilentlyContinue }
        $all |
            ForEach-Object { ($_.Line -replace '^\[[^\]]+\]\s*', '') } |
            ForEach-Object { if ($_.Length -gt 140) { $_.Substring(0, 140) } else { $_ } } |
            Group-Object |
            Sort-Object Count -Descending |
            Select-Object -First 12 |
            ForEach-Object { Write-Output ("  [{0,5}x] {1}" -f $_.Count, $_.Name) }

        Write-Output ""
        Write-Output "=== MOST RECENT $Show MATCHES ==="
        $all | Select-Object -Last $Show | ForEach-Object {
            $line = $_.Line
            if ($line.Length -gt 220) { $line = $line.Substring(0, 220) }
            Write-Output ("  " + $line)
        }
    } -ArgumentList $LogPath, $Days, $Show, $AppErrorsOnly.IsPresent
}
finally {
    Remove-PSSession $session -ErrorAction SilentlyContinue
}
