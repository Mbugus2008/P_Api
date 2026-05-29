using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public interface INavMpesaConfigService
    {
        Task<MpesaCon.MpesaConfig?> ReadConfigAsync();
    }

    public class NavMpesaConfigService : INavMpesaConfigService, IDisposable
    {
        private readonly dynamic _client;
        private readonly ILogger _logger;
        private readonly Client _clientInfo;
        private bool _disposed;

        public NavMpesaConfigService(Client clientInfo, ILogger logger)
        {
            _clientInfo = clientInfo;
            _logger = logger;
            _client = NavClientHelper.InitializeClient<MpesaCon.MpesaConfig>(clientInfo);
            _logger.LogInformation("NavMpesaConfigService initialized for client {ClientCode}", clientInfo.ClientCode);
        }

        public async Task<MpesaCon.MpesaConfig?> ReadConfigAsync()
        {
            try
            {
                _logger.LogInformation("Reading MpesaConfig from NAV for client {ClientCode}", _clientInfo.ClientCode);

                var result = await _client.ReadAsync("DEFAULT");
                var config = result?.MpesaConfig;

                if (config == null)
                {
                    _logger.LogWarning("No MpesaConfig record found in NAV for client {ClientCode}. Ensure Table 50080 has a record with Primary Key = DEFAULT.", _clientInfo.ClientCode);
                    return null;
                }

                string? merchantName = config.Merchant_Name;
                string env = config.Environment.ToString();
                string trxCode = config.Transaction_Code.ToString();
                _logger.LogInformation("MpesaConfig loaded: Merchant={Merchant}, Env={Environment}, Trx={TransactionCode}",
                    merchantName, env, trxCode);

                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading MpesaConfig from NAV for client {ClientCode}", _clientInfo.ClientCode);
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                (_client as IDisposable)?.Dispose();
                _disposed = true;
            }
        }
    }
}
