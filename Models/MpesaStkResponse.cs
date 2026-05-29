using System.Text.Json.Serialization;

namespace ParcelAPI.Models
{
    public class MpesaStkResponse
    {
        [JsonPropertyName("MerchantRequestID")]
        public string? MerchantRequestId { get; set; }

        [JsonPropertyName("CheckoutRequestID")]
        public string? CheckoutRequestId { get; set; }

        [JsonPropertyName("ResponseCode")]
        public string? ResponseCode { get; set; }

        [JsonPropertyName("ResponseDescription")]
        public string? ResponseDescription { get; set; }

        [JsonPropertyName("CustomerMessage")]
        public string? CustomerMessage { get; set; }
    }
}
