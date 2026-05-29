namespace ParcelAPI.Models
{
    public class MpesaSettings
    {
        public string ConsumerKey { get; set; } = string.Empty;
        public string ConsumerSecret { get; set; } = string.Empty;
        public string ShortCode { get; set; } = string.Empty;
        public string MerchantName { get; set; } = string.Empty;
        public string TransactionCode { get; set; } = "BG";
        public string Environment { get; set; } = "Sandbox";
        public string SandboxUrl { get; set; } = "https://sandbox.safaricom.co.ke";
        public string ProductionUrl { get; set; } = "https://api.safaricom.co.ke";
    }
}
