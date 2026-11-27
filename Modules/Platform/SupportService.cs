using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Platform
{
    public record SupportInfoDto(string CompanyName, string ProductName, string? Phone, string? WhatsApp, string? Email, string? Hours, string? Website);

    public record AboutDto(string CompanyName, string ProductName, string? Phone, string? WhatsApp, string? Email, string? Hours, string? Website,
        string Version, bool ShopPc, string? LicenceCustomer, string? LicencePlan, string? DeviceCode);

    // The support details: read by everyone, changed only by the platform owner.
    public class SupportService
    {
        // Shown until the platform owner saves the details on /platform/support.
        public static readonly SupportInfoDto Default = new("Vyom Solutions", "AllInOne POS", null, null, null, null, null);

        private readonly AppDbContext _db;
        private readonly TimeProvider _time;

        public SupportService(AppDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        public async Task<SupportInfoDto> Get(CancellationToken ct)
        {
            var row = await _db.SupportSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
            return row is null ? Default : new SupportInfoDto(row.CompanyName, row.ProductName, row.Phone, row.WhatsApp, row.Email, row.Hours, row.Website);
        }

        // Call Validate first.
        public async Task<SupportInfoDto> Save(SupportInfoDto input, CancellationToken ct)
        {
            Apply(_db, Clean(input), _time.GetUtcNow().UtcDateTime);
            await _db.SaveChangesAsync(ct);
            return await Get(ct);
        }

        // Puts the details into this database without saving. Used by Save on the cloud and by the licence renewal on a shop PC.
        public static void Apply(AppDbContext db, SupportInfoDto info, DateTime now)
        {
            var row = db.SupportSettings.Find(1);
            if (row is null)
            {
                row = new SupportSetting { Id = 1 };
                db.SupportSettings.Add(row);
            }
            row.CompanyName = info.CompanyName;
            row.ProductName = info.ProductName;
            row.Phone = info.Phone;
            row.WhatsApp = info.WhatsApp;
            row.Email = info.Email;
            row.Hours = info.Hours;
            row.Website = info.Website;
            row.UpdatedAt = now;
        }

        public static SupportInfoDto Clean(SupportInfoDto s) => new(
            s.CompanyName?.Trim() ?? "", s.ProductName?.Trim() ?? "",
            Blank(s.Phone), Blank(s.WhatsApp), Blank(s.Email), Blank(s.Hours), Blank(s.Website));

        public static Dictionary<string, string[]> Validate(SupportInfoDto input)
        {
            var s = Clean(input);
            var errors = new Dictionary<string, string[]>();
            if (s.CompanyName == "") errors["companyName"] = new[] { "Enter the company name." };
            else if (s.CompanyName.Length > 100) errors["companyName"] = new[] { "Keep it under 100 characters." };
            if (s.ProductName == "") errors["productName"] = new[] { "Enter the product name." };
            else if (s.ProductName.Length > 60) errors["productName"] = new[] { "Keep it under 60 characters." };
            if (s.Phone is not null && !IsPhone(s.Phone)) errors["phone"] = new[] { "Use digits, spaces and an optional + at the start, e.g. +91 94295 49007." };
            if (s.WhatsApp is not null && !IsPhone(s.WhatsApp)) errors["whatsApp"] = new[] { "Use digits, spaces and an optional + at the start, e.g. +91 94295 49007." };
            if (s.Email is not null && (s.Email.Length > 200 || !MailAddress.TryCreate(s.Email, out _))) errors["email"] = new[] { "Enter a valid email address." };
            if (s.Hours is not null && s.Hours.Length > 100) errors["hours"] = new[] { "Keep it under 100 characters." };
            if (s.Website is not null && (s.Website.Length > 200 || !Uri.TryCreate(s.Website, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
                errors["website"] = new[] { "Enter the full address, starting with https://" };
            return errors;
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static bool IsPhone(string value)
        {
            if (value.Length > 20) return false;
            var digits = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsAsciiDigit(c)) digits++;
                else if (c == '+' && i == 0) continue;
                else if (c != ' ' && c != '-') return false;
            }
            return digits is >= 6 and <= 15;
        }
    }
}
