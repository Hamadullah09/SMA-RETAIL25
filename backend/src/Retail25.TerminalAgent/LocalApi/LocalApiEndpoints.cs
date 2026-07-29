using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Peripherals;
using Retail25.TerminalAgent.Rfid;
using Retail25.TerminalAgent.Server;
using Retail25.TerminalAgent.Spooling;

namespace Retail25.TerminalAgent.LocalApi;

/// <summary>
/// The loopback API on 127.0.0.1:8477 (doc 06 §5).
/// <para>
/// It exists for exactly two kinds of thing: actions with no server-side meaning — reading the scale,
/// a printer self-test — and actions where the sub-100 ms round trip matters. Everything that moves
/// money or stock goes through the server, because those need to be permission-checked and auditable,
/// and a browser talking straight to hardware is neither.
/// </para>
/// <para>
/// Bound to loopback only, so nothing off this machine can reach it.
/// </para>
/// </summary>
internal static class LocalApiEndpoints
{
    public static void MapLocalApi(this WebApplication app)
    {
        var group = app.MapGroup("/").AddEndpointFilter<LoopbackOnlyFilter>();

        // Doubles as the health probe for the Windows service (doc 06 §7).
        group.MapGet("/status", (
            RfidReaderService reader,
            PeripheralCoordinator peripherals,
            IServerConnection server,
            ProfileStore profiles,
            TagBuffer buffer) => Results.Ok(new
            {
                agentVersion = AgentVersion.Current,
                station = profiles.Current?.StationCode,
                serverConnected = server.IsConnected,
                readerMode = profiles.Mode.ToString(),
                reader = new { online = reader.ReaderOnline, device = reader.ReaderDescription, buffered = buffer.Count },
                printer = new { online = peripherals.PrinterOnline },
                scale = new { online = peripherals.ScaleOnline },
                drawer = new { online = peripherals.DrawerOnline },
                poleDisplay = new { online = peripherals.PoleDisplayOnline },
            }));

        // The F-key "Get Weight" path. Latency is why this is local: a cashier holding a bag on the
        // platter should see the number immediately, not after a server round trip.
        group.MapPost("/scale/weight", async (PeripheralCoordinator peripherals, CancellationToken ct) =>
        {
            await peripherals.RequestWeightAsync(ct);
            return Results.Accepted();
        });

        group.MapPost("/scale/zero", async (PeripheralCoordinator peripherals, CancellationToken ct) =>
        {
            await peripherals.ZeroScaleAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/print/test", async (PeripheralCoordinator peripherals, CancellationToken ct) =>
        {
            await peripherals.PrintTestAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/pole-display", async (
            PoleRequest request,
            PeripheralCoordinator peripherals,
            CancellationToken ct) =>
        {
            await peripherals.DisplayPoleAsync(request.Line1 ?? string.Empty, request.Line2 ?? string.Empty, ct);
            return Results.NoContent();
        });

        group.MapPost("/reader/mode", async (
            ReaderModeRequest request,
            RfidReaderService reader,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<ReaderMode>(request.Mode, ignoreCase: true, out var mode))
            {
                return Results.BadRequest(new { code = "reader.mode_invalid" });
            }

            await reader.ApplyModeAsync(mode, ct);
            return Results.NoContent();
        });

        // The drawer is deliberately NOT exposed here for sales. A pop must be permission-checked and
        // must land in the drawer ledger, so the browser asks the server, which asks the agent.

        group.MapGet("/spool", async (ITagSpool spool, CancellationToken ct) =>
            Results.Ok(new { pending = await spool.CountAsync(ct) }));
    }

    /// <summary>
    /// Refuses anything that did not come from this machine. The listener is already bound to
    /// loopback; this is the second lock, because a misread configuration that binds 0.0.0.0 would
    /// otherwise expose a till's hardware to the shop network.
    /// </summary>
    private sealed class LoopbackOnlyFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var remote = context.HttpContext.Connection.RemoteIpAddress;

            if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return await next(context);
        }
    }

    internal sealed record PoleRequest(string? Line1, string? Line2);

    internal sealed record ReaderModeRequest(string Mode);
}
