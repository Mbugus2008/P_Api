using System.Text.Json.Serialization;

namespace ParcelAPI.Models
{
    public class DarajaTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public string? ExpiresIn { get; set; }
    }
}
