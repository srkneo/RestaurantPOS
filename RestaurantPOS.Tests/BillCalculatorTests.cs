using System;
using System.Linq;
using POSSystem.Billing;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    public class BillCalculatorTests
    {
        private static BillLineInput Line(string name, int qty, decimal price, decimal rate) => new(name, qty, price, rate);

        [Fact]
        public void Regular_PricesWithoutTax_AddsCgstAndSgstPerRate_AndRoundsTheTotal()
        {
            var t = BillCalculator.Calculate(new[] { Line("Paneer Tikka", 2, 220m, 5m), Line("Cola", 1, 60m, 18m) }, TaxMode.Regular, false, 0m);

            Assert.Equal(500m, t.ItemsTotal);
            Assert.Equal(500m, t.TaxableValue);
            Assert.Equal(16.40m, t.Cgst);   // 11.00 + 5.40
            Assert.Equal(16.40m, t.Sgst);
            Assert.Equal(0.20m, t.RoundOff); // 532.80 -> 533
            Assert.Equal(533m, t.Total);
            Assert.Equal(11.00m, t.Lines[0].Cgst);
            Assert.Equal(5.40m, t.Lines[1].Sgst);
        }

        [Fact]
        public void Regular_RoundsDown_WhenBelowHalf()
        {
            var t = BillCalculator.Calculate(new[] { Line("Thali", 1, 152.76m, 5m) }, TaxMode.Regular, false, 0m);
            // tax 3.82 + 3.82 -> 160.40 -> 160
            Assert.Equal(3.82m, t.Cgst);
            Assert.Equal(-0.40m, t.RoundOff);
            Assert.Equal(160m, t.Total);
        }

        [Fact]
        public void Regular_PricesWithTax_TakesTaxOut_AndKeepsWhatTheCustomerPays()
        {
            var even = BillCalculator.Calculate(new[] { Line("Thali", 1, 105m, 5m) }, TaxMode.Regular, true, 0m);
            Assert.Equal(100m, even.TaxableValue);
            Assert.Equal(2.50m, even.Cgst);
            Assert.Equal(2.50m, even.Sgst);
            Assert.Equal(105m, even.Total);
            Assert.Equal(0m, even.RoundOff);

            var odd = BillCalculator.Calculate(new[] { Line("Combo", 1, 99m, 5m) }, TaxMode.Regular, true, 0m);
            Assert.Equal(2.36m, odd.Cgst);
            Assert.Equal(2.36m, odd.Sgst);
            Assert.Equal(94.28m, odd.TaxableValue);
            Assert.Equal(99m, odd.TaxableValue + odd.Cgst + odd.Sgst);
            Assert.Equal(99m, odd.Total);
        }

        [Fact]
        public void Discount_IsSharedByLineAmount_BeforeTax()
        {
            var t = BillCalculator.Calculate(new[] { Line("Biryani", 1, 300m, 5m), Line("Raita", 2, 50m, 5m) }, TaxMode.Regular, false, 50m);

            Assert.Equal(37.50m, t.Lines[0].Discount);
            Assert.Equal(12.50m, t.Lines[1].Discount);
            Assert.Equal(350m, t.TaxableValue);
            Assert.Equal(8.75m, t.Cgst);      // 6.56 + 2.19
            Assert.Equal(8.75m, t.Sgst);
            Assert.Equal(0.50m, t.RoundOff);  // 367.50 -> 368 (half rounds up)
            Assert.Equal(368m, t.Total);
        }

        [Fact]
        public void DiscountShares_AlwaysAddUpExactly()
        {
            var shares = BillCalculator.ShareDiscount(new[] { 10m, 10m, 10m }, 10m);
            Assert.Equal(10m, shares.Sum());
            Assert.All(shares, s => Assert.InRange(s, 3.33m, 3.34m));

            Assert.Equal(new[] { 0m, 0m }, BillCalculator.ShareDiscount(new[] { 5m, 5m }, 0m));
        }

        [Fact]
        public void Composition_And_NoGst_ShowNoTax()
        {
            var lines = new[] { Line("Paneer Tikka", 2, 220m, 5m), Line("Cola", 1, 60m, 18m) };
            foreach (var mode in new[] { TaxMode.Composition, TaxMode.None })
            {
                var t = BillCalculator.Calculate(lines, mode, false, 20m);
                Assert.Equal(0m, t.Cgst);
                Assert.Equal(0m, t.Sgst);
                Assert.Equal(480m, t.TaxableValue);
                Assert.Equal(480m, t.Total);
            }
        }

        [Fact]
        public void ZeroRateLine_HasNoTax()
        {
            var t = BillCalculator.Calculate(new[] { Line("Water", 1, 20m, 0m), Line("Tea", 1, 20m, 5m) }, TaxMode.Regular, false, 0m);
            Assert.Equal(0m, t.Lines[0].Cgst);
            Assert.Equal(0.50m, t.Lines[1].Cgst);
            Assert.Equal(41m, t.Total);
        }

        [Fact]
        public void BadInput_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => BillCalculator.Calculate(Array.Empty<BillLineInput>(), TaxMode.None, false, 0m));
            Assert.Throws<ArgumentException>(() => BillCalculator.Calculate(new[] { Line("Tea", 1, 20m, 5m) }, TaxMode.None, false, 21m));
            Assert.Throws<ArgumentException>(() => BillCalculator.Calculate(new[] { Line("Tea", 1, 20m, 5m) }, TaxMode.None, false, -1m));
        }

        [Fact]
        public void InvoiceNumbers_Are16CharactersAtMost_AndFollowTheFinancialYear()
        {
            Assert.Equal("INV/26-27/000123", BillNumbers.Format("INV", 2026, 123));
            Assert.Equal(16, BillNumbers.Format("INV", 2026, 123).Length);
            Assert.Equal("A/26-27/00000001", BillNumbers.Format("A", 2026, 1));
            Assert.Equal("INV/99-00/000001", BillNumbers.Format("INV", 2099, 1));

            Assert.Equal(2026, BillNumbers.FinancialYearOf(new DateOnly(2026, 4, 1)));
            Assert.Equal(2026, BillNumbers.FinancialYearOf(new DateOnly(2027, 3, 31)));
            Assert.Equal(2025, BillNumbers.FinancialYearOf(new DateOnly(2026, 3, 31)));
        }
    }
}
