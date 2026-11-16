using System;
using System.Collections.Generic;
using System.Linq;
using POSSystem.Billing;
using POSSystem.Models;
using Xunit;

namespace RestaurantPOS.Tests
{
    // Thousands of random bills (every GST mode, prices with and without GST, discounts) must always obey the money rules.
    // A fixed seed keeps failures repeatable; the failing case is printed in the message.
    public class BillMathPropertyTests
    {
        private static readonly decimal[] Rates = { 0m, 5m, 12m, 18m, 28m };

        [Theory]
        [InlineData(2026)]
        [InlineData(7)]
        [InlineData(424242)]
        public void RandomBills_AlwaysAddUp(int seed)
        {
            var random = new Random(seed);
            for (var n = 0; n < 3000; n++)
            {
                var mode = (TaxMode)random.Next(3);
                var inclusive = random.Next(2) == 0;
                var lines = Enumerable.Range(0, random.Next(1, 9)).Select(i => new BillLineInput(
                    $"Item {i}", random.Next(1, 21), random.Next(1, 100000) / 100m,
                    mode == TaxMode.Regular ? Rates[random.Next(Rates.Length)] : 0m)).ToList();
                var itemsTotal = lines.Sum(l => BillCalculator.Paise(l.Quantity * l.UnitPrice));
                var discount = random.Next(3) switch
                {
                    0 => 0m,
                    1 => random.Next(0, 4) / 100m,                                   // tiny discounts are the hardest to share
                    _ => Math.Floor(itemsTotal * (decimal)random.NextDouble() * 100m) / 100m
                };
                discount = Math.Min(discount, itemsTotal);
                var context = $"seed {seed} case {n}: mode {mode}, inclusive {inclusive}, discount {discount}, lines " +
                    string.Join("; ", lines.Select(l => $"{l.Quantity} x {l.UnitPrice} @ {l.TaxRate}%"));

                var bill = BillCalculator.Calculate(lines, mode, inclusive, discount);
                Check(bill, mode, itemsTotal, discount, context);
            }
        }

        private static void Check(BillTotals bill, TaxMode mode, decimal itemsTotal, decimal discount, string context)
        {
            void True(bool ok, string rule) { if (!ok) throw new Xunit.Sdk.XunitException($"{rule} broken for {context}"); }
            bool Paise(decimal v) => v == Math.Round(v, 2);

            True(bill.ItemsTotal == itemsTotal, "items total = sum of quantity x price");
            True(bill.Discount == discount && bill.Lines.Sum(l => l.Discount) == discount, "line discounts add up to the discount");
            True(bill.TaxableValue == bill.Lines.Sum(l => l.TaxableValue), "taxable value = sum of lines");
            True(bill.Cgst == bill.Lines.Sum(l => l.Cgst) && bill.Sgst == bill.Lines.Sum(l => l.Sgst), "GST = sum of lines");
            True(bill.Cgst == bill.Sgst, "CGST = SGST");
            foreach (var l in bill.Lines)
            {
                True(l.Discount >= 0 && l.Discount <= l.Amount, "a line discount is between 0 and the line amount");
                True(l.TaxableValue >= 0 && l.Cgst >= 0 && l.Cgst == l.Sgst, "line taxable and GST are not negative, CGST = SGST");
                True(Paise(l.Amount) && Paise(l.Discount) && Paise(l.TaxableValue) && Paise(l.Cgst), "line values are whole paise");
                if (mode != TaxMode.Regular || l.TaxRate == 0) True(l.Cgst == 0, "no GST without regular GST");
            }

            var beforeRounding = bill.TaxableValue + bill.Cgst + bill.Sgst;
            True(bill.Total == Math.Round(bill.Total, 0) && bill.Total >= 0, "total is whole rupees and not negative");
            True(bill.RoundOff == bill.Total - beforeRounding && Math.Abs(bill.RoundOff) <= 0.50m, "round-off is at most 50 paise");
            if (mode != TaxMode.Regular)
                True(bill.TaxableValue == itemsTotal - discount, "without GST the bill is items total minus discount");
        }

        [Fact]
        public void PricesWithGst_TheCustomerPaysTheMenuPrice()
        {
            var lines = new List<BillLineInput> { new("Thali", 3, 199m, 5m), new("Lassi", 2, 61m, 12m), new("Cola", 1, 45m, 18m) };
            var bill = BillCalculator.Calculate(lines, TaxMode.Regular, true, 0m);
            Assert.Equal(764m, bill.TaxableValue + bill.Cgst + bill.Sgst);   // 597 + 122 + 45, GST is inside the prices
            Assert.Equal(764m, bill.Total);
        }

        [Fact]
        public void TinyDiscount_IsNeverNegativeOnAnyLine()
        {
            var shares = BillCalculator.ShareDiscount(new[] { 1m, 1m, 1m, 1m }, 0.02m);
            Assert.Equal(0.02m, shares.Sum());
            Assert.All(shares, s => Assert.InRange(s, 0m, 0.01m));
        }
    }
}
