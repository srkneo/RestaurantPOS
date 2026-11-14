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

        // First token of each business day.
        public int FirstToken { get; set; } = 1;

        // Takeaway orders must carry the customer's name.
        public bool TakeawayNeedsName { get; set; } = true;

        public bool DineInEnabled { get; set; } = true;
        public bool TakeawayEnabled { get; set; } = true;
        public OrderType DefaultOrderType { get; set; } = OrderType.DineIn;
        public bool KitchenScreenEnabled { get; set; } = true;

        public static readonly string[] Presets = { "dine-in", "cafe", "takeaway-counter" };

        // Ready-made setups for common outlet types; quantity limit and first token are kept.
        public OutletSettings WithPreset(string preset)
        {
            var s = Parse(ToJson());
            switch (preset)
            {
                case "dine-in":
                    s.DineInEnabled = true; s.TakeawayEnabled = true; s.DefaultOrderType = OrderType.DineIn; s.TakeawayNeedsName = true; s.KitchenScreenEnabled = true;
                    break;
                case "cafe":
                    s.DineInEnabled = true; s.TakeawayEnabled = true; s.DefaultOrderType = OrderType.Takeaway; s.TakeawayNeedsName = true; s.KitchenScreenEnabled = true;
                    break;
                case "takeaway-counter":
                    s.DineInEnabled = false; s.TakeawayEnabled = true; s.DefaultOrderType = OrderType.Takeaway; s.TakeawayNeedsName = false; s.KitchenScreenEnabled = true;
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
