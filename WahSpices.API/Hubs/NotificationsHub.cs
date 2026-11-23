using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WahSpices.API.Hubs;

/// <summary>
/// Real-time channel for customer notifications (/hubs/notifications). Server → client only:
///   "notification"        a new broadcast for this user (same shape as GET /api/notifications items)
///   "broadcast-ended"     { id } — stop showing its banner (stays in the bell until read)
///   "broadcast-removed"   { id } — deleted by the admin, drop it everywhere
///   "notification-state"  { id?, all?, readAt?, bannerSeenAt? } — another tab/device read or closed something
/// Auth: the customer's access token (Authorization header, or ?access_token= for WebSockets — accepted on this path
/// only). The connection closes when that 15-minute token expires; the client reconnects with a refreshed token.
/// Groups: "u:{userId}" per customer, "customers" for broadcasts that go to everyone.
/// </summary>
[Authorize(Policy = "CustomerOnly")]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";
    public const string AllCustomersGroup = "customers";
    public static string UserGroup(int userId) => $"u:{userId}";

    public override async Task OnConnectedAsync()
    {
        if (int.TryParse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(uid));
            await Groups.AddToGroupAsync(Context.ConnectionId, AllCustomersGroup);
        }
        else
        {
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }
}
