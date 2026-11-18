using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;

namespace POSSystem.Billing
{
    public record ApprovalRulesDto(bool ReprintNeedsPin);

    // Owner: which cashier actions need a manager PIN. Reprinting a bill does by default; the owner can drop it.
    // (Void and credit notes always need a manager, they change money.)
    public static class ApprovalRulesEndpoints
    {
        public static WebApplication MapApprovalRulesEndpoints(this WebApplication app)
        {
            var admin = app.MapGroup("/api/admin/approval-rules").RequireAuthorization(p => p.RequireRole("admin")).WithTags("Settings");

            admin.MapGet("/", async (AppDbContext db) =>
            {
                var outlet = await db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);
                return outlet == null ? Results.NotFound() : Results.Ok(new ApprovalRulesDto(outlet.GetSettings().ReprintNeedsPin));
            }).WithName("GetApprovalRules").WithSummary("Which cashier actions need a manager PIN");

            admin.MapPut("/", async (ApprovalRulesDto request, AppDbContext db) =>
            {
                var outlet = await db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);
                if (outlet == null) return Results.NotFound();
                var settings = outlet.GetSettings();
                settings.ReprintNeedsPin = request.ReprintNeedsPin;
                outlet.Settings = settings.ToJson();
                await db.SaveChangesAsync();
                return Results.Ok(new ApprovalRulesDto(settings.ReprintNeedsPin));
            }).WithName("SetApprovalRules").WithSummary("Choose whether a cashier needs a manager PIN to print a bill again");

            return app;
        }
    }
}
