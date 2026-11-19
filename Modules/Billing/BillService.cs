using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;

namespace POSSystem.Billing
{
    public record BillLineDto(string ItemName, int Quantity, decimal UnitPrice, decimal TaxRate, decimal Amount, decimal Discount,
        decimal TaxableValue, decimal Cgst, decimal Sgst);
    public record PaymentDto(int Id, string Method, decimal Amount, decimal? Tendered, decimal? Change, string? Reference, DateTime ReceivedAt);
    public record StoreOnBill(string RestaurantName, string OutletName, string? Address, string? Phone, string? Gstin, string? Fssai, string? Footer);
    public record BillDto(int Id, int OrderId, string InvoiceNumber, string Title, string? TaxNote, DateTime IssuedAt, DateOnly BusinessDate,
        string TaxMode, bool PricesIncludeTax, string Status, string OrderLabel, int TokenNumber, string? CustomerName, string? CustomerPhone,
        decimal ItemsTotal, decimal Discount, decimal TaxableValue, decimal Cgst, decimal Sgst, decimal RoundOff, decimal Total,
        string? VoidReason, DateTime? VoidedAt, int PrintCount, int Version, StoreOnBill Store,
        IReadOnlyList<BillLineDto> Lines, IReadOnlyList<TaxSummaryRow> TaxSummary,
        decimal AmountPaid, decimal AmountDue, DateTime? PaidAt, IReadOnlyList<PaymentDto> Payments,
        DateTime? CreditedAt = null, string? VoidApprovedBy = null);
    public record BillSummaryDto(int Id, int OrderId, string InvoiceNumber, DateTime IssuedAt, string OrderLabel, int TokenNumber,
        decimal Total, string Status, int Version);

    public record CreateBillRequest(decimal? DiscountPercent, decimal? DiscountAmount, string? ApproverPin = null);
    public record VoidBillRequest(string? Reason, int Version, string? ApproverPin = null);
    public record BillingSettingsDto(string TaxMode, bool PricesIncludeTax, string BillPrefix, string? UpiId = null, string? UpiName = null,
        int PaperWidth = 80, string PrintLanguage = "en");
    public record PrintSettingsDto(int PaperWidth, string PrintLanguage, bool UpiReady, bool ReviewReady = false, bool FeedbackReady = false, bool ReprintNeedsPin = true);
    public record AddPaymentRequest(string? Method, decimal Amount, decimal? Tendered, string? Reference, int Version);
    public record UpiLinkDto(string Link, decimal Amount, string UpiId);

    public class BillExistsException : Exception
    {
        public int BillId { get; }
        public BillExistsException(int billId) : base("This order already has a bill.") => BillId = billId;
    }

    // Thrown by the order service when a billed order is changed.
    public class BilledOrderException : OrderStateException
    {
        public BilledOrderException() : base("This order has a bill. Void the bill first to change the order.") { }
    }

    public class BillService
    {
        public const string CompositionNote = "Composition taxable person, not eligible to collect tax on supplies";
        private readonly AppDbContext _db;

        public BillService(AppDbContext db)
        {
            _db = db;
        }

        public static string TitleFor(TaxMode mode) => mode switch
        {
            TaxMode.Regular => "Tax Invoice",
            TaxMode.Composition => "Bill of Supply",
            _ => "Bill"
        };

        // checkDiscount gets the discount as a percent of the items before the bill is made, and can refuse it.
        public async Task<Bill> CreateBill(int orderId, CreateBillRequest request, Func<decimal, Task>? checkDiscount = null)
        {
            var order = await _db.KitchenOrders.Include(o => o.Items).Include(o => o.Table).FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new KeyNotFoundException("Order not found.");
            if (order.Status == OrderStatus.Cancelled) throw new ArgumentException("A cancelled order cannot be billed.");
            if (order.Items.Count == 0) throw new ArgumentException("The order has no items.");
            var existing = await _db.Bills.Where(b => b.KitchenOrderId == orderId && b.Status != BillStatus.Void).Select(b => (int?)b.Id).FirstOrDefaultAsync();
            if (existing.HasValue) throw new BillExistsException(existing.Value);

            var outlet = await _db.Outlets.FirstAsync(o => o.Id == _db.CurrentOutletId && o.TenantId == _db.CurrentTenantId);
            var tenant = await _db.Tenants.FirstAsync(t => t.Id == _db.CurrentTenantId);
            var settings = outlet.GetSettings();
            if (settings.TaxMode != TaxMode.None && string.IsNullOrWhiteSpace(outlet.Gstin))
                throw new ArgumentException("Add your GSTIN in Store first, or set the tax mode to No GST.");

            var menuIds = order.Items.Select(i => i.MenuItemId).Distinct().ToList();
            var rates = await _db.MenuItems.Where(m => menuIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.TaxRate);
            var inputs = order.Items.OrderBy(i => i.Id)
                .Select(i => new BillLineInput(POSSystem.Menu.MenuOptions.BillName(i), i.Quantity, i.UnitPrice,
                    settings.TaxMode == TaxMode.Regular ? rates.GetValueOrDefault(i.MenuItemId, 5m) : 0m))
                .ToList();
            var itemsTotal = inputs.Sum(i => BillCalculator.Paise(i.Quantity * i.UnitPrice));
            var discount = DiscountOf(request, itemsTotal);
            if (checkDiscount != null && discount > 0 && itemsTotal > 0) await checkDiscount(discount * 100m / itemsTotal);
            var totals = BillCalculator.Calculate(inputs, settings.TaxMode, settings.PricesIncludeTax, discount);

            var now = DateTime.UtcNow;
            var businessDate = BusinessClock.BusinessDate(now, outlet.TimeZone, outlet.BusinessDayStartHour);
            var financialYear = BillNumbers.FinancialYearOf(businessDate);

            var bill = new Bill
            {
                KitchenOrderId = order.Id,
                FinancialYear = financialYear,
                BusinessDate = businessDate,
                IssuedAt = now,
                TaxMode = settings.TaxMode,
                PricesIncludeTax = settings.PricesIncludeTax,
                ItemsTotal = totals.ItemsTotal,
                Discount = totals.Discount,
                TaxableValue = totals.TaxableValue,
                Cgst = totals.Cgst,
                Sgst = totals.Sgst,
                RoundOff = totals.RoundOff,
                Total = totals.Total,
                OrderLabel = order.OrderType == OrderType.DineIn && order.Table != null ? $"Table {order.Table.TableNumber}" : "Takeaway",
                TokenNumber = order.TokenNumber,
                CustomerName = order.CustomerName,
                CustomerPhone = order.CustomerPhone,
                RestaurantName = tenant.Name,
                OutletName = outlet.Name,
                Address = outlet.Address,
                Phone = outlet.Phone,
                Gstin = outlet.Gstin,
                Fssai = outlet.Fssai,
                Footer = outlet.ReceiptFooter,
                Lines = totals.Lines.Select(l => new BillLine
                {
                    TenantId = _db.CurrentTenantId,
                    ItemName = l.Name,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    TaxRate = l.TaxRate,
                    Amount = l.Amount,
                    Discount = l.Discount,
                    TaxableValue = l.TaxableValue,
                    Cgst = l.Cgst,
                    Sgst = l.Sgst
                }).ToList()
            };

            // Number and bill are saved together: if saving fails, the number is not used up.
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
            bill.Sequence = await NextNumber(financialYear);
            bill.InvoiceNumber = BillNumbers.Format(settings.BillPrefix, financialYear, bill.Sequence);
            _db.Bills.Add(bill);
            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
            return bill;
        }

        public async Task<Bill> VoidBill(int id, VoidBillRequest request, string? approvedBy = null)
        {
            var bill = await Load(id) ?? throw new KeyNotFoundException("Bill not found.");
            if (bill.Version != request.Version) throw new OrderConflictException("This bill was changed on another screen.");
            if (bill.Status == BillStatus.Void) throw new ArgumentException("This bill is already void.");
            if (bill.CreditedAt != null) throw new ArgumentException("This bill has a credit note and cannot be voided.");
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) throw new ArgumentException("Give a reason for voiding the bill.");
            if (reason.Length > 200) throw new ArgumentException("The reason can be at most 200 characters.");

            bill.Status = BillStatus.Void;
            bill.VoidReason = reason;
            bill.VoidApprovedBy = approvedBy;
            bill.VoidedAt = DateTime.UtcNow;
            bill.Version++;
            await _db.SaveChangesAsync();
            return bill;
        }

        public Task<Bill?> GetBill(int id) => Load(id);

        public Task<Bill?> GetActiveBillForOrder(int orderId) =>
            _db.Bills.Include(b => b.Lines).Include(b => b.Payments).FirstOrDefaultAsync(b => b.KitchenOrderId == orderId && b.Status != BillStatus.Void);

        private Task<Bill?> Load(int id) => _db.Bills.Include(b => b.Lines).Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id);

        public static decimal PaidOf(Bill b) => b.Payments.Sum(p => p.Amount);
        public static decimal DueOf(Bill b) => b.Status == BillStatus.Void ? 0m : Math.Max(b.Total - PaidOf(b), 0m);

        // Records money received. Cash may be more than due (change is given back); UPI and card must be at most what is due.
        public async Task<Bill> AddPayment(int billId, AddPaymentRequest request)
        {
            var bill = await Load(billId) ?? throw new KeyNotFoundException("Bill not found.");
            if (bill.Version != request.Version) throw new OrderConflictException("This bill was changed on another screen.");
            if (bill.Status == BillStatus.Void) throw new ArgumentException("This bill is void.");
            if (bill.CreditedAt != null) throw new ArgumentException("This bill has a credit note.");
            if (bill.Status == BillStatus.Paid) throw new ArgumentException("This bill is already paid.");
            if (!Enum.TryParse<PaymentMethod>(request.Method, true, out var method) || int.TryParse(request.Method, out _))
                throw new ArgumentException("Choose Cash, UPI or Card.");

            var due = DueOf(bill);
            var amount = BillCalculator.Paise(request.Amount);
            if (amount <= 0) throw new ArgumentException("Enter an amount above 0.");
            if (amount > due) throw new ArgumentException($"Only {due.ToString("0.00", CultureInfo.InvariantCulture)} is due on this bill.");
            var reference = request.Reference?.Trim();
            if (reference is { Length: > 40 }) throw new ArgumentException("The reference can be at most 40 characters.");

            decimal? tendered = null, change = null;
            if (method == PaymentMethod.Cash && request.Tendered.HasValue)
            {
                tendered = BillCalculator.Paise(request.Tendered.Value);
                if (tendered < amount) throw new ArgumentException("The cash received is less than the amount.");
                change = tendered - amount;
            }

            bill.Payments.Add(new BillPayment
            {
                TenantId = _db.CurrentTenantId,
                Method = method,
                Amount = amount,
                Tendered = tendered,
                Change = change,
                Reference = string.IsNullOrEmpty(reference) ? null : reference,
                ReceivedAt = DateTime.UtcNow,
                ReceivedBy = _db.CurrentUserId
            });
            if (PaidOf(bill) >= bill.Total)
            {
                bill.Status = BillStatus.Paid;
                bill.PaidAt = DateTime.UtcNow;
            }
            bill.Version++;
            await _db.SaveChangesAsync();
            return bill;
        }

        // upi://pay link for the amount still due; any UPI app reads it from the QR code.
        public async Task<UpiLinkDto> UpiLink(int billId)
        {
            var bill = await Load(billId) ?? throw new KeyNotFoundException("Bill not found.");
            var outlet = await _db.Outlets.FirstAsync(o => o.Id == _db.CurrentOutletId && o.TenantId == _db.CurrentTenantId);
            var settings = outlet.GetSettings();
            if (string.IsNullOrWhiteSpace(settings.UpiId)) throw new ArgumentException("Add your UPI ID in billing settings first.");
            var due = DueOf(bill);
            if (due <= 0) throw new ArgumentException("Nothing is due on this bill.");
            var name = string.IsNullOrWhiteSpace(settings.UpiName) ? bill.RestaurantName : settings.UpiName;
            var link = BuildUpiLink(settings.UpiId, name, due, bill.InvoiceNumber);
            return new UpiLinkDto(link, due, settings.UpiId);
        }

        public static string BuildUpiLink(string upiId, string payeeName, decimal amount, string note) =>
            "upi://pay?pa=" + upiId +
            "&pn=" + Uri.EscapeDataString(payeeName) +
            "&am=" + amount.ToString("0.00", CultureInfo.InvariantCulture) +
            "&cu=INR" +
            "&tn=" + Uri.EscapeDataString(note);

        public async Task<List<Bill>> GetBills(DateOnly? businessDate)
        {
            var date = businessDate ?? await Today();
            return await _db.Bills.Where(b => b.OutletId == _db.CurrentOutletId && b.BusinessDate == date)
                .OrderByDescending(b => b.Sequence).ToListAsync();
        }

        public async Task<DateOnly> Today()
        {
            var outlet = await _db.Outlets.FirstAsync(o => o.Id == _db.CurrentOutletId && o.TenantId == _db.CurrentTenantId);
            return BusinessClock.BusinessDate(DateTime.UtcNow, outlet.TimeZone, outlet.BusinessDayStartHour);
        }

        public static decimal DiscountOf(CreateBillRequest request, decimal itemsTotal)
        {
            if (request.DiscountPercent.HasValue && request.DiscountAmount.HasValue)
                throw new ArgumentException("Give a discount in percent or in rupees, not both.");
            if (request.DiscountPercent is { } percent)
            {
                if (percent < 0 || percent > 100) throw new ArgumentException("The discount percent must be between 0 and 100.");
                return BillCalculator.Paise(itemsTotal * percent / 100m);
            }
            if (request.DiscountAmount is { } amount)
            {
                if (amount < 0 || amount > itemsTotal) throw new ArgumentException("The discount must be between 0 and the items total.");
                return BillCalculator.Paise(amount);
            }
            return 0m;
        }

        // On PostgreSQL one statement raises the counter and locks its row until the bill is saved.
        private async Task<int> NextNumber(int financialYear)
        {
            var tenantId = _db.CurrentTenantId;
            var outletId = _db.CurrentOutletId;
            if (_db.Database.IsRelational())
            {
                var numbers = await _db.Database.SqlQuery<int>($@"
                    INSERT INTO ""BillCounters"" (""TenantId"", ""OutletId"", ""FinancialYear"", ""LastNumber"")
                    VALUES ({tenantId}, {outletId}, {financialYear}, 1)
                    ON CONFLICT (""TenantId"", ""OutletId"", ""FinancialYear"")
                    DO UPDATE SET ""LastNumber"" = ""BillCounters"".""LastNumber"" + 1
                    RETURNING ""LastNumber"" AS ""Value""").ToListAsync();
                return numbers.Single();
            }

            var max = await _db.Bills.Where(b => b.OutletId == outletId && b.FinancialYear == financialYear).Select(b => (int?)b.Sequence).MaxAsync();
            return (max ?? 0) + 1;
        }

        public static BillDto ToDto(Bill b) => new(
            b.Id, b.KitchenOrderId, b.InvoiceNumber, TitleFor(b.TaxMode), b.TaxMode == TaxMode.Composition ? CompositionNote : null,
            b.IssuedAt, b.BusinessDate, b.TaxMode.ToString(), b.PricesIncludeTax, b.Status.ToString(), b.OrderLabel, b.TokenNumber,
            b.CustomerName, b.CustomerPhone, b.ItemsTotal, b.Discount, b.TaxableValue, b.Cgst, b.Sgst, b.RoundOff, b.Total,
            b.VoidReason, b.VoidedAt, b.PrintCount, b.Version,
            new StoreOnBill(b.RestaurantName, b.OutletName, b.Address, b.Phone, b.Gstin, b.Fssai, b.Footer),
            b.Lines.OrderBy(l => l.Id).Select(l => new BillLineDto(l.ItemName, l.Quantity, l.UnitPrice, l.TaxRate, l.Amount, l.Discount,
                l.TaxableValue, l.Cgst, l.Sgst)).ToList(),
            b.TaxMode == TaxMode.Regular ? BillCalculator.TaxSummary(b.Lines) : Array.Empty<TaxSummaryRow>(),
            PaidOf(b), DueOf(b), b.PaidAt,
            b.Payments.OrderBy(p => p.Id).Select(p => new PaymentDto(p.Id, p.Method.ToString(), p.Amount, p.Tendered, p.Change, p.Reference, p.ReceivedAt)).ToList(),
            b.CreditedAt, b.VoidApprovedBy);

        public static BillSummaryDto ToSummary(Bill b) =>
            new(b.Id, b.KitchenOrderId, b.InvoiceNumber, b.IssuedAt, b.OrderLabel, b.TokenNumber, b.Total, b.Status.ToString(), b.Version);

        // ---------- Billing settings ----------

        public static BillingSettingsDto SettingsOf(Outlet outlet)
        {
            var s = outlet.GetSettings();
            return new BillingSettingsDto(s.TaxMode.ToString(), s.PricesIncludeTax, s.BillPrefix, s.UpiId, s.UpiName, s.PaperWidth, s.PrintLanguage);
        }

        public static PrintSettingsDto PrintSettingsOf(Outlet outlet)
        {
            var s = outlet.GetSettings();
            return new PrintSettingsDto(s.PaperWidth, s.PrintLanguage, !string.IsNullOrWhiteSpace(s.UpiId), !string.IsNullOrWhiteSpace(s.GoogleReviewUrl), s.FeedbackOnBill, s.ReprintNeedsPin);
        }

        // Counts prints; the second and later prints of a bill are marked DUPLICATE on paper.
        public async Task<int> MarkPrinted(int billId)
        {
            var bill = await _db.Bills.FirstOrDefaultAsync(b => b.Id == billId) ?? throw new KeyNotFoundException("Bill not found.");
            bill.PrintCount++;
            await _db.SaveChangesAsync();
            return bill.PrintCount;
        }

        public static Dictionary<string, string[]> ValidateSettings(BillingSettingsDto dto)
        {
            var errors = new Dictionary<string, string[]>();
            if (!Enum.TryParse<TaxMode>(dto.TaxMode, true, out _) || int.TryParse(dto.TaxMode, out _))
                errors["taxmode"] = new[] { "Choose Regular, Composition or None." };
            if (string.IsNullOrWhiteSpace(dto.BillPrefix) || !Regex.IsMatch(dto.BillPrefix.Trim().ToUpperInvariant(), "^[A-Z0-9]{1,3}$"))
                errors["billprefix"] = new[] { "Use 1 to 3 letters or digits, for example INV." };
            if (!string.IsNullOrWhiteSpace(dto.UpiId) && !Regex.IsMatch(dto.UpiId.Trim(), @"^[A-Za-z0-9._-]{2,200}@[A-Za-z][A-Za-z0-9]{1,63}$"))
                errors["upiid"] = new[] { "Enter a UPI ID like yourname@okaxis." };
            if (dto.UpiName is { Length: > 50 })
                errors["upiname"] = new[] { "The name for UPI can be at most 50 characters." };
            if (dto.PaperWidth != 58 && dto.PaperWidth != 80)
                errors["paperwidth"] = new[] { "Choose 58 mm or 80 mm paper." };
            if (dto.PrintLanguage != "en" && dto.PrintLanguage != "gu" && dto.PrintLanguage != "hi")
                errors["printlanguage"] = new[] { "Choose English (en), Gujarati (gu) or Hindi (hi)." };
            return errors;
        }

        public static void ApplySettings(Outlet outlet, BillingSettingsDto dto)
        {
            var s = outlet.GetSettings();
            s.TaxMode = Enum.Parse<TaxMode>(dto.TaxMode, true);
            s.PricesIncludeTax = dto.PricesIncludeTax;
            s.BillPrefix = dto.BillPrefix.Trim().ToUpperInvariant();
            s.UpiId = string.IsNullOrWhiteSpace(dto.UpiId) ? null : dto.UpiId.Trim();
            s.UpiName = string.IsNullOrWhiteSpace(dto.UpiName) ? null : dto.UpiName.Trim();
            s.PaperWidth = dto.PaperWidth;
            s.PrintLanguage = dto.PrintLanguage;
            outlet.Settings = s.ToJson();
        }
    }
}
