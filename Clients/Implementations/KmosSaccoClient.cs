using ParcelAPI.Models;

namespace ParcelAPI.Clients.Implementations
{
    public class KmosSaccoClient : BaseClient
    {
        public KmosSaccoClient(Client client) : base(client) { }

        public override string GetApiEndpoint()
        {
            return "https://api.kmos-sacco.co.ke/v1";
        }

        public override Dictionary<string, string> GetConfiguration()
        {
            var config = base.GetConfiguration();
            config["Region"] = "Kenya";
            config["ServiceType"] = "SACCO";
            return config;
        }
    }
}
