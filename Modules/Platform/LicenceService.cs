using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public record RenewRequest(string Key, string DeviceId, string Version);
    // Support carries the support details from the cloud, so a shop PC shows the same footer and About page.
    public record RenewResponse(LicenceFile Licence, DateTime ServerTimeUtc, SupportInfoDto? Support = null);
    public record ActivateRequest(string? Key);
    public record LicenceStatusDto(string State, bool Locked, string? Customer, string? Plan, DateTime? LeaseUntil, DateTime? PaidUntil, string Message, bool Required);

    // Talks to the licence server in the cloud. The response is null (with a reason) when it cannot be reached or says no.
    public interface ILicenceCloud
    {
        Task<(RenewResponse? Response, string? Error)> Renew(RenewRequest request, CancellationToken ct);
    }

    public class HttpLicenceCloud : ILicenceCloud
    {
        private readonly HttpClient _http;
        private readonly string _url;

        public HttpLicenceCloud(HttpClient http, IConfiguration config)
        {
            _http = http;
            _url = (config["Licence:ServerUrl"] ?? "").TrimEnd('/');
        }

        public async Task<(RenewResponse? Response, string? Error)> Renew(RenewRequest request, CancellationToken ct)
        {
            if (_url.Length == 0) return (null, "The licence server address is not set up on this PC.");
            try
            {
                using var res = await _http.PostAsJsonAsync(_url + "/api/licence/renew", request, ct);
                if (res.IsSuccessStatusCode) return (await res.Content.ReadFromJsonAsync<RenewResponse>(ct), null);

                string? message = null;
                try
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("message", out var m)) message = m.GetString();
                }
                catch (JsonException) { }
                return (null, message ?? $"The licence server answered {(int)res.StatusCode}.");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                return (null, "Could not reach the licence server. Check the internet connection.");
            }
        }
    }

    // Keeps the licence of this PC and tells whether the PC is locked. Does nothing unless Licence:Required is true.
    public class LicenceService
    {
        private sealed record Cached(LicenceStatus Status, DateTime At);

        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILicenceCloud _cloud;
        private readonly IConfiguration _config;
        private readonly TimeProvider _time;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private volatile Cached? _cache;
        private ECDsa? _publicKey;

        public LicenceService(IServiceScopeFactory scopes, ILicenceCloud cloud, IConfiguration config, TimeProvider time)
        {
            _scopes = scopes;
            _cloud = cloud;
            _config = config;
            _time = time;
        }

        public bool Required => string.Equals(_config["Licence:Required"], "true", StringComparison.OrdinalIgnoreCase);

        private static string AppVersion =>
            Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

        public LicenceStatusDto Describe(LicenceStatus s) =>
            new(s.State.ToString(), s.Locked, s.Customer, s.Plan, s.LeaseUntil, s.PaidUntil, s.Message, Required);

        public async Task<LicenceStatus> GetStatus(CancellationToken ct = default)
        {
            if (!Required) return new LicenceStatus(LicenceState.Active, false, null, null, null, null, "Licence not required.");

            var cached = _cache;
            // Only a fresh answer is reused. A clock set back makes the age negative, so the licence is checked again at once.
            var age = cached is null ? CacheFor : _time.GetUtcNow().UtcDateTime - cached.At;
            if (cached is not null && age >= TimeSpan.Zero && age < CacheFor) return cached.Status;

            return await InRecord(async (db, record, now) =>
            {
                var status = Evaluate(record, now);
                // Remember the latest time this PC has seen, so a clock set back later is noticed. Once a minute is enough.
                if (now - record.LastSeenUtc >= TouchEvery)
                {
                    record.LastSeenUtc = now;
                    await db.SaveChangesAsync(ct);
                }
                _cache = new Cached(status, now);
                return status;
            }, ct);
        }

        // A short code for this PC, so support can tell shop PCs apart on the phone.
        public Task<string> DeviceCode(CancellationToken ct) =>
            InRecord((_, record, _) => Task.FromResult(record.DeviceId.Length > 8 ? record.DeviceId[..8].ToUpperInvariant() : record.DeviceId.ToUpperInvariant()), ct);

        public Task<LicenceStatus> Activate(string? key, CancellationToken ct)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Enter the licence key.");
            return Renew(key, ct);
        }

        public async Task<LicenceStatus> RenewStored(CancellationToken ct)
        {
            var key = await InRecord((_, record, _) => Task.FromResult(record.LicenceKey), ct);
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("This PC has no licence key yet. Enter the licence key first.");
            return await Renew(key, ct);
        }

        private async Task<LicenceStatus> Renew(string key, CancellationToken ct)
        {
            var deviceId = await InRecord((_, record, _) => Task.FromResult(record.DeviceId), ct);
            // No lock while waiting for the network, so the screens never wait for the licence server.
            var (response, error) = await _cloud.Renew(new RenewRequest(key, deviceId, AppVersion), ct);

            await InRecord(async (db, record, now) =>
            {
                var publicKey = PublicKey();
                var payload = response is null || publicKey is null ? null : Licence.Verify(response.Licence, publicKey);
                if (payload is null)
                {
                    record.LastRenewError = Short(response is null
                        ? error ?? "The licence server did not answer."
                        : "The licence server sent a licence this PC cannot trust.");
                    await db.SaveChangesAsync(ct);
                    _cache = null;
                    throw new ArgumentException(record.LastRenewError);
                }

                record.LicenceKey = key;
                record.Payload = response!.Licence.Payload;
                record.Signature = response.Licence.Signature;
                record.LastRenewedUtc = now;
                record.LastRenewError = null;
                // The support details from the cloud, for the footer and the About page on this PC.
                if (response.Support is not null && SupportService.Validate(response.Support).Count == 0)
                    SupportService.Apply(db, SupportService.Clean(response.Support), now);
                // The cloud's clock is the truth. When this PC agrees with it, forget an older "last seen" time
                // that came from a clock that was wrong once; otherwise the PC could never unlock itself.
                if ((response.ServerTimeUtc.ToUniversalTime() - now).Duration() <= Licence.ClockSlack) record.LastSeenUtc = now;
                await db.SaveChangesAsync(ct);
                _cache = null;
                return 0;
            }, ct);

            return await GetStatus(ct);
        }

        private LicenceStatus Evaluate(LicenceRecord record, DateTime now)
        {
            var key = PublicKey();
            if (key is null)
                return new LicenceStatus(LicenceState.Invalid, true, null, null, null, null, "This installation has no licence key set up. Contact support.");
            var file = record.Payload is null ? null : new LicenceFile(record.Payload, record.Signature ?? "");
            return Licence.Evaluate(file, key, now, record.LastSeenUtc);
        }

        private ECDsa? PublicKey()
        {
            if (_publicKey is not null) return _publicKey;
            var text = _config["Licence:PublicKey"];
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return _publicKey = Licence.PublicKeyFromBase64(text); }
            catch (Exception e) when (e is FormatException or CryptographicException) { return null; }
        }

        // One thing at a time touches the licence row.
        private async Task<T> InRecord<T>(Func<AppDbContext, LicenceRecord, DateTime, Task<T>> work, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = _time.GetUtcNow().UtcDateTime;
                var record = await db.LicenceRecords.FirstOrDefaultAsync(x => x.Id == 1, ct);
                if (record is null)
                {
                    record = new LicenceRecord { Id = 1, DeviceId = Guid.NewGuid().ToString("N"), LastSeenUtc = now };
                    db.LicenceRecords.Add(record);
                    await db.SaveChangesAsync(ct);
                }
                return await work(db, record, now);
            }
            finally
            {
                _gate.Release();
            }
        }

        private static string Short(string text) => text.Length <= 300 ? text : text[..300];
    }
}
