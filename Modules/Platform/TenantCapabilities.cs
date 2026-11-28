using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using POSSystem.Models;

namespace POSSystem.Platform
{
    // C3: the capabilities a restaurant (Tenant) may use, stored as a JSON list in Tenants.Capabilities.
    // No list (null: restaurants added before C3) = every capability, which is today's behaviour.
    public static class TenantCapabilities
    {
        // The stored list in registry order. Null, empty or broken JSON = every capability; unknown keys are dropped.
        public static IReadOnlyList<string> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Capabilities.Keys;
            string[]? stored;
            try
            {
                stored = JsonSerializer.Deserialize<string[]>(json);
            }
            catch (JsonException)
            {
                return Capabilities.Keys;
            }
            if (stored == null) return Capabilities.Keys;
            var set = new HashSet<string>(stored.Where(k => k != null), StringComparer.Ordinal);
            return Capabilities.Keys.Where(k => set.Contains(k)).ToList();
        }

        // Checks the list with Capabilities.Validate and writes it as JSON in registry order.
        public static string ToJson(IEnumerable<string>? keys)
        {
            var list = keys?.ToList();
            var error = Capabilities.Validate(list);
            if (error != null) throw new ArgumentException(error);
            var set = new HashSet<string>(list!, StringComparer.Ordinal);
            return JsonSerializer.Serialize(Capabilities.Keys.Where(k => set.Contains(k)).ToList());
        }

        // The JSON list of an outlet type (C2), for a new restaurant.
        public static string ForType(string? outletType)
        {
            var type = OutletTypes.Find(outletType);
            if (type == null) throw new ArgumentException($"Unknown outlet type: {outletType}");
            return ToJson(type.Capabilities);
        }

        public static bool Has(Tenant tenant, string key)
        {
            if (tenant == null) throw new ArgumentException("Tenant is missing.");
            return Parse(tenant.Capabilities).Contains(key);
        }
    }
}
