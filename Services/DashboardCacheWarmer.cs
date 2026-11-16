using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using ParcelAPI.Clients;
using ParcelAPI.Controllers;
using ParcelAPI.Data;

namespace ParcelAPI.Services
{
    /// <summary>
    /// Keeps the dashboard caches warm.
    ///
    /// A cold dashboard cache costs one full Business Central read (~50s), and
    /// that read used to land on whoever opened the dashboard first — typically
    /// the mobile app after a quiet period. This service refreshes the caches in
    /// the background every few minutes, so every request is served from memory
    /// in about a second.
    ///
    /// Configuration (appsettings.json):
    ///   Dashboard:CacheWarmMinutes = 5     (0 disables the warmer)
    ///   Dashboard:WarmClientCodes  = []    (empty = every active client)
    /// </summary>
    public class DashboardCacheWarmer : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DashboardCacheWarmer> _logger;
        private readonly IConfiguration _configuration;

        public DashboardCacheWarmer(
            IServiceScopeFactory scopeFactory,
            ILogger<DashboardCacheWarmer> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var minutes = _configuration.GetValue("Dashboard:CacheWarmMinutes", 5);
            if (minutes <= 0)
            {
                _logger.LogInformation("Dashboard cache warmer is disabled (Dashboard:CacheWarmMinutes <= 0)");
                return;
            }

            var configured = _configuration.GetSection("Dashboard:WarmClientCodes").Get<string[]>();
            var only = configured?
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .ToArray();

            // Let the host finish starting (and the IIS app pool settle) first.
            await DelayAsync(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await WarmOnceAsync(only, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Dashboard cache warm-up failed");
                }

                await DelayAsync(TimeSpan.FromMinutes(minutes), stoppingToken);
            }
        }

        private async Task WarmOnceAsync(string[]? only, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ParcelContext>();
            var factory = scope.ServiceProvider.GetRequiredService<IClientFactory>();

            var codes = await db.Clients
                .Where(c => c.Active)
                .Select(c => c.ClientCode)
                .ToListAsync(cancellationToken);

            foreach (var code in codes)
            {
                if (only is { Length: > 0 } &&
                    !only.Contains(code, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (cancellationToken.IsCancellationRequested) return;

                IClient? client;
                try
                {
                    client = await factory.GetClientAsync(code);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Dashboard warmer could not create client {ClientCode}", code);
                    continue;
                }

                if (client?.NavParcelService == null) continue;

                var started = System.Diagnostics.Stopwatch.StartNew();
                await DashboardController.WarmAsync(client, cancellationToken);
                _logger.LogDebug(
                    "Dashboard cache warmed for {ClientCode} in {Elapsed} ms",
                    code,
                    started.ElapsedMilliseconds);
            }
        }

        private static async Task DelayAsync(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }
}
