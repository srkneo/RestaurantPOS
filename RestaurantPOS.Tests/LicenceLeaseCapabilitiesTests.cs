using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // C5: the shop PC lease carries the capabilities inside the signed payload. Leases signed before C5 have no list and keep every capability.
    public class LicenceLeaseCapabilitiesTests : IDisposable
    {
        private static readonly DateTime Now = DateTime.UtcNow;
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public void Dispose() => _key.Dispose();

        private static LicencePayload Payload(string[]? capabilities) =>
            new LicencePayload("L1", "Anand Restaurant", "yearly", Now, Now.AddDays(7), Now.AddDays(300), null, capabilities);

        [Fact]
        public void SignedLease_CarriesTheList_InRegistryOrder()
        {
            var file = Licence.Sign(Payload(new[] { "stock", "tables" }), _key);
            var payload = Licence.Verify(file, _key);
            Assert.NotNull(payload);
            Assert.Equal(new[] { "stock", "tables" }, payload!.Capabilities);
            Assert.Equal(new[] { "tables", "stock" }, Licence.CapabilitiesOf(payload).ToArray());
            Assert.False(Licence.Evaluate(file, _key, Now, null).Locked);
        }

        [Fact]
        public void UnknownKeys_AndDuplicates_AreDropped()
        {
            var payload = Payload(new[] { "night-club", "stock", "tables", "stock", "" });
            Assert.Equal(new[] { "tables", "stock" }, Licence.CapabilitiesOf(payload).ToArray());
        }

        [Fact]
        public void EmptyList_AllowsNothing()
        {
            Assert.Empty(Licence.CapabilitiesOf(Payload(Array.Empty<string>())));
        }

        [Fact]
        public void NoPayload_OrNoList_AllowsEverything()
        {
            Assert.Equal(Capabilities.Keys.ToArray(), Licence.CapabilitiesOf(null).ToArray());
            Assert.Equal(Capabilities.Keys.ToArray(), Licence.CapabilitiesOf(Payload(null)).ToArray());
        }

        [Fact]
        public void OldLease_WithoutTheField_StaysValid_AndKeepsEverything()
        {
            // The JSON a cloud server before C5 signed: no capabilities field at all.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                licenceId = "L1", customer = "Old Shop", plan = "yearly", issuedAt = Now, leaseUntil = Now.AddDays(7), paidUntil = Now.AddDays(300)
            }, Web);
            Assert.DoesNotContain("capabilities", Encoding.UTF8.GetString(bytes));
            var file = new LicenceFile(Convert.ToBase64String(bytes), Convert.ToBase64String(_key.SignData(bytes, HashAlgorithmName.SHA256)));

            var payload = Licence.Verify(file, _key);
            Assert.NotNull(payload);
            Assert.Null(payload!.Capabilities);
            Assert.Equal(Capabilities.Keys.ToArray(), Licence.CapabilitiesOf(payload).ToArray());
            var status = Licence.Evaluate(file, _key, Now, null);
            Assert.Equal(LicenceState.Active, status.State);
            Assert.False(status.Locked);
        }

        [Fact]
        public void WidenedList_FailsTheSignatureCheck_AndLocksThePc()
        {
            var file = Licence.Sign(Payload(new[] { "tables" }), _key);
            var widened = Licence.Verify(file, _key)! with { Capabilities = Capabilities.Keys.ToArray() };
            var forged = new LicenceFile(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(widened, Web)), file.Signature);

            Assert.Null(Licence.Verify(forged, _key));
            var status = Licence.Evaluate(forged, _key, Now, null);
            Assert.Equal(LicenceState.Invalid, status.State);
            Assert.True(status.Locked);
        }

        [Fact]
        public void RemovedList_FailsTheSignatureCheck()
        {
            var file = Licence.Sign(Payload(new[] { "tables" }), _key);
            var removed = Licence.Verify(file, _key)! with { Capabilities = null };
            var forged = new LicenceFile(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(removed, Web)), file.Signature);

            Assert.Null(Licence.Verify(forged, _key));
            Assert.True(Licence.Evaluate(forged, _key, Now, null).Locked);
        }

        [Fact]
        public void SignedWithAnotherKey_IsRefused()
        {
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var file = Licence.Sign(Payload(Capabilities.Keys.ToArray()), other);

            Assert.Null(Licence.Verify(file, _key));
            Assert.True(Licence.Evaluate(file, _key, Now, null).Locked);
        }
    }
}
