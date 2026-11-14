using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;

namespace POSSystem.Billing
{
    public static class BillEndpoints
    {
        private static readonly string[] Till = { "cashier", "manager", "admin" };

        public static WebApplication MapBillEndpoints(this WebApplication app)
        {
            app.MapPost("/api/orders/{id:int}/bill", (int id, CreateBillRequest? request, BillService bills) =>
                Run(async () =>
                {
                    var bill = await bills.CreateBill(id, request ?? new CreateBillRequest(null, null));
                    return Results.Created($"/api/bills/{bill.Id}", BillService.ToDto(bill));
                }))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("CreateBill").WithSummary("Make the bill for an order (optional discount in percent or rupees)");

            app.MapGet("/api/orders/{id:int}/bill", async (int id, BillService bills) =>
                await bills.GetActiveBillForOrder(id) is { } bill ? Results.Ok(BillService.ToDto(bill)) : Results.NotFound())
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("GetOrderBill").WithSummary("The current (not void) bill of an order");

            app.MapGet("/api/bills", async (string? date, BillService bills) =>
            {
                DateOnly? day = null;
                if (!string.IsNullOrWhiteSpace(date))
                {
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = new[] { "Use the date format yyyy-MM-dd." } });
                    day = parsed;
                }
                return Results.Ok((await bills.GetBills(day)).Select(BillService.ToSummary));
            })
            .RequireAuthorization(p => p.RequireRole(Till))
            .WithName("GetBills").WithSummary("Bills of one business day (default today), newest first");

            app.MapGet("/api/bills/{id:int}", async (int id, BillService bills) =>
                await bills.GetBill(id) is { } bill ? Results.Ok(BillService.ToDto(bill)) : Results.NotFound())
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("GetBill").WithSummary("One bill with its lines and GST summary");

            app.MapPost("/api/bills/{id:int}/void", (int id, VoidBillRequest request, BillService bills) =>
                Run(async () => Results.Ok(BillService.ToDto(await bills.VoidBill(id, request)))))
                .RequireAuthorization(p => p.RequireRole("manager", "admin"))
                .WithName("VoidBill").WithSummary("Void a bill with a reason (the bill and its number are kept)");

            var settings = app.MapGroup("/api/admin/billing-settings").RequireAuthorization(p => p.RequireRole("admin")).WithTags("Settings");

            settings.MapGet("/", async (AppDbContext db) =>
                await LoadOutlet(db) is { } outlet ? Results.Ok(BillService.SettingsOf(outlet)) : Results.NotFound())
                .WithName("GetBillingSettings").WithSummary("Tax mode, prices with or without GST, invoice prefix");

            settings.MapPut("/", async (BillingSettingsDto dto, AppDbContext db) =>
            {
                var outlet = await LoadOutlet(db);
                if (outlet == null) return Results.NotFound();
                var errors = BillService.ValidateSettings(dto);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                BillService.ApplySettings(outlet, dto);
                await db.SaveChangesAsync();
                return Results.Ok(BillService.SettingsOf(outlet));
            }).WithName("UpdateBillingSettings").WithSummary("Change tax mode, prices with or without GST, invoice prefix");

            return app;
        }

        private static Task<POSSystem.Models.Outlet?> LoadOutlet(AppDbContext db) =>
            db.Outlets.FirstOrDefaultAsync(o => o.Id == db.CurrentOutletId && o.TenantId == db.CurrentTenantId);

        private static async Task<IResult> Run(Func<Task<IResult>> action)
        {
            try
            {
                return await action();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (BillExistsException ex)
            {
                return Results.Conflict(new { message = ex.Message, code = "already-billed", billId = ex.BillId });
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
