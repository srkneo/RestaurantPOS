using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public record ShopLicenceDto(int Id, string Key, string Customer, string Plan, string State, DateTime PaidUntil, bool Revoked,
        bool DeviceBound, DateTime? LastSeenAt, string? LastVersion, DateTime CreatedAt);

    public record CreateShopLicenceRequest(string? Customer, string? Plan, int? Days);

    public record ExtendShopLicenceRequest(int Days, string? Plan);

    // The licence server says no (stopped, ended, or another PC uses the key): 403.
    public class LicenceRefusedException : Exception
    {
        public LicenceRefusedException(string message) : base(message) { }
    }

    // Creates and manages shop licences, and signs the leases shop PCs ask for. Signing needs Licence:PrivateKey (cloud server only).
    public class LicenceIssuer
    {
        public const int TrialDays = 30;
        public const int YearDays = 365;
        public const int LeaseDays = 7;
        public const int MaxDays = 1100;
        public static readonly string[] Plans = { "trial", "yearly" };

        // No 0, O, 1, I, L: keys are read out over the phone.
        private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly TimeProvider _time;

        public LicenceIssuer(AppDbContext db, IConfiguration config, TimeProvider time)
        {
            _db = db;
            _config = config;
            _time = time;
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        public static Dictionary<string, string[]> Validate(CreateShopLicenceRequest request)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Customer)) errors["customer"] = new[] { "Enter the customer name." };
            else if (request.Customer.Trim().Length > 200) errors["customer"] = new[] { "The name is too long." };
            if (request.Plan is not null && !Plans.Contains(request.Plan.Trim().ToLowerInvariant())) errors["plan"] = new[] { "Plan must be trial or yearly." };
            if (request.Days is < 1 or > MaxDays) errors["days"] = new[] { $"Days must be between 1 and {MaxDays}." };
            return errors;
        }

        public ShopLicenceDto Describe(ShopLicence l)
        {
            var now = Now;
            var state = l.Revoked ? "Revoked" : now > l.PaidUntil ? "Expired" : l.Plan == "trial" ? "Trial" : "Active";
            return new ShopLicenceDto(l.Id, l.Key, l.Customer, l.Plan, state, l.PaidUntil, l.Revoked, l.DeviceId is not null, l.LastSeenAt, l.LastVersion, l.CreatedAt);
        }

        public async Task<List<ShopLicenceDto>> List(CancellationToken ct) =>
            (await _db.ShopLicences.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync(ct)).Select(Describe).ToList();

        public async Task<ShopLicenceDto> Create(CreateShopLicenceRequest request, CancellationToken ct)
        {
            var plan = (request.Plan ?? "trial").Trim().ToLowerInvariant();
            var days = request.Days ?? (plan == "trial" ? TrialDays : YearDays);
            var now = Now;
            var licence = new ShopLicence
            {
                Key = NewKey(), Customer = request.Customer!.Trim(), Plan = plan,
                PaidUntil = now.AddDays(days), CreatedAt = now
            };
            _db.ShopLicences.Add(licence);
            await _db.SaveChangesAsync(ct);
            return Describe(licence);
        }

        // Adds days to the end of the paid period (or to today when it has already ended).
        public async Task<ShopLicenceDto> Extend(int id, ExtendShopLicenceRequest request, CancellationToken ct)
        {
            if (request.Days is < 1 or > MaxDays) throw new ArgumentException($"Days must be between 1 and {MaxDays}.");
            if (request.Plan is not null && !Plans.Contains(request.Plan.Trim().ToLowerInvariant())) throw new ArgumentException("Plan must be trial or yearly.");
            var licence = await Find(id, ct);
            var now = Now;
            licence.PaidUntil = (licence.PaidUntil > now ? licence.PaidUntil : now).AddDays(request.Days);
            if (request.Plan is not null) licence.Plan = request.Plan.Trim().ToLowerInvariant();
            await _db.SaveChangesAsync(ct);
            return Describe(licence);
        }

        public async Task<ShopLicenceDto> SetRevoked(int id, bool revoked, CancellationToken ct)
        {
            var licence = await Find(id, ct);
            licence.Revoked = revoked;
            await _db.SaveChangesAsync(ct);
            return Describe(licence);
        }

        // For a shop that moved to a new PC.
        public async Task<ShopLicenceDto> ResetDevice(int id, CancellationToken ct)
        {
            var licence = await Find(id, ct);
            licence.DeviceId = null;
            await _db.SaveChangesAsync(ct);
            return Describe(licence);
        }

        // A shop PC asks for a fresh lease. The lease never reaches past the paid period.
        public async Task<RenewResponse> Renew(RenewRequest request, CancellationToken ct)
        {
            var privateKeyText = _config["Licence:PrivateKey"];
            if (string.IsNullOrWhiteSpace(privateKeyText)) throw new KeyNotFoundException("The licence server is not switched on here.");

            var key = (request.Key ?? "").Trim().ToUpperInvariant();
            var deviceId = (request.DeviceId ?? "").Trim();
            if (deviceId.Length is 0 or > 64) throw new ArgumentException("The device id is missing.");

            var licence = await _db.ShopLicences.FirstOrDefaultAsync(x => x.Key == key, ct)
                ?? throw new KeyNotFoundException("This licence key is not known. Check the key and try again.");
            if (licence.Revoked) throw new LicenceRefusedException("This licence has been stopped. Contact support.");

            var now = Now;
            if (now > licence.PaidUntil)
                throw new LicenceRefusedException($"This licence ended on {licence.PaidUntil.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}. Contact support to renew.");

            if (licence.DeviceId is null) licence.DeviceId = deviceId;
            else if (licence.DeviceId != deviceId) throw new LicenceRefusedException("This licence key is already used on another PC. Contact support.");

            licence.LastSeenAt = now;
            var version = request.Version ?? "";
            licence.LastVersion = version.Length <= 40 ? version : version[..40];
            await _db.SaveChangesAsync(ct);

            var lease = now.AddDays(LeaseDays);
            var payload = new LicencePayload(licence.Id.ToString(), licence.Customer, licence.Plan, now, lease < licence.PaidUntil ? lease : licence.PaidUntil, licence.PaidUntil);
            using var privateKey = Licence.PrivateKeyFromBase64(privateKeyText);
            return new RenewResponse(Licence.Sign(payload, privateKey), now);
        }

        private async Task<ShopLicence> Find(int id, CancellationToken ct) =>
            await _db.ShopLicences.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Licence not found.");

        private static string NewKey()
        {
            var bytes = RandomNumberGenerator.GetBytes(20);
            var chars = bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray();
            return "POS-" + string.Join("-", Enumerable.Range(0, 4).Select(i => new string(chars, i * 5, 5)));
        }
    }
}
