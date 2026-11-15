using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    public enum CashMovementKind
    {
        In,
        Out
    }

    // One till session: opened with the cash in the drawer, closed with the cash counted.
    // At most one open shift per outlet. Expected cash and cash sales are kept when it closes, so the record never changes later.
    public class CashShift : TenantEntity
    {
        public int Id { get; set; }
        public DateOnly BusinessDate { get; set; }
        public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
        public string? OpenedBy { get; set; }
        public decimal OpeningCash { get; set; }

        public DateTime? ClosedAt { get; set; }
        public string? ClosedBy { get; set; }
        public decimal? CashSales { get; set; }
        public decimal? ExpectedCash { get; set; }
        public decimal? CountedCash { get; set; }
        [MaxLength(200)] public string? CloseNote { get; set; }

        public int Version { get; set; } = 1;
        public List<CashMovement> Movements { get; set; } = new();
    }

    // Cash put into the drawer (In, e.g. more change) or taken out (Out, e.g. paid a vendor), with a reason.
    public class CashMovement
    {
        public int Id { get; set; }
        public int TenantId { get; set; } = 1;
        public int CashShiftId { get; set; }
        public CashMovementKind Kind { get; set; }
        public decimal Amount { get; set; }        // always above 0; Kind says the direction
        [MaxLength(100)] public string Reason { get; set; } = string.Empty;
        public DateTime At { get; set; } = DateTime.UtcNow;
        public string? By { get; set; }
    }
}
