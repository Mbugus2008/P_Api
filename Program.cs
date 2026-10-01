using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using ParcelAPI.Clients;
using ParcelAPI.Converters;
using ParcelAPI.Data;
using ParcelAPI.Filters;
using ParcelAPI.Middleware;
using ParcelAPI.Services;
using Serilog;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "Logs/parcel-api-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new NullableDateTimeConverter());
        // Accepts both "Received_Date_Time" and the legacy "Received_DateTime"
        // spellings on batch create/update, so older app builds persist their
        // dispatch/receive timestamps too.
        options.JsonSerializerOptions.Converters.Add(new NavBatchJsonConverter());
    });
// Gzip/brotli responses: a big help for the dashboard's many JSON calls.
builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
    c.OperationFilter<AddClientIdHeaderParameter>();
    c.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
});

// Add DbContext
builder.Services.AddDbContext<ParcelContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null)));

// Add services
builder.Services.AddScoped<IClientFactory, ClientFactory>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<ClientIdentifierFilter>();
builder.Services.AddScoped<NavSmsService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IEtimsService, EtimsService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Optional per-deployment override of the NAV/BC host (appsettings: Nav:HostOverride).
// Needed when the API runs on the same machine as NAV: calling it by its public
// hostname trips the Windows loopback check and fails with 401 Negotiate.
NavRuntimeSettings.HostOverride = builder.Configuration["Nav:HostOverride"];

// Ensure eTIMS table exists
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ParcelContext>();
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EtimsSettings')
            CREATE TABLE EtimsSettings (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                ClientCode NVARCHAR(50) NOT NULL,
                TinPin NVARCHAR(20) NOT NULL,
                BranchId NVARCHAR(10) DEFAULT '00',
                DeviceSerialNo NVARCHAR(100),
                ApiUsername NVARCHAR(100),
                ApiPassword NVARCHAR(200),
                CmcKey NVARCHAR(100) NULL,
                LastInvoiceNo INT DEFAULT 0,
                Environment NVARCHAR(20) DEFAULT 'Sandbox',
                IsActive BIT DEFAULT 1,
                CreatedAt DATETIME2 DEFAULT GETUTCDATE()
            )");
        // Add columns if upgrading from older table
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE EtimsSettings ADD CmcKey NVARCHAR(100) NULL"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE EtimsSettings ADD LastInvoiceNo INT DEFAULT 0"); } catch { }
    }
    catch { /* table may already exist */ }

    // Ensure marketer / billing tables exist
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ParcelMarketers')
            CREATE TABLE ParcelMarketers (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Code NVARCHAR(50) NOT NULL,
                Name NVARCHAR(150) NOT NULL,
                Phone NVARCHAR(30) NULL,
                Type NVARCHAR(20) NOT NULL DEFAULT 'Partner',
                PerParcelRate DECIMAL(18,2) NOT NULL DEFAULT 5,
                ReferralFee DECIMAL(18,2) NOT NULL DEFAULT 5000,
                Active BIT NOT NULL DEFAULT 1,
                CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            )");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ParcelMarketerClients')
            CREATE TABLE ParcelMarketerClients (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                MarketerCode NVARCHAR(50) NOT NULL,
                ClientCode NVARCHAR(50) NOT NULL,
                Type NVARCHAR(20) NOT NULL DEFAULT 'Partner',
                StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                EndedAt DATETIME2 NULL,
                Notes NVARCHAR(500) NULL
            )");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ParcelMarketerPayouts')
            CREATE TABLE ParcelMarketerPayouts (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                MarketerCode NVARCHAR(50) NOT NULL,
                ClientCode NVARCHAR(50) NULL,
                Period NVARCHAR(20) NOT NULL DEFAULT '',
                Kind NVARCHAR(30) NOT NULL DEFAULT 'ParcelCommission',
                ParcelCount INT NOT NULL DEFAULT 0,
                Amount DECIMAL(18,2) NOT NULL DEFAULT 0,
                Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
                PaidAt DATETIME2 NULL,
                Notes NVARCHAR(500) NULL,
                CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            )");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ParcelMarketers_Code')
            CREATE UNIQUE INDEX IX_ParcelMarketers_Code ON ParcelMarketers (Code)");
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ParcelMarketerPayouts_Marketer_Period')
            CREATE INDEX IX_ParcelMarketerPayouts_Marketer_Period ON ParcelMarketerPayouts (MarketerCode, Period)");
    }
    catch { /* tables may already exist */ }
}

// Configure the HTTP request pipeline
app.UseResponseCompression();
app.UseSwagger();
app.UseSwaggerUI();

// app.UseHttpsRedirection();  // disabled — HTTP used for older Android devices
var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
provider.Mappings[".apk"] = "application/vnd.android.package-archive";
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });

var parcelAppPath = System.IO.Path.Combine(app.Environment.ContentRootPath, "ParcelApp");
if (System.IO.Directory.Exists(parcelAppPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(parcelAppPath),
        RequestPath = "/ParcelApp",
        // Without this provider the .apk extension has no known content type
        // and the static file middleware answers 404 for the APK download.
        ContentTypeProvider = provider
    });
}
app.UseCors("AllowAll");

// Add custom middleware for client identification
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ClientIdentificationMiddleware>();

app.UseAuthorization();
app.MapControllers();

try
{
    Log.Information("Starting Parcel API");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}