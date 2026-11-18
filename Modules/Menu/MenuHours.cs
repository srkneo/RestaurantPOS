using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Menu
{
    // Time-based menu: a category can be served only between two times of the outlet's day (breakfast 07:00-11:00,
    // or late night 22:00-02:00 across midnight). No times = served all day.
    public static class MenuHours
    {
        private static readonly string[] Formats = { "HH:mm", "H:mm" };

        public static bool IsOpen(TimeOnly? from, TimeOnly? to, TimeOnly now)
        {
            if (from == null || to == null || from == to) return true;
            return from < to ? now >= from && now < to : now >= from || now < to;
        }

        // Time of day now in the outlet's time zone.
        public static TimeOnly LocalNow(string? timeZone) =>
            TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessClock.FindZone(timeZone)));

        // Empty text = no time (true, null); "07:00" = that time; anything else = false.
        public static bool TryParse(string? text, out TimeOnly? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!TimeOnly.TryParseExact(text.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return false;
            value = time;
            return true;
        }

        public static TimeOnly? Parse(string? text) => TryParse(text, out var value) ? value : null;

        public static string? Text(TimeOnly? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture);

        // Categories of this outlet that are outside their serving hours right now.
        public static async Task<HashSet<int>> ClosedCategoryIds(AppDbContext db)
        {
            var timed = await db.MenuCategories
                .Where(c => c.OutletId == db.CurrentOutletId && c.AvailableFrom != null && c.AvailableTo != null).ToListAsync();
            if (timed.Count == 0) return new HashSet<int>();
            var outlet = await db.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId);
            var now = LocalNow(outlet?.TimeZone);
            return timed.Where(c => !IsOpen(c.AvailableFrom, c.AvailableTo, now)).Select(c => c.Id).ToHashSet();
        }
    }
}
