using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using ParcelAPI.Clients;
using ParcelAPI.Converters;
using ParcelAPI.Data;
using ParcelAPI.Filters;
using ParcelAPI.Middleware;
using ParcelAPI.Services;
using Serilog;
using System.Text.Json;
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
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new NullableDateTimeConverter());
    });
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

// Configure the HTTP request pipeline
app.UseSwagger();
app.UseSwaggerUI();

// app.UseHttpsRedirection();  // disabled — HTTP used for older Android devices
app.UseDefaultFiles();
app.UseStaticFiles();

var parcelAppPath = System.IO.Path.Combine(app.Environment.ContentRootPath, "ParcelApp");
if (System.IO.Directory.Exists(parcelAppPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(parcelAppPath),
        RequestPath = "/ParcelApp"
    });
}
app.UseCors("AllowAll");

// Add custom middleware for client identification
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