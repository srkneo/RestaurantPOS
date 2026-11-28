using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using POSSystem.Models;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // C2: outlet types owned by the platform. Pure unit tests, no HTTP endpoint yet (C9 adds the platform screen).
    public class OutletTypesTests
    {
        private static readonly string[] TypeKeys = { "restaurant", "cafe", "takeaway-counter", "cloud-kitchen", "bakery", "dairy" };

        [Fact]
        public void All_ListsTheSixTypesInOrder()
        {
            Assert.Equal(TypeKeys, OutletTypes.All.Select(t => t.Key).ToArray());
            Assert.Equal(TypeKeys, OutletTypes.Keys.ToArray());
        }

        [Fact]
        public void Keys_AreKebabCase_AndNamesAndDescriptionsAreFilled()
        {
            foreach (var t in OutletTypes.All)
            {
                Assert.Matches(new Regex("^[a-z]+(-[a-z]+)*$"), t.Key);
                Assert.False(string.IsNullOrWhiteSpace(t.Name), t.Key);
                Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Key);
            }
            Assert.Equal("Restaurant", OutletTypes.Find("restaurant")!.Name);
            Assert.Equal("Takeaway counter", OutletTypes.Find("takeaway-counter")!.Name);
            Assert.Equal("Cloud kitchen", OutletTypes.Find("cloud-kitchen")!.Name);
        }

        [Fact]
        public void EveryType_UsesOnlyKnownCapabilities_WithDependencies_InRegistryOrder()
        {
            foreach (var t in OutletTypes.All)
            {
                Assert.NotEmpty(t.Capabilities);
                Assert.Null(Capabilities.Validate(t.Capabilities));
                Assert.Equal(Capabilities.WithDependencies(t.Capabilities).ToArray(), t.Capabilities.ToArray());
            }
        }

        [Fact]
        public void KnownCapabilityLists()
        {
            Assert.Equal(new[] { "tables", "kitchen-screen", "tokens", "reservations", "qr-ordering", "feedback", "loyalty", "stock", "recipes", "combos", "shifts", "credit-notes", "tips", "expenses" },
                OutletTypes.Find("restaurant")!.Capabilities.ToArray());
            Assert.Equal(new[] { "kitchen-screen", "tokens", "stock", "recipes", "combos", "shifts", "credit-notes", "expenses" },
                OutletTypes.Find("cloud-kitchen")!.Capabilities.ToArray());
            Assert.Equal(new[] { "kitchen-screen", "tokens", "loyalty", "stock", "sell-by-weight", "simple-counter", "shifts", "credit-notes", "expenses" },
                OutletTypes.Find("dairy")!.Capabilities.ToArray());
            Assert.DoesNotContain("tables", OutletTypes.Find("takeaway-counter")!.Capabilities);
            Assert.DoesNotContain("reservations", OutletTypes.Find("cafe")!.Capabilities);
            Assert.Contains("sell-by-weight", OutletTypes.Find("bakery")!.Capabilities);
        }

        [Theory]
        [InlineData("restaurant", "dine-in")]
        [InlineData("cafe", "cafe")]
        [InlineData("takeaway-counter", "takeaway-counter")]
        [InlineData("bakery", "bakery")]
        [InlineData("dairy", "dairy")]
        public void Preset_GivesTheSameSettingsAsTodaysOwnerPreset(string type, string oldPreset)
        {
            var start = new OutletSettings { MaxItemQuantity = 7, FirstToken = 50, SimpleCounter = true, DineInEnabled = false, KitchenScreenEnabled = false, LoyaltyEnabled = true, QrOrderingEnabled = true, BillPrefix = "BK" };
            Assert.Equal(start.WithPreset(oldPreset).ToJson(), OutletTypes.ApplyPreset(type, start).ToJson());
            var plain = new OutletSettings();
            Assert.Equal(plain.WithPreset(oldPreset).ToJson(), OutletTypes.ApplyPreset(type, plain).ToJson());
        }

        [Fact]
        public void CloudKitchen_Preset_IsTakeawayOnly_WithNames_AndKitchenScreen()
        {
            var s = OutletTypes.ApplyPreset("cloud-kitchen", new OutletSettings { MaxItemQuantity = 12, FirstToken = 5, SimpleCounter = true });
            Assert.False(s.DineInEnabled);
            Assert.True(s.TakeawayEnabled);
            Assert.Equal(OrderType.Takeaway, s.DefaultOrderType);
            Assert.True(s.TakeawayNeedsName);
            Assert.True(s.KitchenScreenEnabled);
            Assert.False(s.SimpleCounter);
            // quantity limit and first token are kept
            Assert.Equal(12, s.MaxItemQuantity);
            Assert.Equal(5, s.FirstToken);
        }

        [Fact]
        public void ApplyPreset_DoesNotChangeTheGivenSettings()
        {
            var start = new OutletSettings { DineInEnabled = true, SimpleCounter = false };
            var before = start.ToJson();
            OutletTypes.ApplyPreset("dairy", start);
            Assert.Equal(before, start.ToJson());
        }

        [Fact]
        public void Preset_AgreesWithTheCapabilities()
        {
            foreach (var t in OutletTypes.All)
            {
                var s = OutletTypes.ApplyPreset(t.Key, new OutletSettings());
                Assert.Equal(t.Capabilities.Contains("simple-counter"), s.SimpleCounter);
                if (s.KitchenScreenEnabled) Assert.Contains("kitchen-screen", t.Capabilities);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("Restaurant")]
        [InlineData("dine-in")]
        [InlineData("night-club")]
        public void Find_UnknownOrBadKey_GivesNull(string? key)
        {
            Assert.Null(OutletTypes.Find(key));
            Assert.False(OutletTypes.IsKnown(key));
        }

        [Fact]
        public void ApplyPreset_BadInput_Throws()
        {
            var unknown = Assert.Throws<ArgumentException>(() => OutletTypes.ApplyPreset("night-club", new OutletSettings()));
            Assert.Equal("Unknown outlet type: night-club", unknown.Message);
            var empty = Assert.Throws<ArgumentException>(() => OutletTypes.ApplyPreset(null, new OutletSettings()));
            Assert.Equal("Unknown outlet type: ", empty.Message);
            var missing = Assert.Throws<ArgumentException>(() => OutletTypes.ApplyPreset("cafe", null!));
            Assert.Equal("Settings are missing.", missing.Message);
        }
    }
}
