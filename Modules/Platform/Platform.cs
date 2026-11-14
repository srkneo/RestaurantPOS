using System;
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

        public OutletSettings GetSettings() => OutletSettings.Parse(Settings);
    }

    // Per-outlet options stored as JSON. New options get a default here, so old rows keep working.
    public class OutletSettings
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public int MaxItemQuantity { get; set; } = 99;

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
