using System.ServiceModel;
using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public interface INavParcelLogsService
    {
        Task<ParcelLogs.Parcel_Logs[]> ReadMultipleLogsAsync(ParcelLogs.Parcel_Logs_Filter[]? filters, int pageSize = 100);
        Task<ParcelLogs.Parcel_Logs> CreateLogAsync(ParcelLogs.Parcel_Logs log);
    }

    public class NavParcelLogsService : INavParcelLogsService, IDisposable
    {
        private readonly ParcelLogs.Parcel_Logs_PortClient _client;
        private readonly ILogger<NavParcelLogsService> _logger;
        private readonly Client _clientInfo;
        private bool _disposed;

        public NavParcelLogsService(Client clientInfo, ILogger<NavParcelLogsService> logger)
        {
            _clientInfo = clientInfo;
            _logger = logger;
            
            // Initialize client using helper
            _client = NavClientHelper.InitializeClient<ParcelLogs.Parcel_Logs>(clientInfo);
            
            _logger.LogInformation("NavParcelLogsService initialized for client {ClientCode} at {Host}:{Port}",
                clientInfo.ClientCode, clientInfo.IPAddress, clientInfo.Port);
        }

        public async Task<ParcelLogs.Parcel_Logs[]> ReadMultipleLogsAsync(ParcelLogs.Parcel_Logs_Filter[]? filters, int pageSize = 100)
        {
            try
            {
                _logger.LogInformation("Reading multiple parcel logs from NAV for client {ClientCode}", 
                    _clientInfo.ClientCode);
                
                var result = await _client.ReadMultipleAsync(filters ?? [], string.Empty, pageSize);
                return result?.ReadMultiple_Result1 ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading multiple parcel logs from NAV");
                throw;
            }
        }

        public async Task<ParcelLogs.Parcel_Logs> CreateLogAsync(ParcelLogs.Parcel_Logs log)
        {
            try
            {
                _logger.LogInformation("Creating parcel log in NAV for client {ClientCode}, Document: {DocumentNo}", 
                    _clientInfo.ClientCode, log.Document_No);
                
                var request = new ParcelLogs.Create(log);
                var result = await _client.CreateAsync(request);
                
                _logger.LogInformation("Parcel log created for Document_No: {DocumentNo}", 
                    result.Parcel_Logs?.Document_No);
                
                return result.Parcel_Logs ?? throw new InvalidOperationException("NAV returned null parcel log after create");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating parcel log in NAV");
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
