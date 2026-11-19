using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using POSSystem.Platform;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Early shops get new versions first; the policy file the shop's update script reads keeps that mode.
    public class ShopUpdatesEarlyTests
    {
        [Fact]
        public void EarlyMode_IsKeptInThePolicyFile()
        {
            var folder = Path.Combine(Path.GetTempPath(), "pos-upd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Updates:Folder"] = folder }).Build();
                ShopUpdates.WritePolicy(config, new UpdatePolicyDto("early", null));
                Assert.Equal("mode=early\nversion=\n", File.ReadAllText(Path.Combine(folder, "policy")));
                Assert.Equal("early", ShopUpdates.ReadPolicy(folder).Mode);

                ShopUpdates.WritePolicy(config, new UpdatePolicyDto("sometimes", null));
                Assert.Equal("auto", ShopUpdates.ReadPolicy(folder).Mode);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
