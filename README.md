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

### Incremental Deploy (Changed Files Only)

Use the incremental deploy script to upload only files that changed since the last deployment:

```powershell
pwsh ./deploy_incremental.ps1 -RunPublish
```

Options:
- `-RemoteHost` defaults to `nav.trimline.co.ke`
- `-RemoteUser` defaults to `Administrator`
- `-RemotePath` defaults to `D:\Parcel`
- `-LocalPublishPath` defaults to `./publish`

The script keeps a manifest on the server at `D:\Parcel\.deploy-manifest.json` and compares SHA256 hashes to send only changed files.

### Web Deploy (Recommended for Fast IIS Deploys)

Use Web Deploy to publish directly to IIS with automatic `app_offline.htm` handling during file sync.

Server one-time setup (already applied on `nav.trimline.co.ke`):

```powershell
sc config WMSVC start= auto
net start WMSVC
netsh advfirewall firewall add rule name=WebDeploy8172 dir=in action=allow protocol=TCP localport=8172
```

Verify remote endpoint is reachable:

```powershell
Test-NetConnection nav.trimline.co.ke -Port 8172
```

Deploy command:

```powershell
pwsh ./deploy_webdeploy.ps1
```

Useful options:
- `-RemoteHost` defaults to `nav.trimline.co.ke`
- `-RemotePort` defaults to `8172`
- `-SiteName` defaults to `Parcel`
- `-UserName` defaults to `Administrator`
- `-SkipPublish` to deploy an already-built `./publish` folder
- `-DryRun` to preview the `msdeploy` command
- `-Password` to pass password non-interactively (for CI)

Example:

```powershell
pwsh ./deploy_webdeploy.ps1 -SiteName Parcel -UserName Administrator
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