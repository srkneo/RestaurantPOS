using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;
using POSSystem.Platform;
using Microsoft.AspNetCore.SignalR;
using POSSystem.Hubs;

namespace POSSystem.Billing
{
    // One guest rating per bill, given from the QR code printed on the bill (v2.0).
    public class GuestFeedback : TenantEntity
    {
        public int Id { get; set; }
        public int BillId { get; set; }
        public int Rating { get; set; }
        [MaxLength(500)] public string? Comment { get; set; }
    }

    public record FeedbackFormDto(string RestaurantName, string InvoiceNumber, bool AlreadyGiven);
    public record FeedbackRequest(int Rating, string? Comment);
    public record FeedbackLinkDto(string Token);
    public record FeedbackItemDto(int Id, string InvoiceNumber, int Rating, string? Comment, DateTime CreatedAt);
    public record FeedbackListDto(int Count, decimal? Average, IReadOnlyList<FeedbackItemDto> Items);
    public record FeedbackSettingDto(bool Enabled);

    // Guest feedback: the bill's QR opens /feedback/<token> without signing in. The token is the bill id signed by the
    // server (ASP.NET data protection), so it cannot be guessed and needs no extra column. Off until the owner turns it on.
    public static class GuestFeedbackEndpoints
    {
        private const string Purpose = "POS.GuestFeedback.v1";
        public const int MaxComment = 500;
        private static readonly string[] Till = { "cashier", "manager", "admin" };
        private static readonly string[] Managers = { "manager", "admin" };

        public static string TokenFor(IDataProtectionProvider protection, int tenantId, int billId) =>
            protection.CreateProtector(Purpose).Protect(FormattableString.Invariant($"{tenantId}:{billId}"));

        // The bill of a token, with that restaurant's data opened for this request. Null = bad token, feedback off or restaurant disabled.
        private static async Task<Bill?> Open(AppDbContext db, TenantStatus tenants, IDataProtectionProvider protection, string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 400) return null;
            string text;
            try { text = protection.CreateProtector(Purpose).Unprotect(token); }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
            var parts = text.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tenantId)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var billId)) return null;
            var bill = await db.Bills.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId && b.TenantId == tenantId);
            if (bill == null || bill.Status == BillStatus.Void || await tenants.IsDisabledAsync(db, tenantId)) return null;
            var outlet = await db.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == bill.OutletId && o.TenantId == tenantId);
            if (outlet == null || !outlet.GetSettings().FeedbackOnBill) return null;
            db.CurrentTenantId = tenantId;
            db.CurrentOutletId = bill.OutletId;
            return bill;
        }

        private static Task<Outlet?> CurrentOutlet(AppDbContext db) =>
            db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);

        public static WebApplication MapGuestFeedbackEndpoints(this WebApplication app)
        {
            var guest = app.MapGroup("/api/feedback/{token}").AllowAnonymous().RequireRateLimiting(RateLimits.Qr).WithTags("Feedback");

            guest.MapGet("/", async (string token, AppDbContext db, TenantStatus tenants, IDataProtectionProvider protection) =>
            {
                var bill = await Open(db, tenants, protection, token);
                if (bill == null) return Results.NotFound();
                var given = await db.Set<GuestFeedback>().AnyAsync(f => f.BillId == bill.Id);
                return Results.Ok(new FeedbackFormDto(bill.RestaurantName, bill.InvoiceNumber, given));
            }).WithName("GetFeedbackForm").WithSummary("Restaurant and bill for a guest who scanned the feedback QR");

            guest.MapPost("/", async (string token, FeedbackRequest request, AppDbContext db, TenantStatus tenants, IDataProtectionProvider protection,
                IHubContext<OrderHub> hub) =>
            {
                var bill = await Open(db, tenants, protection, token);
                if (bill == null) return Results.NotFound();
                var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
                var errors = new Dictionary<string, string[]>();
                if (request.Rating is < 1 or > 5) errors["rating"] = new[] { "Choose 1 to 5 stars." };
                if (comment is { Length: > MaxComment }) errors["comment"] = new[] { $"At most {MaxComment} characters." };
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                if (await db.Set<GuestFeedback>().AnyAsync(f => f.BillId == bill.Id))
                    return Results.Conflict(new { message = "Thank you, we already have your feedback for this bill.", code = "already-given" });
                db.Set<GuestFeedback>().Add(new GuestFeedback
                {
                    TenantId = bill.TenantId, OutletId = bill.OutletId, BillId = bill.Id, Rating = request.Rating, Comment = comment,
                    CreatedBy = "guest"
                });
                await db.SaveChangesAsync();
                // 1 or 2 stars: tell the staff at once, the guest may still be at the table.
                if (request.Rating <= 2)
                    await hub.Clients.Group($"tenant-{bill.TenantId}").SendAsync("LowRating",
                        new { billId = bill.Id, invoiceNumber = bill.InvoiceNumber, orderLabel = bill.OrderLabel, rating = request.Rating, comment });
                return Results.Ok(new { message = "Thank you!" });
            }).WithName("GiveFeedback").WithSummary("A guest's 1-5 star rating and comment for one bill");

            // Staff: the bill's signed token, for the QR on the printed bill and the WhatsApp message.
            app.MapGet("/api/bills/{id:int}/feedback-link", async (int id, AppDbContext db, IDataProtectionProvider protection) =>
            {
                var outlet = await CurrentOutlet(db);
                if (outlet == null || !outlet.GetSettings().FeedbackOnBill) return Results.NotFound();
                var bill = await db.Bills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && b.OutletId == db.CurrentOutletId);
                return bill == null ? Results.NotFound() : Results.Ok(new FeedbackLinkDto(TokenFor(protection, bill.TenantId, bill.Id)));
            }).RequireAuthorization(p => p.RequireRole(Till)).WithTags("Feedback")
              .WithName("GetFeedbackLink").WithSummary("Signed feedback token of a bill (empty when feedback is off)");

            app.MapGet("/api/bills/{id:int}/feedback-qr.svg", async (int id, string? origin, AppDbContext db, IDataProtectionProvider protection) =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var site) || (site.Scheme != Uri.UriSchemeHttp && site.Scheme != Uri.UriSchemeHttps))
                    return Results.BadRequest(new { message = "Give the address of this POS, like https://pos.example.com." });
                var outlet = await CurrentOutlet(db);
                if (outlet == null || !outlet.GetSettings().FeedbackOnBill) return Results.NotFound();
                var bill = await db.Bills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && b.OutletId == db.CurrentOutletId);
                if (bill == null) return Results.NotFound();
                var link = $"{site.GetLeftPart(UriPartial.Authority)}/feedback/{TokenFor(protection, bill.TenantId, bill.Id)}";
                using var generator = new QRCoder.QRCodeGenerator();
                using var data = generator.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.L);
                return Results.Content(new QRCoder.SvgQRCode(data).GetGraphic(4), "image/svg+xml");
            }).RequireAuthorization(p => p.RequireRole(Till)).WithTags("Feedback")
              .WithName("GetFeedbackQr").WithSummary("QR code (SVG) of the bill's feedback page");

            // Owner and managers: the latest ratings and the average.
            app.MapGet("/api/admin/feedback", async (AppDbContext db) =>
            {
                var all = db.Set<GuestFeedback>().Where(f => f.OutletId == db.CurrentOutletId);
                var count = await all.CountAsync();
                var average = count == 0 ? (decimal?)null : Math.Round((decimal)await all.AverageAsync(f => f.Rating), 1);
                var latest = await all.OrderByDescending(f => f.Id).Take(200).ToListAsync();
                var billIds = latest.Select(f => f.BillId).ToList();
                var numbers = await db.Bills.AsNoTracking().Where(b => billIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.InvoiceNumber);
                return Results.Ok(new FeedbackListDto(count, average, latest.Select(f => new FeedbackItemDto(f.Id,
                    numbers.TryGetValue(f.BillId, out var n) ? n : string.Empty, f.Rating, f.Comment, f.CreatedAt)).ToList()));
            }).RequireAuthorization(p => p.RequireRole(Managers)).WithTags("Feedback")
              .WithName("GetFeedback").WithSummary("Latest guest ratings of this outlet with the average");

            var setting = app.MapGroup("/api/admin/feedback-setting").RequireAuthorization(p => p.RequireRole("admin")).WithTags("Settings");
            setting.MapGet("/", async (AppDbContext db) =>
                await CurrentOutlet(db) is { } outlet ? Results.Ok(new FeedbackSettingDto(outlet.GetSettings().FeedbackOnBill)) : Results.NotFound())
                .WithName("GetFeedbackSetting").WithSummary("Whether bills print a feedback QR code");
            setting.MapPut("/", async (FeedbackSettingDto request, AppDbContext db) =>
            {
                var outlet = await CurrentOutlet(db);
                if (outlet == null) return Results.NotFound();
                var settings = outlet.GetSettings();
                settings.FeedbackOnBill = request.Enabled;
                outlet.Settings = settings.ToJson();
                await db.SaveChangesAsync();
                return Results.Ok(new FeedbackSettingDto(settings.FeedbackOnBill));
            }).WithName("SetFeedbackSetting").WithSummary("Turn the feedback QR on bills on or off");

            return app;
        }
    }
}
