using System;
using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    // Cancels a bill of an earlier day (bills of today are voided instead). Has its own number series (CN/26-27/000001)
    // and is reported in the period it is issued. One credit note per bill, for the whole bill.
    public class CreditNote : TenantEntity
    {
        public int Id { get; set; }
        public int BillId { get; set; }

        [MaxLength(16)] public string Number { get; set; } = string.Empty;
        public int FinancialYear { get; set; }
        public int Sequence { get; set; }
        public DateOnly BusinessDate { get; set; }
        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(200)] public string Reason { get; set; } = string.Empty;
        public decimal TaxableValue { get; set; }
        public decimal Cgst { get; set; }
        public decimal Sgst { get; set; }
        public decimal RoundOff { get; set; }
        public decimal Total { get; set; }

        public PaymentMethod? RefundMethod { get; set; }   // how the money paid on the bill was given back; null when nothing was paid
        public decimal RefundAmount { get; set; }
        [MaxLength(256)] public string? ApprovedBy { get; set; }
    }

    // Last credit note number per outlet per financial year, raised in the same transaction as the credit note.
    public class CreditNoteCounter
    {
        public int TenantId { get; set; }
        public int OutletId { get; set; }
        public int FinancialYear { get; set; }
        public int LastNumber { get; set; }
    }
}
