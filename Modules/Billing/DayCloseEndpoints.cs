using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using POSSystem.Interfaces;

namespace POSSystem.Billing
{
    public static class DayCloseEndpoints
    {
        private static readonly string[] Till = { "cashier", "manager", "admin" };
        private static readonly string[] Managers = { "manager", "admin" };

        public static WebApplication MapDayCloseEndpoints(this WebApplication app)
        {
            var shifts = app.MapGroup("/api/shifts").WithTags("Day close");

            shifts.MapGet("/current", async (DayCloseService service) =>
                Results.Ok(new CurrentShiftDto(await service.Current() is { } shift ? await service.Describe(shift) : null)))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("GetCurrentShift").WithSummary("The open till shift with the cash expected now (shift is null when none is open)");

            shifts.MapPost("/open", (OpenShiftRequest request, DayCloseService service) =>
                Run(async () => Results.Ok(await service.Describe(await service.Open(request)))))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("OpenShift").WithSummary("Open a till shift with the cash in the drawer");

            shifts.MapPost("/current/movements", (CashMovementRequest request, DayCloseService service) =>
                Run(async () => Results.Ok(await service.Describe(await service.AddMovement(request)))))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("AddCashMovement").WithSummary("Put cash into the drawer or take it out, with a reason");

            shifts.MapPost("/current/close", (CloseShiftRequest request, DayCloseService service) =>
                Run(async () => Results.Ok(await service.Describe(await service.Close(request)))))
                .RequireAuthorization(p => p.RequireRole(Till))
                .WithName("CloseShift").WithSummary("Close the shift with the cash counted; shows short or excess");

            shifts.MapGet("/", (string? date, DayCloseService service) =>
                WithDate(date, async day =>
                {
                    var list = new List<ShiftDto>();
                    foreach (var shift in await service.GetShifts(day)) list.Add(await service.Describe(shift));
                    return Results.Ok(list);
                }))
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithName("GetShifts").WithSummary("Till shifts of one business day (default today)");

            app.MapGet("/api/reports/day", (string? date, DayCloseService service) =>
                WithDate(date, async day => Results.Ok(await service.DayReport(day))))
                .RequireAuthorization(p => p.RequireRole(Managers))
                .WithTags("Day close")
                .WithName("GetDayReport").WithSummary("End-of-day (Z) report: sales, GST per rate, payments, void bills, shifts");

            return app;
        }

        private static async Task<IResult> WithDate(string? date, Func<DateOnly?, Task<IResult>> action)
        {
            if (string.IsNullOrWhiteSpace(date)) return await action(null);
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = new[] { "Use the date format yyyy-MM-dd." } });
            return await action(day);
        }

        private static async Task<IResult> Run(Func<Task<IResult>> action)
        {
            try
            {
                return await action();
            }
            catch (OrderConflictException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { message = "This shift was changed on another screen. Check the amounts again." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }
    }
}
