using System.ServiceModel;
using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public interface INavParcelService
    {
        Task<Parcels.Parcel?> ReadParcelAsync(string documentNo);
        Task<Parcels.Parcel[]> ReadMultipleParcelsAsync(Parcels.Parcel_Filter[]? filters, int pageSize = 100);
        Task<Parcels.Parcel> CreateParcelAsync(Parcels.Parcel parcel);
        Task<Parcels.Parcel> UpdateParcelAsync(Parcels.Parcel parcel);
        Task<bool> DeleteParcelAsync(string key);
    }

    public class NavParcelService : INavParcelService, IDisposable
    {
        private readonly Parcels.Parcel_PortClient _client;
        private readonly ILogger<NavParcelService> _logger;
        private readonly Client _clientInfo;
        private bool _disposed;

        public NavParcelService(Client clientInfo, ILogger<NavParcelService> logger)
        {
            _clientInfo = clientInfo;
            _logger = logger;
            
            // Initialize client using helper
            _client = NavClientHelper.InitializeClient<Parcels.Parcel>(clientInfo);
            
            _logger.LogInformation("NavParcelService initialized for client {ClientCode} at {Host}:{Port}",
                clientInfo.ClientCode, clientInfo.IPAddress, clientInfo.Port);
        }

        public async Task<Parcels.Parcel?> ReadParcelAsync(string documentNo)
        {
            try
            {
                _logger.LogInformation("Reading parcel {DocumentNo} from NAV for client {ClientCode}", 
                    documentNo, _clientInfo.ClientCode);
                
                var result = await _client.ReadAsync(documentNo);
                return result?.Parcel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading parcel {DocumentNo} from NAV", documentNo);
                throw;
            }
        }

        public async Task<Parcels.Parcel[]> ReadMultipleParcelsAsync(Parcels.Parcel_Filter[]? filters, int pageSize = 100)
        {
            try
            {
                _logger.LogInformation("Reading multiple parcels from NAV for client {ClientCode}", 
                    _clientInfo.ClientCode);
                
                var result = await _client.ReadMultipleAsync(filters ?? [], string.Empty, pageSize);
                return result?.ReadMultiple_Result1 ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading multiple parcels from NAV");
                throw;
            }
        }

        public async Task<Parcels.Parcel> CreateParcelAsync(Parcels.Parcel parcel)
        {
            try
            {
                _logger.LogInformation("Creating parcel in NAV for client {ClientCode}", 
                    _clientInfo.ClientCode);

                NormalizeParcelForNav(parcel);
                
                var request = new Parcels.Create(parcel);
                var result = await _client.CreateAsync(request);
                
                _logger.LogInformation("Parcel created with Document_No: {DocumentNo}", 
                    result.Parcel?.Document_No);
                
                return result.Parcel ?? throw new InvalidOperationException("NAV returned null parcel after create");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating parcel in NAV");
                throw;
            }
        }

        public async Task<Parcels.Parcel> UpdateParcelAsync(Parcels.Parcel parcel)
        {
            try
            {
                _logger.LogInformation("Updating parcel {DocumentNo} in NAV for client {ClientCode}", 
                    parcel.Document_No, _clientInfo.ClientCode);
                
                // First fetch the existing parcel from NAV to get the Key
                var existingParcel = await ReadParcelAsync(parcel.Document_No);
                if (existingParcel == null)
                    throw new InvalidOperationException($"Parcel {parcel.Document_No} not found in NAV");

                // Copy the Key from the existing parcel
                parcel.Key = existingParcel.Key;

                NormalizeParcelForNav(parcel);
                
                var request = new Parcels.Update(parcel);
                var result = await _client.UpdateAsync(request);
                
                return result.Parcel ?? throw new InvalidOperationException("NAV returned null parcel after update");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating parcel {DocumentNo} in NAV", parcel.Document_No);
                throw;
            }
        }

        public async Task<bool> DeleteParcelAsync(string key)
        {
            try
            {
                _logger.LogInformation("Deleting parcel with key {Key} from NAV for client {ClientCode}", 
                    key, _clientInfo.ClientCode);
                
                var result = await _client.DeleteAsync(key);
                return result.Delete_Result1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting parcel with key {Key} from NAV", key);
                throw;
            }
        }
        private static void NormalizeParcelForNav(Parcels.Parcel parcel)
        {
            // Payment_Method is now an enum in generated NAV proxy.
            // Ensure it is explicitly sent during create/update.
            parcel.Payment_MethodSpecified = true;
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
