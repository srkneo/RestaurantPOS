using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using POSSystem.Data;

namespace POSSystem.Platform
{
    public static class AboutEndpoints
    {
        public static WebApplication MapAboutEndpoints(this WebApplication app)
        {
            // Open to everyone, because the footer shows it on the sign-in page too. The licence part only for signed-in staff.
            app.MapGet("/api/about", async (HttpContext http, SupportService support, LicenceService licence, IConfiguration config, CancellationToken ct) =>
            {
                var s = await support.Get(ct);
                var version = config["App:Version"];
                if (string.IsNullOrWhiteSpace(version)) version = "dev";
                string? customer = null, plan = null, device = null;
                if (licence.Required && http.User.Identity?.IsAuthenticated == true)
                {
                    var status = await licence.GetStatus(ct);
                    customer = status.Customer;
                    plan = status.Plan;
                    device = await licence.DeviceCode(ct);
                }
                return Results.Ok(new AboutDto(s.CompanyName, s.ProductName, s.Phone, s.WhatsApp, s.Email, s.Hours, s.Website,
                    version, licence.Required, customer, plan, device));
            })
                .AllowAnonymous().WithTags("About")
                .WithName("GetAbout").WithSummary("Support details, the app version and, on a shop PC, its licence");

            var group = app.MapGroup("/api/platform/support").RequireAuthorization(p => p.RequireRole(AuthSetup.PlatformRole)).WithTags("Platform");

            group.MapGet("/", async (SupportService support, CancellationToken ct) => Results.Ok(await support.Get(ct)))
                .WithName("GetSupportDetails").WithSummary("The support details every restaurant sees");

            group.MapPut("/", async (SupportInfoDto request, SupportService support, CancellationToken ct) =>
            {
                var errors = SupportService.Validate(request);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                return Results.Ok(await support.Save(request, ct));
            })
                .WithName("SaveSupportDetails").WithSummary("Change the support details (shop PCs get them with their next licence renewal)");

            return app;
        }
    }
}
