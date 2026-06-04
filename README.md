# ParcelAPI

A modern ASP.NET Core Web API for managing parcel operations with multi-tenant support.

## Features

- ? Multi-tenant support via `X-Client-Identifier` header
- ? Entity Framework Core with SQL Server
- ? Structured logging with Serilog
- ? RESTful API design
- ? Swagger/OpenAPI documentation
- ? Async/await patterns throughout
- ? CORS enabled
- ? Clean architecture (Controllers ? Services ? Data)

## Endpoints

### Parcels
- `POST /api/Parcel/Parcels` - Get parcels with filtering and pagination
- `POST /api/Parcel/updateparcels` - Add or update a parcel
- `POST /api/Parcel/Locations` - Get all locations for a client
- `POST /api/Parcel/Users` - Get parcel users/agents for a client

## Getting Started

### Prerequisites
- .NET 9 SDK
- SQL Server
- Visual Studio 2022 or VS Code

### Configuration

1. Update the connection string in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=ParcelDB;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
  }
}
```

2. Run Entity Framework migrations:
```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Running the API

```bash
cd ParcelAPI
dotnet restore
dotnet build
dotnet run
```

The API will be available at:
- HTTP: http://localhost:5000
- HTTPS: https://localhost:5001
- Swagger UI: https://localhost:5001/swagger

## 🚀 Production Pipeline — Rapid, Seamless Updates

### Overview

```
Git Push → GitHub Actions → Build → Web Deploy (app_offline) → Health Check → Done!
                     ↓
              DB Migrations (before code)
```

### Zero-Downtime Strategy

This pipeline uses **Web Deploy with `app_offline.htm`** — the gold standard for IIS deployments:

1. `app_offline.htm` is placed in the app root → IIS immediately serves it (no more app processing)
2. **In-flight requests complete gracefully** (no abrupt kill)
3. All new files are synced
4. `app_offline.htm` is removed → IIS restarts the app pool with new code
5. New requests hit the updated API

**Typical downtime:** 3–10 seconds (the time to sync files).

### CI/CD Pipeline (Recommended)

A GitHub Actions workflow is included at `.github/workflows/deploy.yml`. On every push to `main`:

| Stage | Description |
|---|---|
| 1. Build | `dotnet restore` → `dotnet build -c Release` |
| 2. Publish | `dotnet publish -c Release -o ./publish` |
| 3. Deploy | `msdeploy` with `-enableRule:AppOffline` to target IIS |
| 4. Verify | (Optional) health check confirms API is serving |

**To use GitHub Actions:**
1. Push the `.github/workflows/deploy.yml` to your repo
2. Add these **repository secrets** (`Settings → Secrets and variables → Actions`):
   - `REMOTE_HOST` — e.g., `nav.trimline.co.ke`
   - `DEPLOY_USER` — e.g., `Administrator`
   - `DEPLOY_PASSWORD` — Web Deploy password
   - `MSDEPLOY_PORT` — (optional, default `8172`)
   - `IIS_SITE_NAME` — (optional, default `Parcel`)

### Local Deploy Commands

#### Fastest: Web Deploy (msdeploy) — with app_offline

```powershell
# Build + deploy in one command
pwsh ./deploy_webdeploy.ps1

# With DB migrations (run BEFORE code deploy for zero-downtime)
pwsh ./deploy_webdeploy.ps1 -RunMigrations -ConnectionString "Server=...;Database=ParcelDB;..."

# Deploy only (skip publish, reuse ./publish folder)
pwsh ./deploy_webdeploy.ps1 -SkipPublish

# Non-interactive (for CI)
pwsh ./deploy_webdeploy.ps1 -Password "yourpassword"

# Preview command without running
pwsh ./deploy_webdeploy.ps1 -DryRun
```

**Options:**
| Parameter | Default | Description |
|---|---|---|
| `-RemoteHost` | `nav.trimline.co.ke` | Target server |
| `-RemotePort` | `8172` | Web Deploy port |
| `-SiteName` | `Parcel` | IIS site name |
| `-UserName` | `Administrator` | Server admin |
| `-Password` | *(prompt)* | Pass non-interactively for CI |
| `-SkipPublish` | off | Skip `dotnet publish`, use existing `./publish` folder |
| `-DisableAppOffline` | off | Skip app_offline (slightly faster, but risk of 502s) |
| `-DryRun` | off | Only print the msdeploy command |
| `-RunMigrations` | off | Run `dotnet ef database update` BEFORE code deploy |
| `-ConnectionString` | `""` | Required with `-RunMigrations` |
| `-SkipHealthCheck` | off | Skip pre/post health verification |

#### Incremental Deploy (Only Changed Files)

For very quick updates when you only changed a few files:

```powershell
pwsh ./deploy_incremental.ps1 -RunPublish
```

Keeps a manifest (`D:\Parcel\.deploy-manifest.json`) and uploads only files whose SHA256 hash changed.

### 🗄️ Database Migration Strategy

To avoid downtime during schema changes, **deploy migrations before code**:

```powershell
# 1. Apply new schema (old code still runs fine)
dotnet ef database update --connection "..."

# 2. Deploy new code (now compatible with new schema)
pwsh ./deploy_webdeploy.ps1

# OR in one step:
pwsh ./deploy_webdeploy.ps1 -RunMigrations -ConnectionString "..."
```

**Rules for zero-downtime schema changes:**
- ✅ Add new columns as **nullable** (old code ignores them)
- ✅ Add new tables (old code doesn't reference them)
- ❌ Never **rename** columns (add new, drop old in next deploy)
- ❌ Never **remove** columns old code still references

### 🔍 Health Checks

Two endpoints available:

| Endpoint | Purpose |
|---|---|
| `GET /api/Health` | Lightweight — just returns 200 if app is alive |
| `GET /api/Health/ready` | Deep check — verifies DB connectivity |

The Android app can poll `/api/Health` and retry if it gets a non-200 response during deployment.

### 📱 Android App Updates (AppUpdateController)

The existing `AppUpdateController` serves APK version info. Configure in `appsettings.json`:

```json
"AppVersion": {
  "Version": "2.1.0",
  "VersionCode": 7,
  "BuildDate": "2025-06-01",
  "DownloadUrl": "https://yourdomain.com/ParcelApp/ParcelApp.apk",
  "ReleaseNotes": "Bug fixes and performance improvements",
  "ForceUpdate": false
}
```

### 🔄 Rollback Plan

If a deploy goes wrong:

```powershell
# Rollback using Web Deploy to previous version
# (Keep a backup of the previous publish folder)
pwsh ./deploy_webdeploy.ps1 -PublishPath ./publish_backup_v1 -SkipPublish
```

Or use the **incremental script** pointing to a backed-up manifest.

### 📊 Monitoring

- **Logs:** `Logs/parcel-api-YYYYMMDD.log` (Serilog rolling file)
- **Health:** `GET /api/Health` endpoint
- **IIS:** Check Event Viewer on the server

---

## Quick Start (for existing contributors)

```bash
git clone <repo>
cd ParcelAPI
dotnet restore
dotnet build
dotnet run
```

## Usage

All requests require the `X-Client-Identifier` header:

```bash
curl -X POST "https://localhost:5001/api/Parcel/Parcels" \
  -H "X-Client-Identifier: CLIENT001" \
  -H "Content-Type: application/json" \
  -d '{
    "searchTerm": "PKG",
    "pageNumber": 1,
    "pageSize": 50
  }'
```

## Project Structure

```
ParcelAPI/
??? Controllers/        # API controllers
??? Models/            # Domain models and DTOs
??? Data/              # DbContext and configurations
??? Services/          # Business logic layer
??? Middleware/        # Custom middleware
??? Logs/              # Application logs (generated)
??? appsettings.json   # Configuration
```

## Technologies

- ASP.NET Core 9.0
- Entity Framework Core 9.0
- Serilog for logging
- Swashbuckle for API documentation
- SQL Server

## Improvements from Original

1. **Dependency Injection**: All services properly injected
2. **Async Operations**: All database operations are async
3. **Structured Logging**: Serilog with file and console outputs
4. **Error Handling**: Consistent error responses with HTTP status codes
5. **Clean Architecture**: Separated concerns (Controller ? Service ? Repository)
6. **Type Safety**: Strong typing throughout
7. **Configuration**: Proper use of configuration system
8. **Middleware**: Custom middleware for cross-cutting concerns
9. **API Documentation**: Integrated Swagger/OpenAPI
10. **Modern Patterns**: Following .NET 9 best practices

## License

MIT