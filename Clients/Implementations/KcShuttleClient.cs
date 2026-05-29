using ParcelAPI.Models;

namespace ParcelAPI.Clients.Implementations
{
    public class KcShuttleClient : BaseClient
    {
        public KcShuttleClient(Client client) : base(client) { }

        public override string GetApiEndpoint()
        {
            return "https://api.kc-shuttle.co.ke/v1";
        }

        public override Dictionary<string, string> GetConfiguration()
        {
            var config = base.GetConfiguration();
            config["Region"] = "Kenya";
            config["ServiceType"] = "Shuttle";
            return config;
        }
    }
}
