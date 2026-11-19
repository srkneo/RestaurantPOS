using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    // One payment a restaurant made to the POS owner (UPI or bank transfer, recorded by hand on /platform).
    public class TenantPayment
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        [MaxLength(10)] public string Plan { get; set; } = "monthly";
        [Precision(12, 2)] public decimal Amount { get; set; }
        [MaxLength(60)] public string? Reference { get; set; }
        public DateOnly PeriodFrom { get; set; }
        public DateOnly PeriodTo { get; set; }
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
        [MaxLength(256)] public string? RecordedBy { get; set; }
    }

    public record SubscriptionDto(string? Plan, DateOnly? PaidUntil, string State, int? DaysLeft, int GraceDays, decimal Monthly, decimal Yearly);
    public record PlansDto(int TrialDays, decimal Monthly, decimal Yearly, bool SignupOpen);
    public record SignupRequest(string? RestaurantName, string? OutletName, string? OwnerName, string? OwnerEmail, string? OwnerPhone, string? OwnerPassword, string? Website);
    public record RecordPaymentRequest(string? Plan, decimal? Amount, string? Reference);
    public record SetSubscriptionRequest(string? Plan, DateOnly? PaidUntil);
    public record TenantPaymentDto(int Id, string Plan, decimal Amount, string? Reference, DateOnly PeriodFrom, DateOnly PeriodTo, DateTime RecordedAt, string? RecordedBy);

    // Cloud subscriptions: 30-day free trial at signup, then monthly or yearly, paid by UPI or bank transfer and
    // recorded by the platform owner. States: none (no subscription needed), trial, active, overdue (up to 7 days
    // after the paid-up date: warning only) and suspended (new orders, bills and payments stop; all data stays viewable).
    public static class Subscription
    {
        public const int TrialDays = 30;
        public const int GraceDays = 7;
        public static readonly string[] Plans = { "trial", "monthly", "yearly" };

        private static readonly TimeZoneInfo India = ResolveIndia();
        private static TimeZoneInfo ResolveIndia()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
        }

        public static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India));

        public static string StateOf(Tenant t)
        {
            if (t.PaidUntil is not { } until) return "none";
            var today = Today();
            if (today <= until) return t.Plan == "trial" ? "trial" : "active";
            return today <= until.AddDays(GraceDays) ? "overdue" : "suspended";
        }

        public static decimal Price(IConfiguration config, string plan) =>
            plan == "yearly"
                ? (decimal.TryParse(config["Pricing:Yearly"], out var y) && y >= 0 ? y : 10000m)
                : (decimal.TryParse(config["Pricing:Monthly"], out var m) && m >= 0 ? m : 1000m);
    }

    // Is a restaurant's subscription suspended? Cached for a minute, cleared when the platform owner changes it.
    public class SubscriptionGate
    {
        private readonly IMemoryCache _cache;

        public SubscriptionGate(IMemoryCache cache)
        {
            _cache = cache;
        }

        private static string Key(int tenantId) => $"tenant-suspended:{tenantId}";

        public async Task<bool> IsSuspendedAsync(AppDbContext db, int tenantId)
        {
            if (tenantId <= 0) return false;
            return await _cache.GetOrCreateAsync(Key(tenantId), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
                return tenant != null && Subscription.StateOf(tenant) == "suspended";
            });
        }

        public void Forget(int tenantId) => _cache.Remove(Key(tenantId));
    }

    public static class SubscriptionEndpoints
    {
        private static readonly Regex Mobile = new(@"^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$", RegexOptions.Compiled);

        public static WebApplication MapSubscriptionEndpoints(this WebApplication app)
        {
            app.MapGet("/api/signup/plans", (IConfiguration config) => Results.Ok(new PlansDto(Subscription.TrialDays,
                    Subscription.Price(config, "monthly"), Subscription.Price(config, "yearly"), SignupOpen(config))))
                .AllowAnonymous().WithTags("Subscriptions").WithName("GetPlans").WithSummary("Trial length and prices shown on the signup page");

            // Self-signup: a new restaurant with its owner, 10 tables and a 30-day trial. Then the owner signs in.
            app.MapPost("/api/signup", async (SignupRequest req, IConfiguration config, AppDbContext db, UserManager<AppUser> users, IOtpSender telegram) =>
            {
                if (!SignupOpen(config)) return Results.NotFound();
                if (!string.IsNullOrEmpty(req.Website)) return Results.Ok(new { }); // filled only by bots
                var create = new CreateTenantRequest(req.RestaurantName, string.IsNullOrWhiteSpace(req.OutletName) ? "Main" : req.OutletName,
                    req.OwnerName, req.OwnerEmail, req.OwnerPassword);
                var errors = await PlatformEndpoints.ValidateAsync(create, users);
                var phone = req.OwnerPhone?.Trim() ?? string.Empty;
                if (!Mobile.IsMatch(phone)) errors["ownerphone"] = new[] { "Enter a 10-digit mobile number." };
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                var (tenant, createErrors) = await PlatformEndpoints.CreateTenantAsync(create, db, users);
                if (tenant == null) return Results.ValidationProblem(createErrors!);
                tenant.Plan = "trial";
                tenant.PaidUntil = Subscription.Today().AddDays(Subscription.TrialDays);
                tenant.OwnerPhone = phone;
                await db.SaveChangesAsync();

                var text = $"New signup: {tenant.Name} – {req.OwnerName?.Trim()}, {req.OwnerEmail?.Trim()}, {phone}. Trial until {tenant.PaidUntil:dd MMM yyyy}.";
                _ = Task.Run(() => telegram.SendAsync(text));
                return Results.Ok(new { trialUntil = tenant.PaidUntil });
            }).AllowAnonymous().RequireRateLimiting(RateLimits.Login).WithTags("Subscriptions").WithName("Signup")
              .WithSummary("Start a free trial: creates the restaurant and its owner");

            app.MapGet("/api/subscription", async (AppDbContext db, IConfiguration config) =>
            {
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == db.CurrentTenantId);
                if (tenant == null) return Results.NotFound();
                int? daysLeft = tenant.PaidUntil is { } until ? until.DayNumber - Subscription.Today().DayNumber : null;
                return Results.Ok(new SubscriptionDto(tenant.Plan, tenant.PaidUntil, Subscription.StateOf(tenant), daysLeft,
                    Subscription.GraceDays, Subscription.Price(config, "monthly"), Subscription.Price(config, "yearly")));
            }).RequireAuthorization(p => p.RequireRole("admin", "manager")).WithTags("Subscriptions")
              .WithName("GetSubscription").WithSummary("This restaurant's plan, paid-up date and state");

            var platform = app.MapGroup("/api/platform/tenants/{id:int}").RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole)).WithTags("Platform");

            platform.MapPost("/payments", async (int id, RecordPaymentRequest req, AppDbContext db, IConfiguration config, SubscriptionGate gate, ClaimsPrincipal user) =>
            {
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
                if (tenant == null) return Results.NotFound();
                var plan = (req.Plan ?? string.Empty).Trim().ToLowerInvariant();
                var errors = new Dictionary<string, string[]>();
                if (plan != "monthly" && plan != "yearly") errors["plan"] = new[] { "Choose monthly or yearly." };
                if (req.Amount is < 0 or > 1_000_000) errors["amount"] = new[] { "Enter an amount from 0 to 10,00,000." };
                var reference = req.Reference?.Trim();
                if (reference is { Length: > 60 }) errors["reference"] = new[] { "The reference can be at most 60 characters." };
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                // The new period starts the day after the paid-up date, or today when that date has passed.
                var today = Subscription.Today();
                var from = tenant.PaidUntil is { } until && until >= today ? until.AddDays(1) : today;
                var to = (plan == "yearly" ? from.AddYears(1) : from.AddMonths(1)).AddDays(-1);
                db.TenantPayments.Add(new TenantPayment
                {
                    TenantId = id, Plan = plan, Amount = req.Amount ?? Subscription.Price(config, plan),
                    Reference = string.IsNullOrEmpty(reference) ? null : reference, PeriodFrom = from, PeriodTo = to,
                    RecordedBy = user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity?.Name
                });
                tenant.Plan = plan;
                tenant.PaidUntil = to;
                await db.SaveChangesAsync();
                gate.Forget(id);
                return Results.Ok(new { plan, paidUntil = to, state = Subscription.StateOf(tenant) });
            }).WithName("RecordTenantPayment").WithSummary("Record a monthly or yearly payment; extends the paid-up date");

            platform.MapGet("/payments", async (int id, AppDbContext db) =>
                Results.Ok(await db.TenantPayments.AsNoTracking().Where(p => p.TenantId == id).OrderByDescending(p => p.Id)
                    .Select(p => new TenantPaymentDto(p.Id, p.Plan, p.Amount, p.Reference, p.PeriodFrom, p.PeriodTo, p.RecordedAt, p.RecordedBy)).ToListAsync()))
                .WithName("GetTenantPayments").WithSummary("Payments of one restaurant, newest first");

            // Corrections: extend a trial, give a restaurant free use (no date), or set the paid-up date by hand.
            platform.MapPut("/subscription", async (int id, SetSubscriptionRequest req, AppDbContext db, SubscriptionGate gate) =>
            {
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
                if (tenant == null) return Results.NotFound();
                var plan = req.Plan?.Trim().ToLowerInvariant();
                if (plan != null && Array.IndexOf(Subscription.Plans, plan) < 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["plan"] = new[] { "Choose trial, monthly or yearly." } });
                tenant.Plan = req.PaidUntil == null ? null : plan ?? tenant.Plan ?? "monthly";
                tenant.PaidUntil = req.PaidUntil;
                await db.SaveChangesAsync();
                gate.Forget(id);
                return Results.Ok(new { plan = tenant.Plan, paidUntil = tenant.PaidUntil, state = Subscription.StateOf(tenant) });
            }).WithName("SetTenantSubscription").WithSummary("Set the plan and paid-up date by hand (empty date = free use)");

            return app;
        }

        // Cloud only: a shop PC (licence required) never offers signup; Signup__Enabled=false closes it on the cloud.
        private static bool SignupOpen(IConfiguration config) =>
            !string.Equals(config["Signup:Enabled"], "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(config["Licence:Required"], "true", StringComparison.OrdinalIgnoreCase);
    }
}
