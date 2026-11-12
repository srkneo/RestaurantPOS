namespace POSSystem.Models
{
    public abstract class TenantEntity
    {
        public int TenantId { get; set; } = 1;
        public int OutletId { get; set; } = 1;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
