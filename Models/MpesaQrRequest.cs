namespace ParcelAPI.Models
{
    public class MpesaQrRequest
    {
        public decimal Amount { get; set; }
        public string? Reference { get; set; }
        public string? Size { get; set; } = "300";
    }
}
