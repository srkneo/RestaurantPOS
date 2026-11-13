using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RestaurantPOS.Tests
{
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers.TryGetValue("X-Test-Role", out var r) ? r.ToString() : "admin";
            var tenant = Request.Headers.TryGetValue("X-Test-Tenant", out var t) ? t.ToString() : "1";
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "test@pos.local"),
                new Claim(ClaimTypes.Email, "test@pos.local"),
                new Claim(ClaimTypes.Role, role),
                new Claim("tenant_id", tenant),
                new Claim("outlet_id", "1")
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
