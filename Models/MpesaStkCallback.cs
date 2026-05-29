using System.Text.Json.Serialization;

namespace ParcelAPI.Models
{
    public class MpesaStkCallback
    {
        [JsonPropertyName("Body")]
        public StkCallbackBody? Body { get; set; }
    }

    public class StkCallbackBody
    {
        [JsonPropertyName("stkCallback")]
        public StkCallback? StkCallback { get; set; }
    }

    public class StkCallback
    {
        [JsonPropertyName("MerchantRequestID")]
        public string? MerchantRequestId { get; set; }

        [JsonPropertyName("CheckoutRequestID")]
        public string? CheckoutRequestId { get; set; }

        [JsonPropertyName("ResultCode")]
        public int ResultCode { get; set; }

        [JsonPropertyName("ResultDesc")]
        public string? ResultDesc { get; set; }

        [JsonPropertyName("CallbackMetadata")]
        public CallbackMetadata? CallbackMetadata { get; set; }
    }

    public class CallbackMetadata
    {
        [JsonPropertyName("Item")]
        public List<CallbackItem>? Item { get; set; }
    }

    public class CallbackItem
    {
        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("Value")]
        public object? Value { get; set; }
    }
}
