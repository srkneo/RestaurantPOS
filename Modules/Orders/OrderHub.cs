using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace POSSystem.Hubs
{
    public class OrderHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            // No restaurant on the sign-in (for example the platform owner): no restaurant's live updates at all.
            var tenant = Context.User?.FindFirst("tenant_id")?.Value;
            if (!string.IsNullOrEmpty(tenant) && tenant != "0")
                await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant-{tenant}");
            await base.OnConnectedAsync();
        }
    }
}
