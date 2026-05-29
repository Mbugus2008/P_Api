namespace ParcelAPI.Models
{
    public class MpesaStkStatus
    {
        public int Id { get; set; }
        public string CheckoutRequestId { get; set; } = string.Empty;
        public string? MerchantRequestId { get; set; }
        public int ResultCode { get; set; }
        public string? ResultDescription { get; set; }
        public decimal? Amount { get; set; }
        public string? MpesaReceiptNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime? TransactionDate { get; set; }
        public string? Reference { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
