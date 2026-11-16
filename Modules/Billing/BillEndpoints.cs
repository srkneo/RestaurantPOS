using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Data;
using POSSystem.Interfaces;
using QRCoder;

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

            // A cashier can void too when a manager types their PIN.
            app.MapPost("/api/bills/{id:int}/void", (int id, VoidBillRequest request, ClaimsPrincipal caller, BillService bills, ApprovalService approvals) =>
                Run(async () => Results.Ok(BillService.ToDto(await bills.VoidBill(id, request, await approvals.Approver(caller, request.ApproverPin))))))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("VoidBill").WithSummary("Void a bill with a reason (the bill and its number are kept)");

            app.MapPost("/api/bills/{id:int}/payments", (int id, AddPaymentRequest request, BillService bills) =>
                Run(async () => Results.Ok(BillService.ToDto(await bills.AddPayment(id, request)))))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("AddBillPayment").WithSummary("Record cash, UPI or card money received (several payments = split bill)");

            app.MapGet("/api/bills/{id:int}/upi", (int id, BillService bills) =>
                Run(async () => Results.Ok(await bills.UpiLink(id))))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("GetBillUpiLink").WithSummary("UPI payment link for the amount still due");

            // QR code of the UPI link as an SVG image, for <img src=...> on the payment screen and the printed bill.
            app.MapGet("/api/bills/{id:int}/upi-qr.svg", (int id, BillService bills) =>
                Run(async () =>
                {
                    var upi = await bills.UpiLink(id);
                    using var generator = new QRCodeGenerator();
                    using var data = generator.CreateQrCode(upi.Link, QRCodeGenerator.ECCLevel.M);
                    var svg = new SvgQRCode(data).GetGraphic(6);
                    return Results.Content(svg, "image/svg+xml");
                }))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("GetBillUpiQr").WithSummary("QR code (SVG) of the UPI payment link");

            app.MapPost("/api/bills/{id:int}/printed", (int id, BillService bills) =>
                Run(async () => Results.Ok(new { printCount = await bills.MarkPrinted(id) })))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("MarkBillPrinted").WithSummary("Count a print of the bill (later prints say DUPLICATE)");

            app.MapGet("/api/print-settings", async (AppDbContext db) =>
                await LoadOutlet(db) is { } outlet ? Results.Ok(BillService.PrintSettingsOf(outlet)) : Results.NotFound())
                .RequireAuthorization(p => p.RequireRole("cashier", "manager", "admin", "kitchen"))
                .WithName("GetPrintSettings").WithSummary("Paper width and language for bills and KOTs");

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
            catch (ApprovalException ex)
            {
                return ApprovalEndpoints.ApprovalProblem(ex);
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
