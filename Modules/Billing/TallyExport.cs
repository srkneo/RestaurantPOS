using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Billing
{
    // Tally export (v1.9): one Sales voucher per business day for Tally Prime / ERP 9 "Import Data".
    // Debit: money received by method, plus what is still unpaid. Credit: sales, CGST, SGST, round off.
    // Every voucher balances; void bills are left out; credit notes are not part of this file.
    public static class TallyExport
    {
        public const string CashLedger = "Cash";
        public const string UpiLedger = "UPI Collections";
        public const string CardLedger = "Card Collections";
        public const string UnpaidLedger = "POS Unpaid Bills";
        public const string SalesLedger = "Sales";
        public const string CgstLedger = "Output CGST";
        public const string SgstLedger = "Output SGST";
        public const string RoundOffLedger = "Round Off";

        public static async Task<string> Xml(AppDbContext db, DateOnly from, DateOnly to)
        {
            var bills = await db.Bills.Include(b => b.Payments).AsNoTracking()
                .Where(b => b.OutletId == db.CurrentOutletId && b.BusinessDate >= from && b.BusinessDate <= to && b.Status != BillStatus.Void)
                .ToListAsync();

            var message = new XElement("TALLYMESSAGE");
            foreach (var day in bills.GroupBy(b => b.BusinessDate).OrderBy(g => g.Key))
                message.Add(Voucher(day.Key, day.ToList()));

            var envelope = new XElement("ENVELOPE",
                new XElement("HEADER", new XElement("TALLYREQUEST", "Import Data")),
                new XElement("BODY", new XElement("IMPORTDATA",
                    new XElement("REQUESTDESC", new XElement("REPORTNAME", "Vouchers")),
                    new XElement("REQUESTDATA", message))));
            return new XDocument(new XDeclaration("1.0", "utf-8", null), envelope).Declaration + "\r\n" + envelope;
        }

        private static XElement Voucher(DateOnly date, List<Bill> bills)
        {
            var total = bills.Sum(b => b.Total);
            var cgst = bills.Sum(b => b.Cgst);
            var sgst = bills.Sum(b => b.Sgst);
            var roundOff = bills.Sum(b => b.RoundOff);
            var payments = bills.SelectMany(b => b.Payments).ToList();
            decimal PaidBy(PaymentMethod m) => payments.Where(p => p.Method == m).Sum(p => p.Amount);
            var cash = PaidBy(PaymentMethod.Cash);
            var upi = PaidBy(PaymentMethod.Upi);
            var card = PaidBy(PaymentMethod.Card);
            var unpaid = total - cash - upi - card;
            var sales = total - cgst - sgst - roundOff;

            var first = bills.Min(b => b.InvoiceNumber);
            var last = bills.Max(b => b.InvoiceNumber);
            var voucher = new XElement("VOUCHER", new XAttribute("VCHTYPE", "Sales"), new XAttribute("ACTION", "Create"),
                new XElement("DATE", date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
                new XElement("VOUCHERTYPENAME", "Sales"),
                new XElement("VOUCHERNUMBER", "POS-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
                new XElement("NARRATION", $"POS sales of {date:dd-MM-yyyy}: {bills.Count} bills ({first} to {last})"));

            // Tally: a debit is ISDEEMEDPOSITIVE Yes with a negative amount; a credit is No with a positive amount.
            void Entry(string ledger, decimal credit)
            {
                if (credit == 0) return;
                voucher.Add(new XElement("ALLLEDGERENTRIES.LIST",
                    new XElement("LEDGERNAME", ledger),
                    new XElement("ISDEEMEDPOSITIVE", credit < 0 ? "Yes" : "No"),
                    new XElement("AMOUNT", credit.ToString("0.00", CultureInfo.InvariantCulture))));
            }

            Entry(CashLedger, -cash);
            Entry(UpiLedger, -upi);
            Entry(CardLedger, -card);
            Entry(UnpaidLedger, -unpaid);
            Entry(SalesLedger, sales);
            Entry(CgstLedger, cgst);
            Entry(SgstLedger, sgst);
            Entry(RoundOffLedger, roundOff);
            return voucher;
        }

        public static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);
    }
}
