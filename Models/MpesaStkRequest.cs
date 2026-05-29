namespace ParcelAPI.Models
{
    public class MpesaStkRequest
    {
        public decimal Amount { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public string? Description { get; set; }
    }
}
