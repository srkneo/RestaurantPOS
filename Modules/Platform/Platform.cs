using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace POSSystem.Models
{
    // A restaurant business (one customer of the POS). Data of different tenants never mixes.
    public class Tenant
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Cloud subscription (v2.0): trial, monthly or yearly, paid up to and including PaidUntil (India date).
        // No PaidUntil = no subscription needed (shop PCs with a licence, restaurants added before subscriptions).
        [MaxLength(10)] public string? Plan { get; set; }
        public DateOnly? PaidUntil { get; set; }
        [MaxLength(20)] public string? OwnerPhone { get; set; }

        // C3: capability keys this restaurant may use, as a JSON list (see TenantCapabilities). Null = every capability (restaurants added before C3).
        [MaxLength(1000)] public string? Capabilities { get; set; }
    }

    // One location of a restaurant. Orders, menu and tables belong to an outlet.
    public class Outlet
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TimeZone { get; set; } = BusinessClock.DefaultTimeZone;
        public int BusinessDayStartHour { get; set; } = BusinessClock.DefaultDayStartHour;
        public string Settings { get; set; } = "{}";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Store profile, printed on bills (v1.6).
        [MaxLength(300)] public string? Address { get; set; }
        [MaxLength(20)] public string? Phone { get; set; }
        [MaxLength(15)] public string? Gstin { get; set; }
        [MaxLength(14)] public string? Fssai { get; set; }
        [MaxLength(300)] public string? ReceiptFooter { get; set; }

        public OutletSettings GetSettings() => OutletSettings.Parse(Settings);
    }

    // Logo of an outlet, kept in the database so it survives server moves and backups.
    public class OutletLogo
    {
        public int OutletId { get; set; }
        public int TenantId { get; set; }
        [MaxLength(50)] public string ContentType { get; set; } = string.Empty;
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // Who changed what and when. Written automatically by AppDbContext for menu, tables, store, staff and cancellations.
    public class AuditLog : TenantEntity
    {
        public long Id { get; set; }
        [MaxLength(20)] public string Action { get; set; } = string.Empty;
        [MaxLength(50)] public string EntityType { get; set; } = string.Empty;
        [MaxLength(50)] public string? EntityId { get; set; }
        public string? Details { get; set; }
    }

    // Per-outlet options stored as JSON. New options get a default here, so old rows keep working.
    public class OutletSettings
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public int MaxItemQuantity { get; set; } = 99;

        // Size of dish names on the POS: small (long names fit), normal or large (easy to read from a distance).
        public string MenuTextSize { get; set; } = "normal";

        // Up to this many rupees still due after a payment can be waived as small change (0 = never).
        public decimal SmallChangeLimit { get; set; } = 5m;

        // Loyalty points (v2.2), off until the owner turns it on: a guest earns 1 point for every this many rupees of a paid bill,
        // and a point is worth this many rupees when spent.
        public bool LoyaltyEnabled { get; set; }
        public decimal LoyaltyRupeesPerPoint { get; set; } = 100m;
        public decimal LoyaltyPointValue { get; set; } = 1m;

        // First token of each business day.
        public int FirstToken { get; set; } = 1;

        // Takeaway orders must carry the customer's name.
        public bool TakeawayNeedsName { get; set; } = true;

        public bool DineInEnabled { get; set; } = true;
        public bool TakeawayEnabled { get; set; } = true;
        public OrderType DefaultOrderType { get; set; } = OrderType.DineIn;
        public bool KitchenScreenEnabled { get; set; } = true;
        // Simple counter (bakery, sweet shop): dine-in needs no table, takeaway needs no name, and the bill opens as soon as the order is placed.
        public bool SimpleCounter { get; set; }

        // The platform owner may open this restaurant as Support to help (payments, refunds and passwords stay blocked).
        public bool AllowSupportAccess { get; set; } = true;

        // QR table ordering: guests order from the table's QR code (off until the owner turns it on).
        public bool QrOrderingEnabled { get; set; }

        // Most discount (percent of the bill) a cashier gives without a manager PIN; null = no limit.
        public decimal? CashierMaxDiscountPercent { get; set; }

        // Billing (v1.6). No GST until the owner sets the registration in billing settings.
        public TaxMode TaxMode { get; set; } = TaxMode.None;
        public bool PricesIncludeTax { get; set; }
        public string BillPrefix { get; set; } = "INV";

        // UPI ID (like name@okaxis) and payee name for the QR code on bills.
        public string? UpiId { get; set; }
        public string? UpiName { get; set; }

        // Google review page; when set, bills print a "Rate us on Google" QR code.
        public string? GoogleReviewUrl { get; set; }

        // Bills print a QR code where the guest rates the visit (1-5 stars and a comment).
        public bool FeedbackOnBill { get; set; }

        // A cashier needs a manager PIN to print a bill a second time (owner can turn it off).
        public bool ReprintNeedsPin { get; set; } = true;

        // Receipt printer: 58 or 80 mm paper; bills and KOTs in English (en), Gujarati (gu) or Hindi (hi).
        public int PaperWidth { get; set; } = 80;
        public string PrintLanguage { get; set; } = "en";

        public static readonly string[] Presets = { "dine-in", "cafe", "takeaway-counter", "bakery", "dairy" };

        // Ready-made setups for common outlet types; quantity limit and first token are kept.
        public OutletSettings WithPreset(string preset)
        {
            var s = Parse(ToJson());
            switch (preset)
            {
                case "dine-in":
                    s.DineInEnabled = true; s.TakeawayEnabled = true; s.DefaultOrderType = OrderType.DineIn; s.TakeawayNeedsName = true; s.KitchenScreenEnabled = true; s.SimpleCounter = false;
                    break;
                case "cafe":
                    s.DineInEnabled = true; s.TakeawayEnabled = true; s.DefaultOrderType = OrderType.Takeaway; s.TakeawayNeedsName = true; s.KitchenScreenEnabled = true; s.SimpleCounter = false;
                    break;
                case "takeaway-counter":
                    s.DineInEnabled = false; s.TakeawayEnabled = true; s.DefaultOrderType = OrderType.Takeaway; s.TakeawayNeedsName = false; s.KitchenScreenEnabled = true; s.SimpleCounter = false;
                    break;
                case "bakery":
                    s.DineInEnabled = true; s.TakeawayEnabled = true; s.DefaultOrderType = OrderType.Takeaway; s.TakeawayNeedsName = false; s.KitchenScreenEnabled = true; s.SimpleCounter = true;
                    break;
                case "dairy":
                    s.DineInEnabled = false; s.TakeawayEnabled = true; s.DefaultOrderType = OrderType.Takeaway; s.TakeawayNeedsName = false; s.KitchenScreenEnabled = true; s.SimpleCounter = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown preset '{preset}'. Use one of: {string.Join(", ", Presets)}.");
            }
            return s;
        }

        public static OutletSettings Parse(string? json)
        {
            try
            {
                return JsonSerializer.Deserialize<OutletSettings>(string.IsNullOrWhiteSpace(json) ? "{}" : json, Json) ?? new OutletSettings();
            }
            catch (JsonException)
            {
                return new OutletSettings();
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this, Json);
    }

    // The business day starts at a set local hour (default 4 am), so late-night orders count for the same day.
    public static class BusinessClock
    {
        public const string DefaultTimeZone = "Asia/Kolkata";
        public const int DefaultDayStartHour = 4;

        public static bool IsKnownZone(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(id);
                return true;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
            {
                return id == DefaultTimeZone;
            }
        }

        public static TimeZoneInfo FindZone(string? id)
        {
            var zoneId = string.IsNullOrWhiteSpace(id) ? DefaultTimeZone : id;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
            {
                // Containers without time zone data: India has no daylight saving, so a fixed +05:30 is exact.
                return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "India Standard Time", "India Standard Time");
            }
        }

        public static DateOnly BusinessDate(DateTime utcNow, string? timeZoneId, int dayStartHour)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), FindZone(timeZoneId));
            return DateOnly.FromDateTime(local.AddHours(-Math.Clamp(dayStartHour, 0, 23)));
        }
    }
}
