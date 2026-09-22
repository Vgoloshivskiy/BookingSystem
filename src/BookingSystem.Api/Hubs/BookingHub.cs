using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BookingSystem.Api.Hubs;

/// <summary>
/// Real-time channel for booking status changes. Clients viewing a resource's schedule
/// join a per-resource group; the server pushes an update to that group the instant a
/// booking is created or cancelled, so every viewer sees the change without polling or
/// refreshing. The hub itself never decides booking outcomes — it is purely a broadcast
/// mechanism invoked by BookingService after a booking transaction has committed.
/// </summary>
[Authorize]
public class BookingHub : Hub
{
    private static string ResourceGroup(int resourceId) => $"resource-{resourceId}";

    public async Task JoinResourceGroup(int resourceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, ResourceGroup(resourceId));
    }

    public async Task LeaveResourceGroup(int resourceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ResourceGroup(resourceId));
    }

    public static async Task BroadcastSlotStatusChanged(
        IHubContext<BookingHub> hubContext, int resourceId, int timeSlotId, bool isBooked)
    {
        await hubContext.Clients.Group(ResourceGroup(resourceId))
            .SendAsync("SlotStatusChanged", new { resourceId, timeSlotId, isBooked });
    }
}
