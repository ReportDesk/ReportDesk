using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace ReportSystem.Hubs;

[Authorize]
public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue("reportdesk_user_id");
        if (int.TryParse(userId, out var parsedUserId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{parsedUserId}");

        await base.OnConnectedAsync();
    }
}
