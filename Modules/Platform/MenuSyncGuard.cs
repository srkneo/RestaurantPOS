using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace POSSystem.Platform
{
    // v1.7 menu sync: while this shop PC copies its menu from the cloud, the menu cannot be changed here
    // (it would be overwritten at the next sync). Marking an item sold out stays allowed: that is the shop's decision.
    public static class MenuSyncGuard
    {
        public const string Message = "This menu is managed in the cloud. Change it there; this PC copies it within 5 minutes.";

        public static bool IsMenuChange(HttpRequest request)
        {
            if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)) return false;
            var path = request.Path;
            if (!path.StartsWithSegments("/api/admin/menu") || path.StartsWithSegments("/api/admin/menu/sync")) return false;
            return !(path.Value ?? "").EndsWith("/availability", StringComparison.OrdinalIgnoreCase);
        }

        public static WebApplication UseMenuSyncGuard(this WebApplication app)
        {
            app.Use(async (context, next) =>
            {
                if (IsMenuChange(context.Request)
                    && (await context.RequestServices.GetRequiredService<MenuSyncService>().Status(context.RequestAborted)).ManagedByCloud)
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    await context.Response.WriteAsJsonAsync(new { message = Message });
                    return;
                }
                await next();
            });
            return app;
        }
    }
}
