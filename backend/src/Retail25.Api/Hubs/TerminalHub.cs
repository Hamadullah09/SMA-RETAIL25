using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Retail25.Api.Hubs;

/// <summary>
/// Terminal agent hub (doc 06 §4). Server → agent: PrintReceipt, OpenDrawer, DisplayPole, RequestWeight.
/// Agent → server: PublishTags, ReportWeight, ReportStatus.
/// </summary>
[Authorize]
public class TerminalHub : Hub
{
    public async Task PublishTags(object[] tags)
    {
        // Agent sends batched RFID tag reads. Server processes and broadcasts to POS clients.
        await Clients.All.SendAsync("TagsReceived", tags);
    }

    public async Task ReportWeight(decimal value, string unit)
    {
        await Clients.All.SendAsync("WeightReported", value, unit);
    }

    public async Task ReportStatus(object status)
    {
        await Clients.All.SendAsync("AgentStatusReported", status);
    }

    public async Task SendPrintReceipt(string stationId, object payload)
    {
        await Clients.Group($"station:{stationId}").SendAsync("PrintReceipt", payload);
    }

    public async Task SendOpenDrawer(string stationId)
    {
        await Clients.Group($"station:{stationId}").SendAsync("OpenDrawer");
    }

    public async Task SendDisplayPole(string stationId, string line1, string line2)
    {
        await Clients.Group($"station:{stationId}").SendAsync("DisplayPole", line1, line2);
    }
}
