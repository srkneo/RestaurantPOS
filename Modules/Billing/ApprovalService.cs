using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using POSSystem.Data;
using POSSystem.Models;

namespace POSSystem.Billing
{
    public record ApprovalRequest(string? Pin, string? Action, int? BillId);
    public record ApprovalDto(string ApprovedBy);
    public record SetPinRequest(string? Pin);

    // Code and HTTP status for the screen: pin-required and wrong-pin (403), too-many (429).
    public class ApprovalException : Exception
    {
        public string Code { get; }
        public int Status { get; }
        public ApprovalException(string code, int status, string message) : base(message)
        {
            Code = code;
            Status = status;
        }
    }

    // Manager PIN: a manager or the owner types it on the cashier's screen to allow a void, a credit note or a reprint.
    // PINs are stored hashed like passwords. Five wrong PINs lock approvals of that restaurant for 15 minutes.
    public class ApprovalService
    {
        public const int MaxWrongTries = 5;
        public static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);
        public static readonly string[] ApproverRoles = { "manager", "admin" };
        private static readonly Regex PinPattern = new(@"^\d{4,6}$");

        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _users;
        private readonly IMemoryCache _cache;

        public ApprovalService(AppDbContext db, UserManager<AppUser> users, IMemoryCache cache)
        {
            _db = db;
            _users = users;
            _cache = cache;
        }

        public static bool IsValidPin(string? pin) => pin != null && PinPattern.IsMatch(pin);

        // Who allows the action: the caller when they are a manager or the owner, otherwise the manager whose PIN was typed.
        public async Task<string> Approver(ClaimsPrincipal caller, string? pin)
        {
            if (ApproverRoles.Any(caller.IsInRole)) return caller.FindFirstValue(ClaimTypes.Name) ?? "manager";
            if (string.IsNullOrWhiteSpace(pin)) throw new ApprovalException("pin-required", 403, "A manager PIN is needed for this.");
            var approver = await FindByPin(pin.Trim()) ?? throw new ApprovalException("wrong-pin", 403, "Wrong manager PIN.");
            return NameOf(approver);
        }

        public static string NameOf(AppUser user) =>
            !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName! : user.Email ?? user.Id;

        public async Task<AppUser?> FindByPin(string pin)
        {
            var key = $"pin-tries:{_db.CurrentTenantId}";
            _cache.TryGetValue(key, out int tries);
            if (tries >= MaxWrongTries) throw new ApprovalException("too-many", 429, "Too many wrong PINs. Wait 15 minutes and try again.");
            if (IsValidPin(pin))
            {
                foreach (var user in await Approvers())
                {
                    if (Matches(user, pin))
                    {
                        _cache.Remove(key);
                        return user;
                    }
                }
            }
            _cache.Set(key, tries + 1, LockTime);
            return null;
        }

        // True when another manager of this restaurant already has this PIN (a PIN must point to one person).
        public async Task<bool> PinTaken(AppUser user, string pin) =>
            (await Approvers()).Any(u => u.Id != user.Id && Matches(u, pin));

        public void SetPin(AppUser user, string? pin) =>
            user.PinHash = string.IsNullOrEmpty(pin) ? null : _users.PasswordHasher.HashPassword(user, pin);

        // Written with the next save, so the approval and the change it allowed are stored together.
        // Details is a jsonb column on Postgres, so the text goes in as JSON.
        public void Record(string entityType, string entityId, string details) =>
            _db.AuditLogs.Add(new AuditLog
            {
                Action = "Approve", EntityType = entityType, EntityId = entityId,
                Details = JsonSerializer.Serialize(new { approval = details })
            });

        private async Task<List<AppUser>> Approvers()
        {
            var tenant = _db.CurrentTenantId;
            var list = new List<AppUser>();
            foreach (var role in ApproverRoles)
                list.AddRange((await _users.GetUsersInRoleAsync(role)).Where(u => u.TenantId == tenant && u.IsActive && u.PinHash != null));
            return list;
        }

        private bool Matches(AppUser user, string pin) =>
            _users.PasswordHasher.VerifyHashedPassword(user, user.PinHash!, pin) != PasswordVerificationResult.Failed;
    }
}
