using Microsoft.EntityFrameworkCore;
using ParcelAPI.Clients;
using ParcelAPI.Data;
using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public class ClientService : IClientService
    {
        private readonly ParcelContext _context;
        private readonly ILogger<ClientService> _logger;
        private readonly IClientFactory _clientFactory;

        public ClientService(ParcelContext context, ILogger<ClientService> logger, IClientFactory clientFactory)
        {
            _context = context;
            _logger = logger;
            _clientFactory = clientFactory;
        }

        public async Task<User.Parcel_Users[]> GetParcelUsersAsync(string clientId)
        {
            var client = await _clientFactory.GetClientAsync(clientId);
            if (client == null)
            {
                return Array.Empty<User.Parcel_Users>();
            }

            // Use client-specific implementation (can be overridden per client)
            return await client.GetParcelUsersAsync();
        }

        public async Task<Client?> GetClientAsync(string clientCode)
        {
            return await _context.Clients
                .FirstOrDefaultAsync(c => c.ClientCode == clientCode && c.Active);
        }

        public async Task<IClient?> GetIClientAsync(string clientCode)
        {
            return await _clientFactory.GetClientAsync(clientCode);
        }
    }
}