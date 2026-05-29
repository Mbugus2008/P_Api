using ParcelAPI.Models;

namespace ParcelAPI.Clients.Implementations
{
    public class CityHoppaClient : BaseClient
    {
        public CityHoppaClient(Client client) : base(client) { }

        public override string GetApiEndpoint()
        {
            return "https://api.cityhopper.co.ke/v1";
        }

        public override Dictionary<string, string> GetConfiguration()
        {
            var config = base.GetConfiguration();
            config["Region"] = "Kenya";
            config["ServiceType"] = "CityHopper";
            return config;
        }
    }
}
