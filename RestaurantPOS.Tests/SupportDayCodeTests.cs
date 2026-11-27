using System;
using System.Security.Cryptography;
using System.Text.Json;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // SUP2: the support code of the day, made from a secret inside the signed lease. Works only today (India time) and only on that PC.
    public class SupportDayCodeTests
    {
        private const string PrivateKeyText = "test-private-key";
        private static readonly DateOnly Day = new(2026, 11, 27);
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        // What a v2.3.2 shop PC reads: it must still accept a new lease that has the extra field.
        private record OldPayload(string LicenceId, string Customer, string Plan, DateTime IssuedAt, DateTime LeaseUntil, DateTime PaidUntil);

        [Fact]
        public void Secret_IsTheSameEachTime_AndDiffersPerPcLicenceAndKey()
        {
            var a = SupportDayCode.SecretFor(PrivateKeyText, 1, "dev-1");
            Assert.Equal(a, SupportDayCode.SecretFor(PrivateKeyText, 1, "dev-1"));
            Assert.Equal(a, SupportDayCode.SecretFor("  " + PrivateKeyText + "\n", 1, "dev-1"));
            Assert.NotEqual(a, SupportDayCode.SecretFor(PrivateKeyText, 1, "dev-2"));
            Assert.NotEqual(a, SupportDayCode.SecretFor(PrivateKeyText, 2, "dev-1"));
            Assert.NotEqual(a, SupportDayCode.SecretFor("another-key", 1, "dev-1"));
            Assert.Equal(32, Convert.FromBase64String(a).Length);
        }

        [Fact]
        public void Code_HasEightReadableCharactersWithADash()
        {
            var code = SupportDayCode.CodeFor(SupportDayCode.SecretFor(PrivateKeyText, 1, "dev-1"), "dev-1", Day);
            Assert.Matches("^[ABCDEFGHJKMNPQRSTVWXYZ23456789]{4}-[ABCDEFGHJKMNPQRSTVWXYZ23456789]{4}$", code);
        }

        [Fact]
        public void Code_WorksOnlyToday_AndOnlyOnThatPc()
        {
            var secret = SupportDayCode.SecretFor(PrivateKeyText, 1, "dev-1");
            var code = SupportDayCode.CodeFor(secret, "dev-1", Day);
            Assert.True(SupportDayCode.Matches(code, secret, "dev-1", Day));
            Assert.True(SupportDayCode.Matches(" " + code.ToLowerInvariant().Replace("-", "") + " ", secret, "dev-1", Day));
            Assert.False(SupportDayCode.Matches(code, secret, "dev-1", Day.AddDays(1)));
            Assert.False(SupportDayCode.Matches(code, secret, "dev-1", Day.AddDays(-1)));
            Assert.False(SupportDayCode.Matches(code, secret, "dev-2", Day));
            Assert.NotEqual(code, SupportDayCode.CodeFor(secret, "dev-1", Day.AddDays(1)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ABCD-EFGH-JKMN-PQRS-TVWX")]
        public void Matches_RefusesEmptyOrTooLongCodes(string? code)
        {
            var secret = SupportDayCode.SecretFor(PrivateKeyText, 1, "dev-1");
            Assert.False(SupportDayCode.Matches(code, secret, "dev-1", Day));
        }

        [Fact]
        public void Matches_RefusesAMissingOrBrokenSecret()
        {
            var code = SupportDayCode.CodeFor(SupportDayCode.SecretFor(PrivateKeyText, 1, "dev-1"), "dev-1", Day);
            Assert.False(SupportDayCode.Matches(code, null, "dev-1", Day));
            Assert.False(SupportDayCode.Matches(code, "", "dev-1", Day));
            Assert.False(SupportDayCode.Matches(code, "not base64 !!", "dev-1", Day));
        }

        [Fact]
        public void Today_UsesIndiaTime()
        {
            Assert.Equal(new DateOnly(2026, 11, 27), SupportDayCode.Today(new DateTime(2026, 11, 27, 18, 29, 0, DateTimeKind.Utc)));
            Assert.Equal(new DateOnly(2026, 11, 28), SupportDayCode.Today(new DateTime(2026, 11, 27, 18, 30, 0, DateTimeKind.Utc)));
        }

        [Fact]
        public void OldLeaseWithoutASecret_IsStillValid()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var now = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);
            // Exactly what v2.3.2 signed: no supportSecret field.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { licenceId = "7", customer = "Old Shop", plan = "yearly", issuedAt = now, leaseUntil = now.AddDays(7), paidUntil = now.AddDays(300) }, Web);
            var file = new LicenceFile(Convert.ToBase64String(bytes), Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256)));
            var payload = Licence.Verify(file, key);
            Assert.NotNull(payload);
            Assert.Null(payload!.SupportSecret);
            Assert.Equal(LicenceState.Active, Licence.Evaluate(file, key, now.AddHours(1), now).State);
        }

        [Fact]
        public void NewLease_CarriesTheSecret_OldPcsStillReadIt_AndAChangedSecretBreaksTheSignature()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var now = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);
            var secret = SupportDayCode.SecretFor(PrivateKeyText, 7, "dev-1");
            var file = Licence.Sign(new LicencePayload("7", "New Shop", "yearly", now, now.AddDays(7), now.AddDays(300), secret), key);
            Assert.Equal(secret, Licence.Verify(file, key)!.SupportSecret);
            Assert.Equal(LicenceState.Active, Licence.Evaluate(file, key, now.AddHours(1), now).State);

            var old = JsonSerializer.Deserialize<OldPayload>(Convert.FromBase64String(file.Payload), Web);
            Assert.NotNull(old);
            Assert.Equal("New Shop", old!.Customer);

            var changed = Licence.Verify(file, key)! with { SupportSecret = SupportDayCode.SecretFor(PrivateKeyText, 7, "dev-2") };
            var forged = new LicenceFile(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(changed, Web)), file.Signature);
            Assert.Null(Licence.Verify(forged, key));
        }
    }
}
