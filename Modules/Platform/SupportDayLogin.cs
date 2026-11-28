using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public record SupportDayLoginRequest(string? Code);

    public static class SupportDayLogin
    {
        public static WebApplication MapSupportDayLoginEndpoints(this WebApplication app)
        {
            // Cloud endpoint
            app.MapGet("/api/platform/licences/{id:int}/support-code", async (int id, AppDbContext db, IConfiguration config, TimeProvider time, CancellationToken ct) =>
            {
                var privateKeyText = config["Licence:PrivateKey"];
                if (string.IsNullOrWhiteSpace(privateKeyText))
                    return Results.NotFound(new { message = "The licence server is not switched on here." });

                var licence = await db.ShopLicences.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (licence == null)
                    return Results.NotFound(new { message = "Licence not found." });

                if (licence.Revoked)
                    return Results.BadRequest(new { message = "This licence has been stopped." });

                var deviceId = licence.DeviceId;
                if (string.IsNullOrEmpty(deviceId))
                    return Results.BadRequest(new { message = "This licence is not used on a PC yet." });

                var day = SupportDayCode.Today(time.GetUtcNow().UtcDateTime);
                var code = SupportDayCode.CodeFor(SupportDayCode.SecretFor(privateKeyText, licence.Id, deviceId), deviceId, day);
                var deviceCode = (deviceId.Length > 8 ? deviceId[..8] : deviceId).ToUpperInvariant();

                return Results.Ok(new { code, day = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), deviceCode });
            })
            .RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole))
            .WithTags("Platform")
            .WithName("GetSupportDayCode")
            .WithSummary("Today's support code for one shop PC");

            // Shop PC endpoint
            app.MapPost("/api/auth/support-day-login", async (SupportDayLoginRequest request, AppDbContext db, IConfiguration config, TimeProvider time, UserManager<AppUser> users, RoleManager<IdentityRole> roles, SignInManager<AppUser> signIn, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Code))
                    return Results.BadRequest(new { message = "Enter the code from support." });

                var record = await db.LicenceRecords.FirstOrDefaultAsync(x => x.Id == 1, ct);
                var publicKeyText = config["Licence:PublicKey"];
                LicencePayload? payload = null;

                if (record?.Payload is not null && !string.IsNullOrWhiteSpace(publicKeyText))
                {
                    try
                    {
                        using var key = Licence.PublicKeyFromBase64(publicKeyText);
                        payload = Licence.Verify(new LicenceFile(record.Payload, record.Signature ?? ""), key);
                    }
                    catch (Exception e) when (e is FormatException or CryptographicException)
                    {
                        payload = null;
                    }
                }

                var secret = payload?.SupportSecret;
                if (record is null || string.IsNullOrEmpty(secret))
                    return Results.BadRequest(new { message = "Day codes need a fresh licence on this PC. Connect it to the internet once, then try again." });

                var day = SupportDayCode.Today(time.GetUtcNow().UtcDateTime);
                if (!SupportDayCode.Matches(request.Code, secret, record.DeviceId, day))
                    return Results.BadRequest(new { message = "The code is wrong or not for today. Ask support for today's code." });

                var tenant = await db.Tenants.IgnoreQueryFilters().Where(t => t.IsActive).OrderBy(t => t.Id).FirstOrDefaultAsync(ct);
                var outlet = tenant is null ? null : await db.Outlets.IgnoreQueryFilters().Where(o => o.TenantId == tenant.Id).OrderBy(o => o.Id).FirstOrDefaultAsync(ct);

                if (tenant is null || outlet is null)
                    return Results.NotFound(new { message = "This PC has no business yet." });

                if (!outlet.GetSettings().AllowSupportAccess)
                    return Results.BadRequest(new { message = "This business has turned off support access." });

                if (!await roles.RoleExistsAsync(SupportAccess.Role))
                {
                    await roles.CreateAsync(new IdentityRole(SupportAccess.Role));
                }

                var email = SupportAccess.EmailFor(tenant.Id);
                var user = await users.FindByEmailAsync(email);

                if (user == null)
                {
                    user = new AppUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        DisplayName = "Support (platform)",
                        TenantId = tenant.Id,
                        OutletId = outlet.Id,
                        IsSupport = true
                    };
                    await users.CreateAsync(user);
                    await users.AddToRolesAsync(user, new[] { "admin", SupportAccess.Role });
                }
                else
                {
                    user.IsActive = true;
                    await users.UpdateAsync(user);
                }

                db.CurrentTenantId = tenant.Id;
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = tenant.Id,
                    Action = "SupportLogin",
                    EntityType = "Support",
                    Details = System.Text.Json.JsonSerializer.Serialize(new { support = "Support session started on this PC with the day code" })
                });
                await db.SaveChangesAsync(ct);

                await signIn.SignOutAsync();
                await signIn.SignInAsync(user, new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                    AllowRefresh = false
                });

                return Results.Ok(new { tenantId = tenant.Id });
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimits.Login)
            .WithTags("Auth")
            .WithName("SupportDayLogin")
            .WithSummary("Shop PC: support signs in for 2 hours with today's code");

            return app;
        }
    }
}
