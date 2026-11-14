using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public record StoreProfileDto(string RestaurantName, string OutletName, string? Address, string? Phone, string? Gstin, string? Fssai, string? ReceiptFooter, bool HasLogo);
    public record StoreProfileRequest(string? RestaurantName, string? OutletName, string? Address, string? Phone, string? Gstin, string? Fssai, string? ReceiptFooter);
    public record StoreSummaryDto(string RestaurantName, string OutletName, bool HasLogo);
    public record OutletSettingsDto(string TimeZone, int BusinessDayStartHour, bool DineInEnabled, bool TakeawayEnabled, OrderType DefaultOrderType,
        bool TakeawayNeedsName, int MaxItemQuantity, int FirstToken, bool KitchenScreenEnabled);
    public record PresetRequest(string? Preset);

    // Validation and mapping for the store profile and outlet settings. Endpoints only load, call these, and save.
    public static class StoreRules
    {
        // GSTIN: 2-digit state code, 10-character PAN, entity number, 'Z', check character (e.g. 24AAACC1206D1ZM for Gujarat).
        private static readonly Regex GstinPattern = new(@"^[0-3][0-9][A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$");
        private static readonly Regex FssaiPattern = new(@"^[0-9]{14}$");
        private static readonly Regex PhonePattern = new(@"^\+?[0-9 ]{6,20}$");

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public static Dictionary<string, string[]> Validate(StoreProfileRequest req)
        {
            var errors = new Dictionary<string, string[]>();
            var restaurant = Clean(req.RestaurantName);
            var outlet = Clean(req.OutletName);
            if (restaurant == null || restaurant.Length > 200) errors["restaurantname"] = new[] { "Enter the restaurant name (up to 200 characters)." };
            if (outlet == null || outlet.Length > 200) errors["outletname"] = new[] { "Enter the outlet name (up to 200 characters)." };
            if (Clean(req.Address) is { Length: > 300 }) errors["address"] = new[] { "The address can be at most 300 characters." };
            var phone = Clean(req.Phone);
            if (phone != null && !PhonePattern.IsMatch(phone)) errors["phone"] = new[] { "Enter a valid phone number." };
            var gstin = Clean(req.Gstin)?.ToUpperInvariant();
            if (gstin != null && !GstinPattern.IsMatch(gstin)) errors["gstin"] = new[] { "Enter a valid 15-character GSTIN, e.g. 24AAACC1206D1ZM." };
            var fssai = Clean(req.Fssai);
            if (fssai != null && !FssaiPattern.IsMatch(fssai)) errors["fssai"] = new[] { "The FSSAI licence number has 14 digits." };
            if (Clean(req.ReceiptFooter) is { Length: > 300 }) errors["receiptfooter"] = new[] { "The bill footer can be at most 300 characters." };
            return errors;
        }

        public static void Apply(Tenant tenant, Outlet outlet, StoreProfileRequest req)
        {
            tenant.Name = Clean(req.RestaurantName)!;
            outlet.Name = Clean(req.OutletName)!;
            outlet.Address = Clean(req.Address);
            outlet.Phone = Clean(req.Phone);
            outlet.Gstin = Clean(req.Gstin)?.ToUpperInvariant();
            outlet.Fssai = Clean(req.Fssai);
            outlet.ReceiptFooter = Clean(req.ReceiptFooter);
        }

        public static StoreProfileDto ToProfile(Tenant tenant, Outlet outlet, bool hasLogo) =>
            new(tenant.Name, outlet.Name, outlet.Address, outlet.Phone, outlet.Gstin, outlet.Fssai, outlet.ReceiptFooter, hasLogo);

        public static StoreSummaryDto ToSummary(Tenant tenant, Outlet outlet, bool hasLogo) => new(tenant.Name, outlet.Name, hasLogo);

        public static Dictionary<string, string[]> Validate(OutletSettingsDto dto)
        {
            var errors = new Dictionary<string, string[]>();
            if (!BusinessClock.IsKnownZone(dto.TimeZone)) errors["timezone"] = new[] { "Unknown time zone. Use for example Asia/Kolkata." };
            if (dto.BusinessDayStartHour is < 0 or > 23) errors["businessdaystarthour"] = new[] { "The business day starts at an hour from 0 to 23." };
            if (!dto.DineInEnabled && !dto.TakeawayEnabled) errors["ordertypes"] = new[] { "Turn on at least one order type." };
            else if ((dto.DefaultOrderType == OrderType.DineIn && !dto.DineInEnabled) || (dto.DefaultOrderType == OrderType.Takeaway && !dto.TakeawayEnabled))
                errors["defaultordertype"] = new[] { "The default order type must be turned on." };
            if (dto.MaxItemQuantity is < 1 or > 999) errors["maxitemquantity"] = new[] { "The quantity limit must be from 1 to 999." };
            if (dto.FirstToken is < 1 or > 9999) errors["firsttoken"] = new[] { "The first token must be from 1 to 9999." };
            return errors;
        }

        public static OutletSettingsDto ToSettings(Outlet outlet)
        {
            var s = outlet.GetSettings();
            return new OutletSettingsDto(outlet.TimeZone, outlet.BusinessDayStartHour, s.DineInEnabled, s.TakeawayEnabled, s.DefaultOrderType,
                s.TakeawayNeedsName, s.MaxItemQuantity, s.FirstToken, s.KitchenScreenEnabled);
        }

        public static void Apply(Outlet outlet, OutletSettingsDto dto)
        {
            var s = outlet.GetSettings();
            s.DineInEnabled = dto.DineInEnabled;
            s.TakeawayEnabled = dto.TakeawayEnabled;
            s.DefaultOrderType = dto.DefaultOrderType;
            s.TakeawayNeedsName = dto.TakeawayNeedsName;
            s.MaxItemQuantity = dto.MaxItemQuantity;
            s.FirstToken = dto.FirstToken;
            s.KitchenScreenEnabled = dto.KitchenScreenEnabled;
            outlet.Settings = s.ToJson();
            outlet.TimeZone = dto.TimeZone.Trim();
            outlet.BusinessDayStartHour = dto.BusinessDayStartHour;
        }
    }
}
