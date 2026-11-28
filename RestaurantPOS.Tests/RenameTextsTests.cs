using System.Collections.Generic;
using POSSystem.Alerts;
using POSSystem.Data;
using Xunit;

namespace RestaurantPOS.Tests
{
    // v2.4 RN2: texts the server sends say AllInOne POS; a name set on the server (Alerts:Name) is kept.
    public class RenameTextsTests
    {
        [Fact]
        public void AuthenticatorIssuer_IsAllInOnePos()
        {
            Assert.Equal("AllInOne POS", AppTwoFactorEndpoints.Issuer);
        }

        [Fact]
        public void ErrorAlerts_DefaultName_IsAllInOnePos()
        {
            Assert.Equal("AllInOne POS", ErrorAlertSender.DefaultName);
            var text = ErrorAlertSender.Compose(ErrorAlertSender.DefaultName, new List<string> { "Order 1 failed" }, 0);
            Assert.StartsWith("⚠️ AllInOne POS: 1 error(s)", text);
            Assert.DoesNotContain("RestaurantPOS", text);
        }

        [Fact]
        public void ErrorAlerts_ANameSetOnTheServer_IsKept()
        {
            var text = ErrorAlertSender.Compose("Anand pilot", new List<string> { "Order 1 failed" }, 2);
            Assert.StartsWith("⚠️ Anand pilot: 3 error(s)", text);
            Assert.DoesNotContain("AllInOne POS", text);
        }
    }
}
