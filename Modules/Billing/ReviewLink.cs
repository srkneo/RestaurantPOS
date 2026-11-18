using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using QRCoder;

namespace POSSystem.Billing
{
    public record ReviewLinkDto(string? ReviewUrl);

    // Google review link: the owner pastes it once, then every bill prints a "Rate us on Google" QR code.
    public static class ReviewLinkEndpoints
    {
        public static WebApplication MapReviewLinkEndpoints(this WebApplication app)
        {
            var admin = app.MapGroup("/api/admin/review-link").RequireAuthorization(p => p.RequireRole("admin")).WithTags("Settings");

            admin.MapGet("/", async (AppDbContext db) =>
                await LoadOutlet(db) is { } outlet ? Results.Ok(new ReviewLinkDto(outlet.GetSettings().GoogleReviewUrl)) : Results.NotFound())
                .WithName("GetReviewLink").WithSummary("Google review link printed as a QR code on bills");

            admin.MapPut("/", async (ReviewLinkDto request, AppDbContext db) =>
            {
                var url = string.IsNullOrWhiteSpace(request.ReviewUrl) ? null : request.ReviewUrl.Trim();
                if (url != null && (url.Length > 300 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["reviewurl"] = new[] { "Paste the full https:// link of your Google review page." } });
                var outlet = await LoadOutlet(db);
                if (outlet == null) return Results.NotFound();
                var settings = outlet.GetSettings();
                settings.GoogleReviewUrl = url;
                outlet.Settings = settings.ToJson();
                await db.SaveChangesAsync();
                return Results.Ok(new ReviewLinkDto(url));
            }).WithName("SetReviewLink").WithSummary("Set or clear the Google review link (empty = no QR on bills)");

            // QR code of the review link as an SVG image, for the printed bill.
            app.MapGet("/api/review-qr.svg", async (AppDbContext db) =>
            {
                var url = (await LoadOutlet(db))?.GetSettings().GoogleReviewUrl;
                if (string.IsNullOrWhiteSpace(url)) return Results.NotFound();
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
                return Results.Content(new SvgQRCode(data).GetGraphic(6), "image/svg+xml");
            }).RequireAuthorization(p => p.RequireRole("cashier", "manager", "admin", "waiter"))
              .WithName("GetReviewQr").WithSummary("QR code (SVG) of the Google review link");

            return app;
        }

        private static Task<POSSystem.Models.Outlet?> LoadOutlet(AppDbContext db) =>
            db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);
    }
}
