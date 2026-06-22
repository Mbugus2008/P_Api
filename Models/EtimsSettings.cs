namespace ParcelAPI.Models
{
    public class EtimsSettings
    {
        public int Id { get; set; }
        public string ClientCode { get; set; } = string.Empty;
        public string TinPin { get; set; } = string.Empty;
        public string BranchId { get; set; } = "00";
        public string DeviceSerialNo { get; set; } = string.Empty;
        public string ApiUsername { get; set; } = string.Empty;
        public string ApiPassword { get; set; } = string.Empty;
        public string? CmcKey { get; set; } // Returned by KRA after init
        public int? LastInvoiceNo { get; set; } // Sequential counter per branch
        public string Environment { get; set; } = "Sandbox"; // Sandbox | Production
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
