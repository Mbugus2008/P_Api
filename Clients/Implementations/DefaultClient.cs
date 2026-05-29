using ParcelAPI.Models;

namespace ParcelAPI.Clients.Implementations
{
    public class DefaultClient : BaseClient
    {
        public DefaultClient(Client client) : base(client) { }

        public override string GetApiEndpoint()
        {
            return $"https://api.default.com/{ClientCode}";
        }
    }
}
