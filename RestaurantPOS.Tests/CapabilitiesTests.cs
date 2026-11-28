using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // C1: the capability list in code (keys, names, dependencies). Pure unit tests, no HTTP endpoint yet (C6/C8 add those).
    public class CapabilitiesTests
    {
        private static readonly string[] FirstKeys =
        {
            "tables", "kitchen-screen", "tokens", "reservations", "qr-ordering", "feedback", "loyalty", "stock",
            "recipes", "combos", "sell-by-weight", "simple-counter", "shifts", "credit-notes", "tips", "expenses"
        };

        [Fact]
        public void All_ListsTheFirstKeysInOrder()
        {
            Assert.Equal(FirstKeys, Capabilities.All.Select(c => c.Key).ToArray());
            Assert.Equal(FirstKeys, Capabilities.Keys.ToArray());
        }

        [Fact]
        public void Keys_AreUniqueLowercaseKebabCase_AndNamesAndDescriptionsAreFilled()
        {
            Assert.Equal(Capabilities.All.Count, Capabilities.All.Select(c => c.Key).Distinct().Count());
            foreach (var c in Capabilities.All)
            {
                Assert.Matches(new Regex("^[a-z]+(-[a-z]+)*$"), c.Key);
                Assert.InRange(c.Key.Length, 1, 40);
                Assert.False(string.IsNullOrWhiteSpace(c.Name), c.Key);
                Assert.False(string.IsNullOrWhiteSpace(c.Description), c.Key);
                Assert.NotNull(c.DependsOn);
            }
        }

        [Fact]
        public void Names_ArePlainEnglish()
        {
            Assert.Equal("Kitchen screen", Capabilities.Find("kitchen-screen")!.Name);
            Assert.Equal("QR ordering", Capabilities.Find("qr-ordering")!.Name);
            Assert.Equal("Sell by weight", Capabilities.Find("sell-by-weight")!.Name);
            Assert.Equal("Credit notes", Capabilities.Find("credit-notes")!.Name);
        }

        [Fact]
        public void EveryDependencyExists_AndNoCapabilityDependsOnItself()
        {
            foreach (var c in Capabilities.All)
                foreach (var d in c.DependsOn)
                {
                    Assert.True(Capabilities.IsKnown(d), $"{c.Key} depends on unknown {d}");
                    Assert.NotEqual(c.Key, d);
                }
        }

        [Fact]
        public void Dependencies_HaveNoCycles()
        {
            var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
            void Visit(string key, List<string> path)
            {
                if (state.TryGetValue(key, out var s))
                {
                    Assert.True(s == 2, "cycle: " + string.Join(" -> ", path.Append(key)));
                    return;
                }
                state[key] = 1;
                foreach (var d in Capabilities.Find(key)!.DependsOn) Visit(d, path.Append(key).ToList());
                state[key] = 2;
            }
            foreach (var c in Capabilities.All) Visit(c.Key, new List<string>());
        }

        [Fact]
        public void KnownDependencies()
        {
            Assert.Equal(new[] { "tables" }, Capabilities.Find("reservations")!.DependsOn.ToArray());
            Assert.Equal(new[] { "tables" }, Capabilities.Find("qr-ordering")!.DependsOn.ToArray());
            Assert.Equal(new[] { "stock" }, Capabilities.Find("recipes")!.DependsOn.ToArray());
            Assert.Empty(Capabilities.Find("tables")!.DependsOn);
            Assert.Empty(Capabilities.Find("loyalty")!.DependsOn);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("Tables")]
        [InlineData("tables ")]
        [InlineData("grocery")]
        public void Find_UnknownOrBadKey_GivesNull(string? key)
        {
            Assert.Null(Capabilities.Find(key));
            Assert.False(Capabilities.IsKnown(key));
        }

        [Fact]
        public void WithDependencies_AddsWhatIsNeeded_InListOrder_WithoutDuplicates()
        {
            Assert.Equal(new[] { "tables", "reservations", "qr-ordering" },
                Capabilities.WithDependencies(new[] { "qr-ordering", "reservations" }).ToArray());
            Assert.Equal(new[] { "stock", "recipes" },
                Capabilities.WithDependencies(new[] { "recipes", "recipes", "stock" }).ToArray());
            Assert.Empty(Capabilities.WithDependencies(Array.Empty<string>()));
        }

        [Fact]
        public void WithDependencies_UnknownKey_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => Capabilities.WithDependencies(new[] { "tables", "grocery" }));
            Assert.Equal("Unknown capability: grocery", ex.Message);
            var missing = Assert.Throws<ArgumentException>(() => Capabilities.WithDependencies(null!));
            Assert.Equal("Capabilities are missing.", missing.Message);
        }

        [Fact]
        public void Validate_GoodLists_GiveNull()
        {
            Assert.Null(Capabilities.Validate(Array.Empty<string>()));
            Assert.Null(Capabilities.Validate(new[] { "tables", "reservations" }));
            Assert.Null(Capabilities.Validate(Capabilities.Keys));
        }

        [Fact]
        public void Validate_BadLists_GiveTheMessage()
        {
            Assert.Equal("Capabilities are missing.", Capabilities.Validate(null));
            Assert.Equal("Unknown capability: grocery", Capabilities.Validate(new[] { "tables", "grocery" }));
            Assert.Equal("Unknown capability: ", Capabilities.Validate(new[] { "" }));
            Assert.Equal("Capability tables is listed twice.", Capabilities.Validate(new[] { "tables", "tables" }));
            Assert.Equal("Capability reservations needs tables.", Capabilities.Validate(new[] { "reservations" }));
            Assert.Equal("Capability recipes needs stock.", Capabilities.Validate(new[] { "loyalty", "recipes" }));
            // unknown keys are reported before missing dependencies
            Assert.Equal("Unknown capability: x", Capabilities.Validate(new[] { "reservations", "x" }));
        }
    }
}
