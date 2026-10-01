namespace ParcelAPI.Models
{
    /// <summary>A sales partner or referrer who brings in parcel clients.</summary>
    public class Marketer
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }

        /// <summary>"Partner" (follow-up role, per-parcel commission) or "Referral" (one-off fee).</summary>
        public string Type { get; set; } = "Partner";

        /// <summary>KSh paid per parcel created for attributed clients (Partner only).</summary>
        public decimal PerParcelRate { get; set; } = 5m;

        /// <summary>One-off fee for a successful referral (Referral only).</summary>
        public decimal ReferralFee { get; set; } = 5000m;

        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Links a marketer to a client (tenant). Drives commission calculation.</summary>
    public class MarketerClient
    {
        public int Id { get; set; }
        public string MarketerCode { get; set; } = string.Empty;
        public string ClientCode { get; set; } = string.Empty;
        public string Type { get; set; } = "Partner";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>Commission ledger entries (pending until paid out to the marketer).</summary>
    public class MarketerPayout
    {
        public int Id { get; set; }
        public string MarketerCode { get; set; } = string.Empty;
        public string? ClientCode { get; set; }

        /// <summary>Month the commission belongs to (yyyy-MM) or "REFERRAL".</summary>
        public string Period { get; set; } = string.Empty;

        /// <summary>"ParcelCommission" (5 x parcels) or "Referral" (one-off 5,000).</summary>
        public string Kind { get; set; } = "ParcelCommission";

        public int ParcelCount { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime? PaidAt { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
