using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Retail25.Api.Hubs;

/// <summary>
/// Inventory browse-grid hub (doc 05 §SignalR). Real-time row patching for browse windows.
/// Groups: grid:{entity}:{filterHash}, location:{id}.
/// </summary>
[Authorize]
public class InventoryHub : Hub
{
    public async Task SubscribeToGrid(string entity, string filterHash)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"grid:{entity}:{filterHash}");
    }

    public async Task UnsubscribeFromGrid(string entity, string filterHash)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"grid:{entity}:{filterHash}");
    }

    public async Task JoinLocation(string locationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"location:{locationId}");
    }
}
