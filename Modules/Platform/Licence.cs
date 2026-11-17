using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace POSSystem.Platform
{
    // What the cloud signs for one shop PC. PaidUntil is the end of the paid period (a trial is 30 days);
    // LeaseUntil is a short lease (7 days) that the shop PC renews from the cloud whenever it is online.
    public record LicencePayload(string LicenceId, string Customer, string Plan, DateTime IssuedAt, DateTime LeaseUntil, DateTime PaidUntil);

    // The file kept on the shop PC: the payload (base64 of JSON) and the signature over exactly those bytes (base64).
    public record LicenceFile(string Payload, string Signature);

    public enum LicenceState { Missing, Invalid, Active, ExpiringSoon, LeaseExpired, PaidUntilExpired, ClockBack }

    public record LicenceStatus(LicenceState State, bool Locked, string? Customer, string? Plan, DateTime? LeaseUntil, DateTime? PaidUntil, string Message);

    public static class Licence
    {
        // A little room so a clock that drifts by a few minutes never locks a shop.
        public static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan LeaseWarning = TimeSpan.FromDays(2);
        public static readonly TimeSpan PaidWarning = TimeSpan.FromDays(14);

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static LicenceFile Sign(LicencePayload payload, ECDsa privateKey)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
            var signature = privateKey.SignData(bytes, HashAlgorithmName.SHA256);
            return new LicenceFile(Convert.ToBase64String(bytes), Convert.ToBase64String(signature));
        }

        // Null when the file is empty, damaged, changed after signing, or signed with another key. Never throws.
        public static LicencePayload? Verify(LicenceFile? file, ECDsa publicKey)
        {
            if (file is null || string.IsNullOrEmpty(file.Payload) || string.IsNullOrEmpty(file.Signature)) return null;
            try
            {
                var bytes = Convert.FromBase64String(file.Payload);
                var signature = Convert.FromBase64String(file.Signature);
                if (!publicKey.VerifyData(bytes, signature, HashAlgorithmName.SHA256)) return null;
                return JsonSerializer.Deserialize<LicencePayload>(bytes, Json);
            }
            catch (Exception e) when (e is FormatException or CryptographicException or JsonException)
            {
                return null;
            }
        }

        // lastSeenUtc is the latest time this PC has ever seen (kept in the database). A clock set back behind it is caught here.
        public static LicenceStatus Evaluate(LicenceFile? file, ECDsa publicKey, DateTime nowUtc, DateTime? lastSeenUtc)
        {
            if (file is null)
                return new LicenceStatus(LicenceState.Missing, true, null, null, null, null, "This PC has no licence yet. Enter the licence key.");

            var payload = Verify(file, publicKey);
            if (payload is null)
                return new LicenceStatus(LicenceState.Invalid, true, null, null, null, null, "The licence is not valid. Contact support.");

            var issued = Utc(payload.IssuedAt);
            var lease = Utc(payload.LeaseUntil);
            var paid = Utc(payload.PaidUntil);
            LicenceStatus Make(LicenceState state, bool locked, string message) =>
                new(state, locked, payload.Customer, payload.Plan, lease, paid, message);

            if (nowUtc < issued - ClockSlack || (lastSeenUtc is { } seen && nowUtc < Utc(seen) - ClockSlack))
                return Make(LicenceState.ClockBack, true, "The PC clock is wrong. Set the correct date and time, then connect to the internet.");

            if (nowUtc > paid)
                return Make(LicenceState.PaidUntilExpired, true, $"The licence ended on {Day(paid)}. Contact support to renew.");

            if (nowUtc > lease)
                return Make(LicenceState.LeaseExpired, true, "This PC has been offline for too long. Connect it to the internet to refresh the licence.");

            if (paid - nowUtc <= PaidWarning)
                return Make(LicenceState.ExpiringSoon, false, $"The licence ends on {Day(paid)}. Contact support to renew.");

            if (lease - nowUtc <= LeaseWarning)
                return Make(LicenceState.ExpiringSoon, false, "Connect this PC to the internet soon to refresh the licence.");

            return Make(LicenceState.Active, false, "Licence active.");
        }

        public static ECDsa PublicKeyFromBase64(string base64)
        {
            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64.Trim()), out _);
            return key;
        }

        public static ECDsa PrivateKeyFromBase64(string base64)
        {
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(base64.Trim()), out _);
            return key;
        }

        private static DateTime Utc(DateTime value) =>
            value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

        private static string Day(DateTime value) => value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
