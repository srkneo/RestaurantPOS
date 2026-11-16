using System;
using System.Collections.Generic;
using System.Linq;
using POSSystem.Models;

namespace POSSystem.Billing
{
    public record BillLineInput(string Name, int Quantity, decimal UnitPrice, decimal TaxRate);
    public record BillLineResult(string Name, int Quantity, decimal UnitPrice, decimal TaxRate, decimal Amount, decimal Discount,
        decimal TaxableValue, decimal Cgst, decimal Sgst);
    public record TaxSummaryRow(decimal TaxRate, decimal TaxableValue, decimal Cgst, decimal Sgst);
    public record BillTotals(IReadOnlyList<BillLineResult> Lines, decimal ItemsTotal, decimal Discount, decimal TaxableValue,
        decimal Cgst, decimal Sgst, decimal RoundOff, decimal Total);

    // All bill money maths in one place. Amounts are rounded to paise (half away from zero), the total to whole rupees.
    public static class BillCalculator
    {
        public static decimal Paise(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        // discount: rupees off the items total (before tax when prices exclude tax; off the price paid when they include it).
        public static BillTotals Calculate(IReadOnlyList<BillLineInput> lines, TaxMode mode, bool pricesIncludeTax, decimal discount)
        {
            if (lines.Count == 0) throw new ArgumentException("A bill needs at least one item.");
            var amounts = lines.Select(l => Paise(l.Quantity * l.UnitPrice)).ToList();
            var itemsTotal = amounts.Sum();
            discount = Paise(discount);
            if (discount < 0 || discount > itemsTotal) throw new ArgumentException("The discount must be between 0 and the items total.");

            var shares = ShareDiscount(amounts, discount);
            var results = new List<BillLineResult>();
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var net = amounts[i] - shares[i];
                decimal taxable = net, cgst = 0m, sgst = 0m;
                if (mode == TaxMode.Regular && line.TaxRate > 0)
                {
                    if (pricesIncludeTax)
                    {
                        // Price already contains GST: take the tax out, keep the line total equal to what the customer pays.
                        var preTax = Paise(net * 100m / (100m + line.TaxRate));
                        cgst = Paise(preTax * line.TaxRate / 200m);
                        sgst = cgst;
                        taxable = net - cgst - sgst;
                    }
                    else
                    {
                        cgst = Paise(net * line.TaxRate / 200m);
                        sgst = cgst;
                    }
                }
                results.Add(new BillLineResult(line.Name, line.Quantity, line.UnitPrice, line.TaxRate, amounts[i], shares[i], taxable, cgst, sgst));
            }

            var taxableTotal = results.Sum(r => r.TaxableValue);
            var cgstTotal = results.Sum(r => r.Cgst);
            var sgstTotal = results.Sum(r => r.Sgst);
            var beforeRounding = taxableTotal + cgstTotal + sgstTotal;
            var total = Math.Round(beforeRounding, 0, MidpointRounding.AwayFromZero);
            return new BillTotals(results, itemsTotal, discount, taxableTotal, cgstTotal, sgstTotal, total - beforeRounding, total);
        }

        // Each line gets its share of the discount by amount, rounded down to paise; the paise left over go one each to the lines
        // that lost most in rounding. Shares add up exactly, and no share is below 0 or above its line
        // (the old way gave the remainder to the largest line, which could go below 0 for tiny discounts).
        public static IReadOnlyList<decimal> ShareDiscount(IReadOnlyList<decimal> amounts, decimal discount)
        {
            var total = amounts.Sum();
            var shares = new decimal[amounts.Count];
            if (discount == 0 || total == 0) return shares;
            var exact = amounts.Select(a => discount * a / total).ToArray();
            for (var i = 0; i < amounts.Count; i++) shares[i] = Math.Floor(exact[i] * 100m) / 100m;
            var left = (int)Math.Round((discount - shares.Sum()) * 100m);
            var order = Enumerable.Range(0, amounts.Count)
                .OrderByDescending(i => exact[i] - shares[i]).ThenByDescending(i => amounts[i]).ThenBy(i => i).ToList();
            for (var k = 0; k < left; k++) shares[order[k % order.Count]] += 0.01m;
            return shares;
        }

        public static IReadOnlyList<TaxSummaryRow> TaxSummary(IEnumerable<BillLine> lines) => lines
            .GroupBy(l => l.TaxRate)
            .OrderBy(g => g.Key)
            .Select(g => new TaxSummaryRow(g.Key, g.Sum(l => l.TaxableValue), g.Sum(l => l.Cgst), g.Sum(l => l.Sgst)))
            .ToList();
    }

    // Invoice numbers: PREFIX/YY-YY/000001, at most 16 characters (GST rule), new series each financial year (April to March).
    public static class BillNumbers
    {
        public const int MaxLength = 16;

        public static int FinancialYearOf(DateOnly date) => date.Month >= 4 ? date.Year : date.Year - 1;

        public static string Format(string prefix, int financialYear, int sequence)
        {
            var year = $"{financialYear % 100:00}-{(financialYear + 1) % 100:00}";
            var width = Math.Max(MaxLength - prefix.Length - year.Length - 2, 1);
            return $"{prefix}/{year}/{sequence.ToString().PadLeft(width, '0')}";
        }
    }
}
