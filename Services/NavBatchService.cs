using System.ServiceModel;
using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public interface INavBatchService
    {
        Task<P_Batches.ParcelBatches?> ReadBatchAsync(string batchNo);
        Task<P_Batches.ParcelBatches[]> ReadMultipleBatchesAsync(P_Batches.ParcelBatches_Filter[]? filters, int pageSize = 100);
        Task<P_Batches.ParcelBatches> CreateBatchAsync(P_Batches.ParcelBatches batch);
        Task<P_Batches.ParcelBatches> UpdateBatchAsync(P_Batches.ParcelBatches batch);
        Task<bool> DeleteBatchAsync(string key);
    }

    public class NavBatchService : INavBatchService, IDisposable
    {
        private readonly P_Batches.ParcelBatches_PortClient _client;
        private readonly ILogger<NavBatchService> _logger;
        private readonly Client _clientInfo;
        private bool _disposed;

        public NavBatchService(Client clientInfo, ILogger<NavBatchService> logger)
        {
            _clientInfo = clientInfo;
            _logger = logger;

            // Initialize client using helper
            _client = NavClientHelper.InitializeClient<P_Batches.ParcelBatches>(clientInfo);

            _logger.LogInformation("NavBatchService initialized for client {ClientCode} at {Host}:{Port}",
                clientInfo.ClientCode, clientInfo.IPAddress, clientInfo.Port);
        }

        public async Task<P_Batches.ParcelBatches?> ReadBatchAsync(string batchNo)
        {
            try
            {
                _logger.LogInformation("Reading batch {BatchNo} from NAV for client {ClientCode}",
                    batchNo, _clientInfo.ClientCode);

                var result = await _client.ReadAsync(batchNo);
                return result?.ParcelBatches;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading batch {BatchNo} from NAV", batchNo);
                throw;
            }
        }

        public async Task<P_Batches.ParcelBatches[]> ReadMultipleBatchesAsync(P_Batches.ParcelBatches_Filter[]? filters, int pageSize = 100)
        {
            try
            {
                _logger.LogInformation("Reading multiple batches from NAV for client {ClientCode}",
                    _clientInfo.ClientCode);

                var result = await _client.ReadMultipleAsync(filters ?? [], string.Empty, pageSize);
                return result?.ReadMultiple_Result1 ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading multiple batches from NAV");
                throw;
            }
        }

        public async Task<P_Batches.ParcelBatches> CreateBatchAsync(P_Batches.ParcelBatches batch)
        {
            try
            {
                _logger.LogInformation("Creating batch in NAV for client {ClientCode}", _clientInfo.ClientCode);

                var request = new P_Batches.Create(batch);
                var result = await _client.CreateAsync(request);

                _logger.LogInformation("Batch created with Batch_No: {BatchNo}", result.ParcelBatches?.Batch_No);

                return result.ParcelBatches ?? throw new InvalidOperationException("NAV returned null batch after create");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating batch in NAV");
                throw;
            }
        }

        public async Task<P_Batches.ParcelBatches> UpdateBatchAsync(P_Batches.ParcelBatches batch)
        {
            try
            {
                _logger.LogInformation("Updating batch {BatchNo} in NAV for client {ClientCode}",
                    batch.Batch_No, _clientInfo.ClientCode);

                // Fetch existing batch to obtain the NAV Key.
                var existingBatch = await ReadBatchAsync(batch.Batch_No);
                if (existingBatch == null)
                    throw new InvalidOperationException($"Batch {batch.Batch_No} not found in NAV");

                batch.Key = existingBatch.Key;

                var request = new P_Batches.Update(batch);
                var result = await _client.UpdateAsync(request);

                return result.ParcelBatches ?? throw new InvalidOperationException("NAV returned null batch after update");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating batch {BatchNo} in NAV", batch.Batch_No);
                throw;
            }
        }

        public async Task<bool> DeleteBatchAsync(string key)
        {
            try
            {
                _logger.LogInformation("Deleting batch with key {Key} from NAV for client {ClientCode}",
                    key, _clientInfo.ClientCode);

                var result = await _client.DeleteAsync(key);
                return result.Delete_Result1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting batch with key {Key} from NAV", key);
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
