using ParcelAPI.Clients.Implementations;
using ParcelAPI.Data;
using ParcelAPI.Models;
using ParcelAPI.Services;

namespace ParcelAPI.Clients
{
    public interface IClientFactory
    {
        Task<IClient?> GetClientAsync(string clientCode);
    }

    public class ClientFactory : IClientFactory
    {
        private readonly ParcelContext _context;
        private readonly ILogger<ClientFactory> _logger;
        private readonly ILoggerFactory _loggerFactory;

        public ClientFactory(ParcelContext context, ILogger<ClientFactory> logger, ILoggerFactory loggerFactory)
        {
            _context = context;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task<IClient?> GetClientAsync(string clientCode)
        {
            IClient? client = null;

            Client? clientEntity = _context.Clients
                .Where(o => o.ClientCode == clientCode)
                .FirstOrDefault();

            if (clientEntity == null)
            {
                _logger.LogWarning("Client not found: {ClientCode}", clientCode);
                return null;
            }

            client = clientCode.ToUpperInvariant() switch
            {
                "LOPHA_SACCO" => new LophaSaccoClient(clientEntity),
                "KC-SHUTTLE" => new KcShuttleClient(clientEntity),
                "KMOS_SACCO" => new KmosSaccoClient(clientEntity),
                "REMBOCLASIC" => new RemboclassicClient(clientEntity),
                "CITYHOPPER" => new CityHoppaClient(clientEntity),
                _ => new DefaultClient(clientEntity)
            };

            if (client != null)
            {
                // Initialize services for the client
                InitializeClientServices(client, clientEntity);
                _logger.LogInformation("Client initialized: {ClientCode} ({ClientType})", 
                    clientCode, client.GetType().Name);
            }

            return client;
        }

        private void InitializeClientServices(IClient client, Client clientEntity)
        {
            // Initialize the various services for the client
            client.UserService = InitializeService<IUserService>(clientEntity);
            client.TransactionService = InitializeService<ITransactionService>(clientEntity);
            client.ParcelService = InitializeService<IParcelService>(clientEntity);
            client.LocationService = InitializeService<ILocationService>(clientEntity);
            
            // Initialize NAV Web Services for Dynamics NAV integration
            client.NavParcelService = new NavParcelService(
                clientEntity, 
                _loggerFactory.CreateLogger<NavParcelService>());
            
            client.NavUserService = new NavUserService(
                clientEntity, 
                _loggerFactory.CreateLogger<NavUserService>());
            
            client.NavLocationService = new NavLocationService(
                clientEntity, 
                _loggerFactory.CreateLogger<NavLocationService>());
            
            client.NavParcelLogsService = new NavParcelLogsService(
                clientEntity, 
                _loggerFactory.CreateLogger<NavParcelLogsService>());

            client.NavBatchService = new NavBatchService(
                clientEntity,
                _loggerFactory.CreateLogger<NavBatchService>());

            client.NavVehicleService = new NavVehicleService(
                clientEntity,
                _loggerFactory.CreateLogger<NavVehicleService>());
            
            _logger.LogDebug("NAV Services initialized for client {ClientCode}", clientEntity.ClientCode);
        }

        private T? InitializeService<T>(Client client) where T : class
        {
            // Placeholder for service initialization logic
            _logger.LogDebug("Initializing service {ServiceType} for client {ClientCode}", 
                typeof(T).Name, client.ClientCode);
            return null;
        }
    }
}
