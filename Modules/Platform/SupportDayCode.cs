using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace POSSystem.Platform
{
    // A class to generate and validate support day codes.
    public static class SupportDayCode
    {
        // The time offset for India.
        public static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

        // The alphabet used for generating the codes.
        private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

        // Generates a secret for a given private key, licence ID, and device ID.
        public static string SecretFor(string privateKeyText, int licenceId, string deviceId)
        {
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(privateKeyText.Trim()));
            var data = Encoding.UTF8.GetBytes("support-day|" + licenceId.ToString(CultureInfo.InvariantCulture) + "|" + deviceId);
            return Convert.ToBase64String(HMACSHA256.HashData(key, data));
        }

        // Gets the current date in India's timezone.
        public static DateOnly Today(DateTime utcNow)
        {
            var utc = utcNow.Kind == DateTimeKind.Local ? utcNow.ToUniversalTime() : utcNow;
            return DateOnly.FromDateTime(utc.Add(IndiaOffset));
        }

        // Generates a code for a given secret, device ID, and date.
        public static string CodeFor(string secret, string deviceId, DateOnly day)
        {
            var hash = HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(deviceId + "|" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            var chars = new char[8];
            for (int i = 0; i < 8; i++)
            {
                chars[i] = Alphabet[hash[i] % Alphabet.Length];
            }
            return new string(chars, 0, 4) + "-" + new string(chars, 4, 4);
        }

        // Checks if a given code matches the expected code for a secret, device ID, and date.
        public static bool Matches(string? code, string? secret, string deviceId, DateOnly day)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(code))
                return false;

            var clean = code.Trim();
            if (clean.Length > 20)
                return false;

            clean = clean.Replace("-", "").Replace(" ", "").ToUpperInvariant();

            string expected;
            try
            {
                expected = CodeFor(secret, deviceId, day).Replace("-", "");
            }
            catch (FormatException)
            {
                return false;
            }

            var a = Encoding.UTF8.GetBytes(clean);
            var b = Encoding.UTF8.GetBytes(expected);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
