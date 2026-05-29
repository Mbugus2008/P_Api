param(
    [string]$RemoteHost = "nav.trimline.co.ke",
    [int]$RemotePort = 8172,
    [string]$SiteName = "Parcel",
    [string]$UserName = "Administrator",
    [string]$ProjectPath = "",
    [string]$PublishPath = "",
    [switch]$SkipPublish,
    [switch]$AllowUntrusted = $true,
    [switch]$DisableAppOffline,
    [switch]$DryRun,
    [string]$Password
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $PSScriptRoot "ParcelAPI.csproj"
}

if ([string]::IsNullOrWhiteSpace($PublishPath)) {
    $PublishPath = Join-Path $PSScriptRoot "publish"
}

function Resolve-MsDeployPath {
    $cmd = Get-Command msdeploy -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $common = @(
        "C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe",
        "C:\Program Files (x86)\IIS\Microsoft Web Deploy V3\msdeploy.exe"
    )

    foreach ($path in $common) {
        if (Test-Path $path) {
            return $path
        }
    }

    throw "msdeploy.exe not found. Install Web Deploy 3.6+ on this machine."
}

function Get-PlainTextPassword {
    param([string]$Provided)

    if (-not [string]::IsNullOrWhiteSpace($Provided)) {
        return $Provided
    }

    $secure = Read-Host "Enter Web Deploy password for $UserName" -AsSecureString
    $cred = New-Object System.Management.Automation.PSCredential("u", $secure)
    return $cred.GetNetworkCredential().Password
}

$msdeploy = Resolve-MsDeployPath

if (-not $SkipPublish) {
    Write-Host "Publishing project..."
    & dotnet publish $ProjectPath -c Release -o $PublishPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed."
    }
}

if (-not (Test-Path $PublishPath)) {
    throw "Publish path not found: $PublishPath"
}

$plainPassword = Get-PlainTextPassword -Provided $Password
$computerName = "https://$RemoteHost`:$RemotePort/msdeploy.axd?site=$SiteName"

$sourceArg = "-source:contentPath=$PublishPath"
$destArg = "-dest:contentPath=$SiteName,computerName=$computerName,userName=$UserName,password=$plainPassword,authType=Basic,includeAcls=False"

$args = @(
    "-verb:sync",
    $sourceArg,
    $destArg,
    "-enableRule:DoNotDeleteRule",
    "-retryAttempts:2",
    "-retryInterval:1000"
)

if (-not $DisableAppOffline) {
    $args += "-enableRule:AppOffline"
}

if ($AllowUntrusted) {
    $args += "-allowUntrusted"
}

Write-Host "Using msdeploy: $msdeploy"
Write-Host "Deploy target: $computerName"
Write-Host "Site name: $SiteName"

if ($DryRun) {
    Write-Host "Dry run enabled. Command preview:"
    Write-Host "$msdeploy $($args -join ' ')"
    exit 0
}

Write-Host "Running Web Deploy sync..."
& $msdeploy @args

if ($LASTEXITCODE -ne 0) {
    throw "msdeploy failed with exit code $LASTEXITCODE"
}

Write-Host "Web Deploy completed successfully."
