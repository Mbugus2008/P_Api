using ParcelAPI.Models;
using ParcelAPI.Services;

namespace ParcelAPI.Clients
{
    public abstract class BaseClient : IClient
    {
        public Client ClientInfo { get; }
        public string ClientCode => ClientInfo.ClientCode;
        
        // Service references
        public IUserService? UserService { get; set; }
        public ITransactionService? TransactionService { get; set; }
        public IParcelService? ParcelService { get; set; }
        public ILocationService? LocationService { get; set; }
        
        // NAV Web Services
        public INavParcelService? NavParcelService { get; set; }
        public INavUserService? NavUserService { get; set; }
        public INavLocationService? NavLocationService { get; set; }
        public INavParcelLogsService? NavParcelLogsService { get; set; }
        public INavBatchService? NavBatchService { get; set; }
        public INavVehicleService? NavVehicleService { get; set; }

        protected BaseClient(Client client)
        {
            ClientInfo = client ?? throw new ArgumentNullException(nameof(client));
        }

        public abstract string GetApiEndpoint();
        
        public virtual Dictionary<string, string> GetConfiguration()
        {
            return new Dictionary<string, string>
            {
                { "ClientCode", ClientCode },
                { "ClientName", ClientInfo.ClientName ?? string.Empty },
                { "ApiEndpoint", GetApiEndpoint() }
            };
        }
        
        /// <summary>
        /// Get parcel users from NAV. Override in client implementations for custom behavior.
        /// Default filters by Account_type = 2 (Parcel users)
        /// </summary>
        public virtual async Task<User.Parcel_Users[]> GetParcelUsersAsync()
        {
            if (NavUserService == null)
            {
                return Array.Empty<User.Parcel_Users>();
            }
            
            // Default: filter by Account_type = 2 (Parcel users)
            var filters = new User.Parcel_Users_Filter[]
            {
                new User.Parcel_Users_Filter
                {
                    Field = User.Parcel_Users_Fields.Account_type,
                    Criteria = "2"
                }
            };
            
            return await NavUserService.ReadMultipleUsersAsync(filters, 1000);
        }
    }
}
