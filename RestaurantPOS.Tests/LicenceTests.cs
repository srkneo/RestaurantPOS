using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class LicenceTests : IDisposable
    {
        private static readonly DateTime Now = new(2026, 12, 1, 6, 0, 0, DateTimeKind.Utc);
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public void Dispose() => _key.Dispose();

        private LicenceFile Make(ECDsa? signer = null, int leaseDays = 7, int paidDays = 300) =>
            Licence.Sign(new LicencePayload("L1", "Anand Restaurant", "yearly", Now, Now.AddDays(leaseDays), Now.AddDays(paidDays)), signer ?? _key);

        [Fact]
        public void ValidLicence_IsActive()
        {
            var status = Licence.Evaluate(Make(), _key, Now, Now);
            Assert.Equal(LicenceState.Active, status.State);
            Assert.False(status.Locked);
            Assert.Equal("Anand Restaurant", status.Customer);
        }

        [Fact]
        public void NoLicence_IsLocked()
        {
            var status = Licence.Evaluate(null, _key, Now, null);
            Assert.Equal(LicenceState.Missing, status.State);
            Assert.True(status.Locked);
        }

        [Fact]
        public void ChangedPayload_IsInvalid()
        {
            var good = Make();
            // A longer paid period, but the old signature.
            var forged = new LicencePayload("L1", "Anand Restaurant", "yearly", Now, Now.AddDays(7), Now.AddDays(3000));
            var file = new LicenceFile(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(forged, new JsonSerializerOptions(JsonSerializerDefaults.Web))), good.Signature);
            var status = Licence.Evaluate(file, _key, Now, Now);
            Assert.Equal(LicenceState.Invalid, status.State);
            Assert.True(status.Locked);
        }

        [Fact]
        public void SignedWithAnotherKey_IsInvalid()
        {
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var status = Licence.Evaluate(Make(other), _key, Now, Now);
            Assert.Equal(LicenceState.Invalid, status.State);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not base64 !!")]
        [InlineData("e30=")] // {} as base64
        public void Garbage_IsInvalid_AndNeverThrows(string text)
        {
            var status = Licence.Evaluate(new LicenceFile(text, text), _key, Now, Now);
            Assert.Equal(LicenceState.Invalid, status.State);
            Assert.True(status.Locked);
        }

        [Fact]
        public void LeaseRunOut_LocksUntilOnline()
        {
            var status = Licence.Evaluate(Make(leaseDays: 7), _key, Now.AddDays(7).AddHours(1), Now);
            Assert.Equal(LicenceState.LeaseExpired, status.State);
            Assert.True(status.Locked);
            Assert.Contains("internet", status.Message);
        }

        [Fact]
        public void ExactlyAtLeaseEnd_IsStillOpen()
        {
            var status = Licence.Evaluate(Make(leaseDays: 7), _key, Now.AddDays(7), Now);
            Assert.False(status.Locked);
        }

        [Fact]
        public void PaidPeriodOver_Locks_EvenWithLeaseLeft()
        {
            var status = Licence.Evaluate(Make(leaseDays: 7, paidDays: 3), _key, Now.AddDays(4), Now);
            Assert.Equal(LicenceState.PaidUntilExpired, status.State);
            Assert.True(status.Locked);
            Assert.Contains("renew", status.Message);
        }

        [Fact]
        public void ClockSetBack_Locks()
        {
            var status = Licence.Evaluate(Make(), _key, Now, Now.AddDays(1));
            Assert.Equal(LicenceState.ClockBack, status.State);
            Assert.True(status.Locked);
        }

        [Fact]
        public void SmallClockDrift_IsFine()
        {
            Assert.False(Licence.Evaluate(Make(), _key, Now, Now.AddMinutes(5)).Locked);
        }

        [Fact]
        public void ClockBeforeIssueTime_Locks()
        {
            var status = Licence.Evaluate(Make(), _key, Now.AddDays(-1), null);
            Assert.Equal(LicenceState.ClockBack, status.State);
        }

        [Fact]
        public void PaidPeriodEndingWithin14Days_Warns_ButStaysOpen()
        {
            var status = Licence.Evaluate(Make(paidDays: 10), _key, Now, Now);
            Assert.Equal(LicenceState.ExpiringSoon, status.State);
            Assert.False(status.Locked);
            Assert.Contains("renew", status.Message);
        }

        [Fact]
        public void LeaseEndingWithin2Days_Warns_ButStaysOpen()
        {
            var status = Licence.Evaluate(Make(leaseDays: 7), _key, Now.AddDays(5.5), Now);
            Assert.Equal(LicenceState.ExpiringSoon, status.State);
            Assert.False(status.Locked);
            Assert.Contains("internet", status.Message);
        }

        [Fact]
        public void Keys_CanBeSavedAsText_AndUsedAgain()
        {
            using var publicKey = Licence.PublicKeyFromBase64(Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()) + "\n");
            using var privateKey = Licence.PrivateKeyFromBase64(Convert.ToBase64String(_key.ExportPkcs8PrivateKey()));
            var file = Licence.Sign(new LicencePayload("L2", "Test", "trial", Now, Now.AddDays(7), Now.AddDays(30)), privateKey);
            Assert.NotNull(Licence.Verify(file, publicKey));
        }
    }
}
