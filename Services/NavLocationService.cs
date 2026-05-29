using System.ServiceModel;
using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public interface INavLocationService
    {
        Task<Loc.Locations?> ReadLocationAsync(string code);
        Task<Loc.Locations[]> ReadMultipleLocationsAsync(Loc.Locations_Filter[]? filters, int pageSize = 100);
    }

    public class NavLocationService : INavLocationService, IDisposable
    {
        private readonly Loc.Locations_PortClient _client;
        private readonly ILogger<NavLocationService> _logger;
        private readonly Client _clientInfo;
        private bool _disposed;

        public NavLocationService(Client clientInfo, ILogger<NavLocationService> logger)
        {
            _clientInfo = clientInfo;
            _logger = logger;
            
            // Initialize client using helper
            _client = NavClientHelper.InitializeClient<Loc.Locations>(clientInfo);
            
            _logger.LogInformation("NavLocationService initialized for client {ClientCode} at {Host}:{Port}",
                clientInfo.ClientCode, clientInfo.IPAddress, clientInfo.Port);
        }

        public async Task<Loc.Locations?> ReadLocationAsync(string code)
        {
            try
            {
                _logger.LogInformation("Reading location {Code} from NAV for client {ClientCode}", 
                    code, _clientInfo.ClientCode);
                
                var result = await _client.ReadAsync(code);
                return result?.Locations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading location {Code} from NAV", code);
                throw;
            }
        }

        public async Task<Loc.Locations[]> ReadMultipleLocationsAsync(Loc.Locations_Filter[]? filters, int pageSize = 100)
        {
            try
            {
                _logger.LogInformation("Reading multiple locations from NAV for client {ClientCode}", 
                    _clientInfo.ClientCode);
                
                var result = await _client.ReadMultipleAsync(filters ?? [], string.Empty, pageSize);
                return result?.ReadMultiple_Result1 ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading multiple locations from NAV");
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    if (_client.State == CommunicationState.Opened)
                    {
                        _client.Close();
                    }
                }
                catch
                {
                    _client.Abort();
                }
                _disposed = true;
            }
        }
    }
}
