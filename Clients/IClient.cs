using ParcelAPI.Models;
using ParcelAPI.Services;

namespace ParcelAPI.Clients
{
    public interface IClient
    {
        Client ClientInfo { get; }
        string ClientCode { get; }
        
        // Service references
        IUserService? UserService { get; set; }
        ITransactionService? TransactionService { get; set; }
        IParcelService? ParcelService { get; set; }
        ILocationService? LocationService { get; set; }
        
        // NAV Web Services
        INavParcelService? NavParcelService { get; set; }
        INavUserService? NavUserService { get; set; }
        INavLocationService? NavLocationService { get; set; }
        INavParcelLogsService? NavParcelLogsService { get; set; }
        INavBatchService? NavBatchService { get; set; }
        INavVehicleService? NavVehicleService { get; set; }
        
        // Client-specific configuration
        string GetApiEndpoint();
        Dictionary<string, string> GetConfiguration();
        
        // Data retrieval methods
        Task<User.Parcel_Users[]> GetParcelUsersAsync();
    }

    // Service interfaces for the different services
    public interface IUserService { }
    public interface ITransactionService { }
    public interface IParcelService { }
    public interface ILocationService { }
}
