using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace POSSystem.Platform
{
    // A one-time code support reads to a shop owner who forgot the password. Only its hash travels to the shop PC.
    public record OwnerResetCodeDto(string Code, DateTime ExpiresAt);

    public record OwnerResetInfoDto(string Hash, DateTime ExpiresAt);

    public record OwnerResetRequest(string? Email, string? Code, string? NewPassword);

    public static class OwnerReset
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

        // Spaces, dashes and small letters do not matter when the owner types the code.
        public static string Normalize(string? code) =>
            new string((code ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

        public static string Hash(string? code) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("pos-owner-reset:" + Normalize(code))));

        public static bool Matches(string? code, string? hash) =>
            hash is not null && hash.Length == 64
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(code)), Encoding.ASCII.GetBytes(hash));
    }
}
