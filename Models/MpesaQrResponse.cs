using System.Text.Json.Serialization;

namespace ParcelAPI.Models
{
    public class MpesaQrResponse
    {
        public string? ResponseCode { get; set; }
        public string? ResponseDescription { get; set; }

        [JsonPropertyName("QRCode")]
        public string? QrCodeBase64 { get; set; }
    }
}
