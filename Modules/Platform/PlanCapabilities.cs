using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using POSSystem.Models;

namespace POSSystem.Platform
{
    // C4: each subscription plan (trial, monthly, yearly) lists the capabilities it allows in the server settings:
    // Plans__<plan>__Capabilities = comma-separated capability keys. Not set or empty = every capability (today's behaviour).
    // A restaurant may use what its own list (C3, TenantCapabilities) AND its plan allow.
    public static class PlanCapabilities
    {
        // The plan's capabilities in registry order. No plan (free use, shop PC) or no setting = every capability; unknown keys are dropped.
        public static IReadOnlyList<string> Allowed(IConfiguration config, string? plan)
        {
            if (config == null) throw new ArgumentException("Configuration is missing.");
            if (string.IsNullOrWhiteSpace(plan)) return Capabilities.Keys;
            var setting = config[$"Plans:{plan.Trim().ToLowerInvariant()}:Capabilities"];
            if (string.IsNullOrWhiteSpace(setting)) return Capabilities.Keys;
            var set = new HashSet<string>(setting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
            return Capabilities.Keys.Where(k => set.Contains(k)).ToList();
        }

        // The restaurant's own list AND its plan, in registry order. A capability whose dependency is not left is dropped too
        // (dependencies come earlier in the registry, so one pass is enough).
        public static IReadOnlyList<string> Effective(Tenant tenant, IConfiguration config)
        {
            if (tenant == null) throw new ArgumentException("Tenant is missing.");
            var allowed = new HashSet<string>(Allowed(config, tenant.Plan), StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var key in TenantCapabilities.Parse(tenant.Capabilities))
            {
                if (!allowed.Contains(key)) continue;
                var capability = Capabilities.Find(key)!;
                if (capability.DependsOn.All(d => result.Contains(d))) result.Add(key);
            }
            return result;
        }

        public static bool Has(Tenant tenant, IConfiguration config, string key) => Effective(tenant, config).Contains(key);

        // For C9/C9b: a capability outside the plan cannot be turned on. Null = all keys are in the plan.
        public static string? Check(IConfiguration config, string? plan, IEnumerable<string>? keys)
        {
            if (keys == null) return "Capabilities are missing.";
            var allowed = Allowed(config, plan);
            foreach (var key in keys)
            {
                if (!allowed.Contains(key)) return $"Capability {key} is not in the {plan!.Trim().ToLowerInvariant()} plan.";
            }
            return null;
        }
    }
}
