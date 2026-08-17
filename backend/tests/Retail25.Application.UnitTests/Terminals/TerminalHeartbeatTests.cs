using FluentAssertions;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Terminals;
using Retail25.Application.UnitTests.Carts;
using Retail25.Domain.Terminals;
using Xunit;

namespace Retail25.Application.UnitTests.Terminals;

/// <summary>
/// What the agent's heartbeat tells the tag reader panel.
/// <para>
/// These exist because nothing did. <c>PublishStatusAsync</c> was written, reviewed and shipped
/// without a single caller, so the reader feed's status was never broadcast at all — and because no
/// test asserted that it was, nothing failed. The panel degraded silently to its defaults: a read
/// rate that showed an em dash forever, and a reader reported as "Reading" whenever the hub was up,
/// including when the antenna was switched off.
/// </para>
/// <para>
/// A shop hit exactly that. The reader was connected, the screen said Reading, the cashier held a
/// tag against it and nothing happened, because the mode was Off and no screen anywhere said so.
/// </para>
/// </summary>
public sealed class TerminalHeartbeatTests
{
    [Fact]
    public async Task The_heartbeat_tells_the_reader_feed_what_the_reader_is_doing()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = Handlers(harness);

        var result = await handlers.Handle(Beat(harness, readerOnline: true, readRate: 12), default);

        result.IsSuccess.Should().BeTrue();

        await harness.RfidNotifier.Received(1).ReaderStatusAsync(
            harness.Location.Id,
            harness.Station.Id,
            Arg.Is<RfidReaderStatus>(s => s.Connected && s.ReadsPerSecond == 12 && s.Mode == "OnDemand"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The bug itself, pinned: connected and reading are different facts.
    /// <para>
    /// The till used to derive "is it reading" from <c>Connected</c>, which is only "can the agent
    /// talk to it". A reader that is switched off answers that question perfectly well.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_switched_off_reader_is_reported_as_off_even_though_it_is_connected()
    {
        using var harness = await PosTestHarness.CreateAsync();
        harness.Station.SetReaderMode(ReaderMode.Off);
        await harness.Db.SaveChangesAsync();

        var handlers = Handlers(harness);

        await handlers.Handle(Beat(harness, readerOnline: true, readRate: 0), default);

        await harness.RfidNotifier.Received(1).ReaderStatusAsync(
            harness.Location.Id,
            harness.Station.Id,
            Arg.Is<RfidReaderStatus>(s => s.Connected && s.Mode == "Off"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A reader the agent cannot reach is reported unreachable rather than merely quiet. Both end up
    /// as "no tag is coming", but they are different faults with different fixes, and the panel is
    /// the only place anyone will see the difference.
    /// </summary>
    [Fact]
    public async Task A_reader_the_agent_cannot_reach_is_reported_as_disconnected()
    {
        using var harness = await PosTestHarness.CreateAsync();
        var handlers = Handlers(harness);

        await handlers.Handle(Beat(harness, readerOnline: false, readRate: 0), default);

        await harness.RfidNotifier.Received(1).ReaderStatusAsync(
            harness.Location.Id,
            harness.Station.Id,
            Arg.Is<RfidReaderStatus>(s => !s.Connected),
            Arg.Any<CancellationToken>());
    }

    private static TerminalHandlers Handlers(PosTestHarness harness) => new(
        harness.Db,
        harness.Notifier,
        harness.TerminalNotifier,
        harness.Clock,
        harness.TagFeed);

    private static ReportAgentStatusCommand Beat(PosTestHarness harness, bool readerOnline, int readRate) => new(
        harness.Station.Id,
        AgentVersion: "1.0.0",
        ReaderOnline: readerOnline,
        PrinterOnline: false,
        ScaleOnline: false,
        DrawerOnline: false,
        PoleDisplayOnline: false,
        ReadRate: readRate);
}
