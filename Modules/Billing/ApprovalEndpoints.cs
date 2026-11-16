using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using POSSystem.Models;

namespace POSSystem.Billing
{
    // Manager PINs, approvals on the cashier's screen, and credit notes.
    public static class ApprovalEndpoints
    {
        private static readonly string[] Till = { "cashier", "manager", "admin" };
        private static readonly string[] Actions = { "reprint" };

        public static WebApplication MapApprovalEndpoints(this WebApplication app)
        {
            app.MapPut("/api/staff/{id}/pin", async (string id, SetPinRequest request, ClaimsPrincipal caller, UserManager<AppUser> users,
                ApprovalService approvals) =>
            {
                var user = await users.FindByIdAsync(id);
                if (user == null || user.TenantId.ToString(CultureInfo.InvariantCulture) != caller.FindFirstValue("tenant_id")) return Results.NotFound();
                var roles = await users.GetRolesAsync(user);
                var pin = request.Pin?.Trim();
                var errors = new Dictionary<string, string[]>();
                if (!roles.Any(r => ApprovalService.ApproverRoles.Contains(r)))
                    errors["pin"] = new[] { "Only managers and the owner can have a PIN." };
                else if (!string.IsNullOrEmpty(pin) && !ApprovalService.IsValidPin(pin))
                    errors["pin"] = new[] { "Use 4 to 6 digits." };
                else if (!string.IsNullOrEmpty(pin) && await approvals.PinTaken(user, pin))
                    errors["pin"] = new[] { "Another manager already uses this PIN. Choose a different one." };
                if (errors.Count > 0) return Results.ValidationProblem(errors);

                approvals.SetPin(user, pin);
                var saved = await users.UpdateAsync(user);
                return saved.Succeeded ? Results.NoContent() : Results.Problem("The PIN could not be saved.");
            })
            .RequireAuthorization(p => p.RequireRole("admin"))
            .WithTags("Staff")
            .WithName("SetStaffPin").WithSummary("Set or clear the approval PIN of a manager or the owner (empty PIN clears it)");

            app.MapPost("/api/approvals", (ApprovalRequest request, ApprovalService approvals, AppDbContext db) =>
                Run(async () =>
                {
                    if (!Actions.Contains(request.Action)) return Results.BadRequest(new { message = "Unknown action." });
                    var approver = await approvals.FindByPin(request.Pin?.Trim() ?? string.Empty)
                        ?? throw new ApprovalException("wrong-pin", 403, "Wrong manager PIN.");
                    var name = ApprovalService.NameOf(approver);
                    approvals.Record("Bill", request.BillId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, $"{request.Action} approved by {name}");
                    await db.SaveChangesAsync();
                    return Results.Ok(new ApprovalDto(name));
                }))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithTags("Billing")
                .WithName("Approve").WithSummary("Check a manager PIN for an action such as a reprint (written to the audit log)");

            app.MapPost("/api/bills/{id:int}/credit-note", (int id, CreateCreditNoteRequest request, ClaimsPrincipal caller,
                CreditNoteService notes, ApprovalService approvals) =>
                Run(async () =>
                {
                    var approvedBy = await approvals.Approver(caller, request.ApproverPin);
                    var note = await notes.Create(id, request, approvedBy);
                    return Results.Created($"/api/credit-notes/{note.Id}", await notes.ToDto(note));
                }))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithTags("Billing")
                .WithName("CreateCreditNote").WithSummary("Cancel a bill of an earlier day with a credit note (manager or manager PIN)");

            app.MapGet("/api/credit-notes/{id:int}", (int id, CreditNoteService notes) =>
                Run(async () => await notes.Get(id) is { } note ? Results.Ok(await notes.ToDto(note)) : Results.NotFound()))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithTags("Billing")
                .WithName("GetCreditNote").WithSummary("One credit note with the bill it cancels");

            app.MapGet("/api/credit-notes", async (string? date, CreditNoteService notes) =>
            {
                DateOnly? day = null;
                if (!string.IsNullOrWhiteSpace(date))
                {
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = new[] { "Use the date format yyyy-MM-dd." } });
                    day = parsed;
                }
                return Results.Ok(await notes.Summaries(await notes.ForDay(day)));
            })
            .RequireAuthorization(p => p.RequireRole("manager", "admin"))
            .WithTags("Billing")
            .WithName("GetCreditNotes").WithSummary("Credit notes issued on one business day (default today)");

            return app;
        }

        public static IResult ApprovalProblem(ApprovalException ex) =>
            Results.Json(new { message = ex.Message, code = ex.Code }, statusCode: ex.Status);

        private static async Task<IResult> Run(Func<Task<IResult>> action)
        {
            try
            {
                return await action();
            }
            catch (ApprovalException ex)
            {
                return ApprovalProblem(ex);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (OrderConflictException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { message = "This bill was changed on another screen." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }
    }
}
