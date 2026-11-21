using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using POSSystem.Models;

namespace POSSystem.Data
{
    public record TwoFactorStatusDto(bool Enabled, int RecoveryCodesLeft);
    public record TwoFactorSetupDto(string Key, string Uri, string QrSvg);
    public record TwoFactorCodeRequest(string? Code);
    public record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

    // 2-step sign-in with an authenticator app (free, works offline): after the password, a short-lived challenge waits for
    // the app's 6-digit code or one printed backup code. Wrong codes count towards the account lock like wrong passwords.
    public class AppTwoFactor
    {
        public const int Minutes = 5;
        public const int MaxTries = 5;
        private readonly IMemoryCache _cache;

        private sealed class Pending
        {
            public string UserId { get; init; } = string.Empty;
            public int Tries { get; set; }
        }

        public AppTwoFactor(IMemoryCache cache) => _cache = cache;

        private static string Key(string challenge) => "app2fa:" + challenge;

        public string Start(AppUser user)
        {
            var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            _cache.Set(Key(challenge), new Pending { UserId = user.Id }, TimeSpan.FromMinutes(Minutes));
            return challenge;
        }

        public bool Has(string? challenge) => !string.IsNullOrEmpty(challenge) && challenge.Length <= 64 && _cache.TryGetValue(Key(challenge), out _);

        // The app's code (6 digits) or a backup code.
        public static async Task<bool> CheckAsync(UserManager<AppUser> users, AppUser user, string? code, bool allowBackup)
        {
            var text = (code ?? string.Empty).Trim();
            if (text.Length == 0 || text.Length > 20) return false;
            var digits = text.Replace(" ", string.Empty);
            if (digits.Length == 6 && digits.All(char.IsDigit))
                return await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, digits);
            return allowBackup && (await users.RedeemTwoFactorRecoveryCodeAsync(user, text)).Succeeded;
        }

        public async Task<AppUser?> VerifyAsync(string? challenge, string? code, UserManager<AppUser> users)
        {
            if (string.IsNullOrEmpty(challenge) || !_cache.TryGetValue(Key(challenge), out Pending? pending) || pending == null) return null;
            var user = await users.FindByIdAsync(pending.UserId);
            if (user == null || !user.IsActive) { _cache.Remove(Key(challenge)); return null; }
            if (await users.IsLockedOutAsync(user)) { _cache.Remove(Key(challenge)); return null; }
            if (await CheckAsync(users, user, code, allowBackup: true))
            {
                _cache.Remove(Key(challenge));
                await users.ResetAccessFailedCountAsync(user);
                return user;
            }
            await users.AccessFailedAsync(user);
            pending.Tries++;
            if (pending.Tries >= MaxTries) _cache.Remove(Key(challenge));
            return null;
        }
    }

    public static class AppTwoFactorEndpoints
    {
        public const string Issuer = "Restaurant POS";
        // The platform owner too: with the app on, sign-in no longer depends on the Telegram code arriving.
        private static readonly string[] Roles = { "admin", "manager", "platform" };

        private static IResult Invalid(string message) =>
            Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = new[] { message } });

        private static async Task<AppUser?> Me(UserManager<AppUser> users, ClaimsPrincipal caller)
        {
            var id = caller.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrEmpty(id) ? null : await users.FindByIdAsync(id);
        }

        // ABCD EFGH ... so the key is easy to type into the app.
        private static string Group(string key) =>
            string.Join(" ", Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4))));

        public static WebApplication MapAppTwoFactorEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/auth/2fa").RequireAuthorization(p => p.RequireRole(Roles)).WithTags("Sign in");

            group.MapGet("/", async (ClaimsPrincipal caller, UserManager<AppUser> users) =>
                await Me(users, caller) is { } me
                    ? Results.Ok(new TwoFactorStatusDto(me.TwoFactorEnabled, me.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(me) : 0))
                    : Results.NotFound())
                .WithName("GetTwoFactor").WithSummary("Is 2-step sign-in with an authenticator app on for me?");

            group.MapPost("/setup", async (ClaimsPrincipal caller, UserManager<AppUser> users) =>
            {
                var me = await Me(users, caller);
                if (me == null) return Results.NotFound();
                if (me.IsDevice) return Invalid("A kitchen screen device cannot use an authenticator app.");
                if (me.TwoFactorEnabled) return Invalid("2-step sign-in is already on. Turn it off first to set it up again.");
                await users.ResetAuthenticatorKeyAsync(me);
                var key = await users.GetAuthenticatorKeyAsync(me) ?? string.Empty;
                var account = me.Email ?? me.UserName ?? "user";
                var uri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(account)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
                using var generator = new QRCoder.QRCodeGenerator();
                using var data = generator.CreateQrCode(uri, QRCoder.QRCodeGenerator.ECCLevel.M);
                return Results.Ok(new TwoFactorSetupDto(Group(key), uri, new QRCoder.SvgQRCode(data).GetGraphic(6)));
            }).WithName("SetUpTwoFactor").WithSummary("A new key and QR code for the authenticator app (not on until confirmed)");

            group.MapPost("/enable", async (TwoFactorCodeRequest request, ClaimsPrincipal caller, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
            {
                var me = await Me(users, caller);
                if (me == null) return Results.NotFound();
                if (me.TwoFactorEnabled) return Invalid("2-step sign-in is already on.");
                if (string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(me))) return Invalid("Press Turn on first to get the QR code.");
                if (!await AppTwoFactor.CheckAsync(users, me, request.Code, allowBackup: false))
                    return Invalid("That code is not right. Type the 6 digits the app shows now.");
                await users.SetTwoFactorEnabledAsync(me, true);
                var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(me, 10))?.ToList() ?? new List<string>();
                await signIn.RefreshSignInAsync(me);
                return Results.Ok(new RecoveryCodesDto(codes));
            }).WithName("EnableTwoFactor").WithSummary("Confirm the app's code: 2-step sign-in is on; returns 10 backup codes");

            group.MapPost("/recovery-codes", async (TwoFactorCodeRequest request, ClaimsPrincipal caller, UserManager<AppUser> users) =>
            {
                var me = await Me(users, caller);
                if (me == null) return Results.NotFound();
                if (!me.TwoFactorEnabled) return Invalid("2-step sign-in is off.");
                if (!await AppTwoFactor.CheckAsync(users, me, request.Code, allowBackup: false))
                    return Invalid("That code is not right. Type the 6 digits the app shows now.");
                var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(me, 10))?.ToList() ?? new List<string>();
                return Results.Ok(new RecoveryCodesDto(codes));
            }).WithName("NewRecoveryCodes").WithSummary("10 new backup codes (the old ones stop working)");

            group.MapPost("/disable", async (TwoFactorCodeRequest request, ClaimsPrincipal caller, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
            {
                var me = await Me(users, caller);
                if (me == null) return Results.NotFound();
                if (!me.TwoFactorEnabled) return Invalid("2-step sign-in is already off.");
                if (!await AppTwoFactor.CheckAsync(users, me, request.Code, allowBackup: true))
                    return Invalid("That code is not right. Type the 6 digits the app shows now, or a backup code.");
                await users.SetTwoFactorEnabledAsync(me, false);
                await users.ResetAuthenticatorKeyAsync(me);
                await signIn.RefreshSignInAsync(me);
                return Results.NoContent();
            }).WithName("DisableTwoFactor").WithSummary("Turn 2-step sign-in off (needs the app's code or a backup code)");

            return app;
        }
    }
}
