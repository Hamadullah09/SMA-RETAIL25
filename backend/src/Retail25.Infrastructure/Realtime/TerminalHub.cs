using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Retail25.Application.Rfid.Commands;
using Retail25.Contracts.Terminals;
using Retail25.Application.Terminals;

namespace Retail25.Infrastructure.Realtime;

/// <summary>
/// The agent's channel (doc 06 §5). One connection per POS machine, joined to its own station group
/// so a print command can never reach the wrong till.
/// <para>
/// Everything arriving here is treated as a request, not a fact: tag reads go through the same
/// <c>IngestTagReads</c> command an HTTP caller would use, so the debounce, the EPC state checks and
/// the audit trail apply identically whichever route a read came in by.
/// </para>
/// </summary>
[Authorize]
public sealed class TerminalHub : Hub
{
    private readonly ISender _sender;

    public TerminalHub(ISender sender) => _sender = sender;

    /// <summary>The agent announces which station it is, and joins that station's group.</summary>
    public async Task RegisterStation(string stationId, string? agentVersion)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, PosGroups.Station(stationId));

        if (long.TryParse(stationId, out var id))
        {
            await _sender.Send(
                new ReportAgentStatusCommand(id, agentVersion, false, false, false, false, false, 0),
                Context.ConnectionAborted);
        }
    }

    /// <summary>A batch of tags from the reader, already coalesced and pre-filtered by the agent.</summary>
    public async Task PublishTags(string stationId, IReadOnlyList<TagRead> tags)
    {
        if (!long.TryParse(stationId, out var id) || tags is null || tags.Count == 0)
        {
            return;
        }

        await _sender.Send(new IngestTagReadsCommand(id, tags), Context.ConnectionAborted);
    }

    public async Task ReportWeight(string stationId, decimal value, string unit, bool stable)
    {
        if (long.TryParse(stationId, out var id))
        {
            await _sender.Send(new ReportWeightCommand(id, value, unit, stable), Context.ConnectionAborted);
        }
    }

    public async Task ReportStatus(
        string stationId,
        string? agentVersion,
        bool readerOnline,
        bool printerOnline,
        bool scaleOnline,
        bool drawerOnline,
        bool poleDisplayOnline,
        int readRate)
    {
        if (long.TryParse(stationId, out var id))
        {
            await _sender.Send(
                new ReportAgentStatusCommand(
                    id, agentVersion, readerOnline, printerOnline, scaleOnline, drawerOnline, poleDisplayOnline, readRate),
                Context.ConnectionAborted);
        }
    }

    /// <summary>
    /// The agent reports what happened to a print job. A failure is not fatal — the sale is already
    /// saved, and the receipt stays reprintable, which is the legacy "printer jammed" story (guide p.12).
    /// </summary>
    public Task ReportPrintResult(string stationId, long transactionId, bool succeeded, string? error)
        => Clients.Group(PosGroups.Station(stationId))
            .SendAsync("PrintResult", new { transactionId, succeeded, error }, Context.ConnectionAborted);
}
