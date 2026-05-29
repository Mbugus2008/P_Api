param(
    [string]$RemoteHost = "nav.trimline.co.ke",
    [string]$RemoteUser = "Administrator",
    [string]$RemotePath = "D:\Parcel",
    [string]$LocalPublishPath = "",
    [switch]$RunPublish,
    [string]$ProjectPath = "",
    [switch]$SkipManifestUpload
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $PSScriptRoot "ParcelAPI.csproj"
}

if ([string]::IsNullOrWhiteSpace($LocalPublishPath)) {
    $LocalPublishPath = Join-Path $PSScriptRoot "publish"
}

$remote = "$RemoteUser@$RemoteHost"
$remoteManifestPath = "$RemotePath\.deploy-manifest.json"

function Assert-Tool {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is not available in PATH."
    }
}

function Invoke-RemotePowerShell {
    param(
        [string]$ScriptText
    )

    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($ScriptText))
    $cmd = "powershell -NoProfile -NonInteractive -EncodedCommand $encoded"
    return & ssh $remote $cmd
}

Assert-Tool -Name "ssh"
Assert-Tool -Name "scp"

if ($RunPublish) {
    Write-Host "Publishing project..."
    & dotnet publish $ProjectPath -c Release -o $LocalPublishPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed."
    }
}

if (-not (Test-Path $LocalPublishPath)) {
    throw "Local publish path not found: $LocalPublishPath"
}

$publishRoot = (Resolve-Path $LocalPublishPath).Path
$publishRootUri = New-Object System.Uri(($publishRoot.TrimEnd('\\') + '\\'))

Write-Host "Reading previous remote manifest (if present)..."
$remoteManifestRaw = Invoke-RemotePowerShell -ScriptText @"
if (Test-Path '$remoteManifestPath') {
    Get-Content -Raw '$remoteManifestPath'
}
"@

$remoteManifest = @{}
if ($remoteManifestRaw -and ($remoteManifestRaw -join "").Trim().Length -gt 0) {
    $parsed = ($remoteManifestRaw -join "`n") | ConvertFrom-Json
    if ($parsed.files) {
        foreach ($f in $parsed.files) {
            $remoteManifest[$f.path] = $f.hash
        }
    }
}

Write-Host "Hashing local publish files..."
$localFiles = Get-ChildItem -Path $LocalPublishPath -Recurse -File
$localEntries = New-Object System.Collections.Generic.List[object]

foreach ($file in $localFiles) {
    $fileUri = New-Object System.Uri($file.FullName)
    $relative = [System.Uri]::UnescapeDataString($publishRootUri.MakeRelativeUri($fileUri).ToString()).Replace('\\', '/')
    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash
    $localEntries.Add([PSCustomObject]@{
        path = $relative
        hash = $hash
        source = $file.FullName
    })
}

$changed = New-Object System.Collections.Generic.List[object]
foreach ($entry in $localEntries) {
    if (-not $remoteManifest.ContainsKey($entry.path) -or $remoteManifest[$entry.path] -ne $entry.hash) {
        $changed.Add($entry)
    }
}

Write-Host ("Local files: {0}" -f $localEntries.Count)
Write-Host ("Changed files: {0}" -f $changed.Count)

if ($changed.Count -eq 0) {
    Write-Host "No changed files detected. Nothing to upload."
    exit 0
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("parcel-delta-" + [Guid]::NewGuid().ToString("N"))
$zipPath = Join-Path ([IO.Path]::GetTempPath()) ("parcel-delta-" + [Guid]::NewGuid().ToString("N") + ".zip")
$manifestPath = Join-Path ([IO.Path]::GetTempPath()) ("parcel-manifest-" + [Guid]::NewGuid().ToString("N") + ".json")

try {
    New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null

    foreach ($entry in $changed) {
        $relativeWindows = $entry.path.Replace('/', '\\')
        $targetFile = Join-Path $tempRoot $relativeWindows
        $targetDir = Split-Path -Path $targetFile -Parent
        if (-not (Test-Path $targetDir)) {
            New-Item -Path $targetDir -ItemType Directory -Force | Out-Null
        }
        Copy-Item -Path $entry.source -Destination $targetFile -Force
    }

    $archiveItems = Get-ChildItem -Path $tempRoot -Recurse -File
    if (-not $archiveItems -or $archiveItems.Count -eq 0) {
        throw "No files were staged for delta archive."
    }

    Compress-Archive -Path $archiveItems.FullName -DestinationPath $zipPath -Force -ErrorAction Stop
    if (-not (Test-Path $zipPath)) {
        throw "Failed to create delta archive at $zipPath"
    }

    Write-Host "Uploading delta package..."
    $deltaTarget = "{0}:/D:/Parcel/_delta.zip" -f $remote
    & scp $zipPath $deltaTarget
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upload delta archive."
    }

    Write-Host "Applying delta on remote server..."
    Invoke-RemotePowerShell -ScriptText @"
if (-not (Test-Path '$RemotePath')) {
    New-Item -Path '$RemotePath' -ItemType Directory -Force | Out-Null
}
Expand-Archive -Path '$RemotePath\\_delta.zip' -DestinationPath '$RemotePath' -Force
Remove-Item '$RemotePath\\_delta.zip' -Force
"@ | Out-Null

    if (-not $SkipManifestUpload) {
        $manifest = [PSCustomObject]@{
            generatedAtUtc = [DateTime]::UtcNow.ToString("o")
            files = $localEntries | ForEach-Object {
                [PSCustomObject]@{
                    path = $_.path
                    hash = $_.hash
                }
            }
        }

        $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

        Write-Host "Uploading updated deploy manifest..."
        $manifestTarget = "{0}:/D:/Parcel/.deploy-manifest.json" -f $remote
        & scp $manifestPath $manifestTarget
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to upload deploy manifest."
        }
    }

    Write-Host "Incremental deployment complete."
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }
    if (Test-Path $manifestPath) {
        Remove-Item -Path $manifestPath -Force
    }
}
