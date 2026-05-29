using System.ServiceModel;
using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public interface INavVehicleService
    {
        Task<Vehicles.VehiclesBasics?> ReadVehicleAsync(string vehicleNumber);
        Task<Vehicles.VehiclesBasics[]> ReadMultipleVehiclesAsync(Vehicles.VehiclesBasics_Filter[]? filters, int pageSize = 100);
    }

    public class NavVehicleService : INavVehicleService, IDisposable
    {
        private readonly Vehicles.VehiclesBasics_PortClient _client;
        private readonly ILogger<NavVehicleService> _logger;
        private readonly Client _clientInfo;
        private bool _disposed;

        public NavVehicleService(Client clientInfo, ILogger<NavVehicleService> logger)
        {
            _clientInfo = clientInfo;
            _logger = logger;

            // Initialize client using helper
            _client = NavClientHelper.InitializeClient<Vehicles.VehiclesBasics>(clientInfo);

            _logger.LogInformation("NavVehicleService initialized for client {ClientCode} at {Host}:{Port}",
                clientInfo.ClientCode, clientInfo.IPAddress, clientInfo.Port);
        }

        public async Task<Vehicles.VehiclesBasics?> ReadVehicleAsync(string vehicleNumber)
        {
            try
            {
                _logger.LogInformation("Reading vehicle {VehicleNumber} from NAV for client {ClientCode}",
                    vehicleNumber, _clientInfo.ClientCode);

                var result = await _client.ReadAsync(vehicleNumber);
                return result?.VehiclesBasics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading vehicle {VehicleNumber} from NAV", vehicleNumber);
                throw;
            }
        }

        public async Task<Vehicles.VehiclesBasics[]> ReadMultipleVehiclesAsync(Vehicles.VehiclesBasics_Filter[]? filters, int pageSize = 100)
        {
            try
            {
                _logger.LogInformation("Reading multiple vehicles from NAV for client {ClientCode}",
                    _clientInfo.ClientCode);

                var result = await _client.ReadMultipleAsync(filters ?? [], string.Empty, pageSize);
                return result?.ReadMultiple_Result1 ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading multiple vehicles from NAV");
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
