using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace POSSystem.Hubs
{
    public class OrderHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var tenant = Context.User?.FindFirst("tenant_id")?.Value ?? "1";
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant-{tenant}");
            await base.OnConnectedAsync();
        }
    }
}
