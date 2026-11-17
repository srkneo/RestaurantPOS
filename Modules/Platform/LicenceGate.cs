using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace POSSystem.Platform
{
    // While the PC is locked, nothing can be changed (no orders, bills or settings). Reading, signing in and the licence page stay open.
    public class LicenceGate
    {
        private readonly RequestDelegate _next;

        public LicenceGate(RequestDelegate next) => _next = next;

        public async Task Invoke(HttpContext context, LicenceService licence)
        {
            var method = context.Request.Method;
            var reading = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
            var path = context.Request.Path;
            var open = path.StartsWithSegments("/api/licence") || path.StartsWithSegments("/api/auth") || path.StartsWithSegments("/orderHub");

            if (!reading && !open && licence.Required)
            {
                var status = await licence.GetStatus(context.RequestAborted);
                if (status.Locked)
                {
                    // 402: payment or licence needed. The offline queue in the browser keeps the order for later.
                    context.Response.StatusCode = 402;
                    await context.Response.WriteAsJsonAsync(new { message = status.Message, code = "licence-locked" });
                    return;
                }
            }
            await _next(context);
        }
    }
}
