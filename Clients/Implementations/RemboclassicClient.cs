using ParcelAPI.Models;

namespace ParcelAPI.Clients.Implementations
{
    public class RemboclassicClient : BaseClient
    {
        public RemboclassicClient(Client client) : base(client) { }

        public override string GetApiEndpoint()
        {
            return "https://api.remboclassic.co.ke/v1";
        }

        public override Dictionary<string, string> GetConfiguration()
        {
            var config = base.GetConfiguration();
            config["Region"] = "Kenya";
            config["ServiceType"] = "Classic";
            return config;
        }
        
        /// <summary>
        /// Get parcel users from NAV with Rembo-specific filtering.
        /// Filters users by Account_type = Parcel (2)
        /// </summary>
        public override async Task<User.Parcel_Users[]> GetParcelUsersAsync()
        {
            if (NavUserService == null)
            {
                return Array.Empty<User.Parcel_Users>();
            }
            
            // Rembo-specific: filter by Account_type = 2 (Parcel users only)
            var filters = new User.Parcel_Users_Filter[]
            {
               
            };
             
            return await NavUserService.ReadMultipleUsersAsync(filters, 1000);
        }
    }
}
