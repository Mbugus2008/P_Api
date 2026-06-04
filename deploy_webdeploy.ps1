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
    [string]$Password,
    [switch]$SkipHealthCheck,
    [switch]$RunMigrations,
    [string]$ConnectionString = "",
    [int]$HealthCheckTimeoutSeconds = 60
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

# --- STEP 1: Run database migrations BEFORE code deploy ---
if ($RunMigrations) {
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        Write-Host "WARNING: -RunMigrations specified but no -ConnectionString provided. Skipping migrations."
        Write-Host "  Pass the connection string from appsettings.json or skip with -SkipHealthCheck and no -ConnectionString"
    } else {
        Write-Host "Running EF Core migrations BEFORE code deploy..."
        $migrationResult = & dotnet ef database update --project $ProjectPath --connection "$ConnectionString" 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "WARNING: Migration may have failed. Check output:"
            Write-Host $migrationResult
            Write-Host "Proceeding with deploy anyway (schema may be ahead of code)."
        } else {
            Write-Host "Migrations applied successfully."
        }
    }
}

# --- STEP 2: Pre-deploy health check ---
$healthUrl = "https://$RemoteHost/api/Health"
if (-not $SkipHealthCheck) {
    Write-Host "Checking current health before deployment..."
    try {
        $currentHealth = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 10
        Write-Host "Current API is running (HTTP $($currentHealth.StatusCode)). Proceeding with deploy."
    } catch {
        Write-Host "WARNING: Pre-deploy health check failed. API may be down already. Proceeding anyway."
        Write-Host "  $($_.Exception.Message)"
    }
}

# --- STEP 3: Deploy via Web Deploy ---
$plainPassword = Get-PlainTextPassword -Provided $Password
$computerName = "https://$RemoteHost`:$RemotePort/msdeploy.axd?site=$SiteName"

$sourceArg = "-source:contentPath=$PublishPath"
$destArg = "-dest:contentPath=$SiteName,computerName=$computerName,userName=$UserName,password=$plainPassword,authType=Basic,includeAcls=False"

$args = @(
    "-verb:sync",
    $sourceArg,
    $destArg,
    "-enableRule:DoNotDeleteRule",
    "-retryAttempts:3",
    "-retryInterval:5000"
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

Write-Host "Running Web Deploy sync (app_offline will drain connections)..."
$deployStart = Get-Date
& $msdeploy @args
$deployDuration = (Get-Date) - $deployStart

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: msdeploy failed with exit code $LASTEXITCODE"
    Write-Host "  Duration: $($deployDuration.TotalSeconds)s"
    Write-Host "  Check the server or run with -DryRun to preview the command."
    exit $LASTEXITCODE
}

Write-Host "Web Deploy sync completed in $($deployDuration.TotalSeconds.ToString('F1'))s"

# --- STEP 4: Post-deploy health check (wait for app to come back) ---
if (-not $SkipHealthCheck) {
    Write-Host "Waiting for API to become healthy (timeout: ${HealthCheckTimeoutSeconds}s)..."
    $healthUrl = "https://$RemoteHost/api/Health"
    
    $timeout = [DateTime]::UtcNow.AddSeconds($HealthCheckTimeoutSeconds)
    $healthy = $false
    
    while ([DateTime]::UtcNow -lt $timeout) {
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                $healthy = $true
                break
            }
        } catch {
            # API not ready yet — wait and retry
        }
        Write-Host "  Waiting..."
        Start-Sleep -Seconds 3
    }
    
    if ($healthy) {
        Write-Host "✅ API is healthy and serving requests after deployment!"
    } else {
        Write-Host "⚠️  WARNING: API health check did not pass within timeout."
        Write-Host "   Manual verification recommended at: $healthUrl"
    }
}

Write-Host "✅ Deployment completed successfully."
