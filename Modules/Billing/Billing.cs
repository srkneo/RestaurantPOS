using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace POSSystem.Models
{
    // Regular: GST invoice with CGST and SGST. Composition: bill of supply, no tax shown. None: not registered for GST.
    public enum TaxMode
    {
        None,
        Regular,
        Composition
    }

    public enum BillStatus
    {
        Unpaid,
        Paid,
        Void
    }

    // A bill (invoice) for one order. Never deleted: a void bill keeps its number and shows as void.
    // Names, prices, tax rates and store details are copied when the bill is made, so later menu or store changes do not alter it.
    public class Bill : TenantEntity
    {
        public int Id { get; set; }
        public int KitchenOrderId { get; set; }

        [MaxLength(16)] public string InvoiceNumber { get; set; } = string.Empty;
        public int FinancialYear { get; set; }      // year the financial year starts in (April)
        public int Sequence { get; set; }

        public DateOnly BusinessDate { get; set; }
        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

        public TaxMode TaxMode { get; set; }
        public bool PricesIncludeTax { get; set; }

        public decimal ItemsTotal { get; set; }
        public decimal Discount { get; set; }
        public decimal TaxableValue { get; set; }
        public decimal Cgst { get; set; }
        public decimal Sgst { get; set; }
        public decimal RoundOff { get; set; }
        public decimal Total { get; set; }

        public BillStatus Status { get; set; } = BillStatus.Unpaid;
        [MaxLength(200)] public string? VoidReason { get; set; }
        public DateTime? VoidedAt { get; set; }

        [MaxLength(60)] public string OrderLabel { get; set; } = string.Empty;
        public int TokenNumber { get; set; }
        [MaxLength(100)] public string? CustomerName { get; set; }
        [MaxLength(20)] public string? CustomerPhone { get; set; }

        [MaxLength(200)] public string RestaurantName { get; set; } = string.Empty;
        [MaxLength(200)] public string OutletName { get; set; } = string.Empty;
        [MaxLength(300)] public string? Address { get; set; }
        [MaxLength(20)] public string? Phone { get; set; }
        [MaxLength(15)] public string? Gstin { get; set; }
        [MaxLength(14)] public string? Fssai { get; set; }
        [MaxLength(300)] public string? Footer { get; set; }

        public int PrintCount { get; set; }
        public int Version { get; set; } = 1;

        public List<BillLine> Lines { get; set; } = new();
    }

    public class BillLine
    {
        public int Id { get; set; }
        public int TenantId { get; set; } = 1;
        public int BillId { get; set; }
        [MaxLength(100)] public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TaxRate { get; set; }
        public decimal Amount { get; set; }        // quantity x unit price, as on the menu
        public decimal Discount { get; set; }      // this line's share of the bill discount
        public decimal TaxableValue { get; set; }
        public decimal Cgst { get; set; }
        public decimal Sgst { get; set; }
    }

    // Last invoice number per outlet per financial year. Raised inside the same database transaction as the bill,
    // so numbers have no gaps and two tills never get the same number.
    public class BillCounter
    {
        public int TenantId { get; set; }
        public int OutletId { get; set; }
        public int FinancialYear { get; set; }
        public int LastNumber { get; set; }
    }
}
