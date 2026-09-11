using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Rfid;
using Retail25.Application.Terminals;
using Retail25.Domain.Configuration;
using Retail25.Domain.Terminals;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.Application.UnitTests.Terminals;

/// <summary>
/// What the settings screen is allowed to say about a reader, and — more to the point — what it must
/// not say.
///
/// <para>
/// Five states exist because they need five different responses: switch it on, assign its antennas,
/// go and look at the PC, go and look at the reader, or nothing at all. A screen that collapsed them
/// into one connected light would send somebody to the wrong end of the shop, so each of these tests
/// pins one state against the evidence that is supposed to produce it.
/// </para>
/// </summary>
public sealed class ReaderStateTests
{
    private const long LocationId = 1L;

    private static readonly DateTimeOffset Now = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"reader-state-{Guid.NewGuid():N}")
            .ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed record Harness(ApplicationDbContext Db, RfidTopologyAdminHandlers Handlers);

    private static async Task<Harness> HarnessAsync(ReaderConnectionSnapshot? serverHeld = null)
    {
        var db = NewDb();

        db.Locations.Add(Location.Create("Test Store", "TST", "PKR", "UTC", TimeOnly.MinValue).Value);
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTime>();
        clock.Now.Returns(Now);

        var serverReaders = Substitute.For<IReaderConnectionStatus>();
        serverReaders.Current.Returns(serverHeld ?? new ReaderConnectionSnapshot(false, []));

        return new Harness(
            db,
            new RfidTopologyAdminHandlers(db, clock, Substitute.For<IRfidNotifier>(), serverReaders));
    }

    /// <summary>
    /// A machine and the reader it drives, as the two of them look after a check-in.
    /// </summary>
    /// <param name="beatAgo">How long ago the machine last checked in.</param>
    /// <param name="seenAgo">
    /// How long ago the machine last reported holding the reader. Equal to <paramref name="beatAgo"/>
    /// means it held it at that check-in; larger means it has checked in since without it.
    /// </param>
    private static async Task<RfidReader> ReaderDrivenByAgentAsync(
        ApplicationDbContext db,
        TimeSpan beatAgo,
        TimeSpan? seenAgo,
        string key = "RFID-001")
    {
        var device = Device.Create(LocationId, $"PC-{key}", "Counter PC").Value;
        device.LastHeartbeat = Now - beatAgo;
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var reader = RfidReader.Create(LocationId, key, $"SERIAL-{key}", 4).Value;
        reader.MoveTo("192.168.1.101", 4001);
        reader.DeviceId = device.Id;
        reader.LastSeen = seenAgo is { } ago ? Now - ago : null;
        db.RfidReaders.Add(reader);
        await db.SaveChangesAsync();

        return reader;
    }

    private static async Task<ReaderRow> SingleRowAsync(Harness harness)
    {
        var result = await harness.Handlers.Handle(new GetRfidTopologyQuery(LocationId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return result.Value.Readers.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task A_reader_no_machine_has_claimed_is_discovered()
    {
        var harness = await HarnessAsync();

        var reader = RfidReader.Create(LocationId, "RFID-001", "ABC123", 4).Value;
        reader.MoveTo("192.168.1.101", 4001);
        reader.LastSeen = Now;
        harness.Db.RfidReaders.Add(reader);
        await harness.Db.SaveChangesAsync();

        var row = await SingleRowAsync(harness);

        row.State.Should().Be(
            ReaderState.Discovered,
            "a sweep found it, but until a machine drives it and its antennas are assigned it reads nothing");
    }

    [Fact]
    public async Task A_reader_its_machine_reported_holding_is_connected()
    {
        var harness = await HarnessAsync();

        // The agent stamps the heartbeat and the sighting in the same check-in.
        await ReaderDrivenByAgentAsync(harness.Db, beatAgo: TimeSpan.FromSeconds(3), seenAgo: TimeSpan.FromSeconds(3));

        (await SingleRowAsync(harness)).State.Should().Be(ReaderState.Connected);
    }

    [Fact]
    public async Task A_reader_whose_machine_has_gone_quiet_is_offline()
    {
        var harness = await HarnessAsync();

        // Two minutes with no check-in. Nothing can be said about the reader itself.
        await ReaderDrivenByAgentAsync(harness.Db, beatAgo: TimeSpan.FromMinutes(2), seenAgo: TimeSpan.FromMinutes(2));

        (await SingleRowAsync(harness)).State.Should().Be(
            ReaderState.Offline,
            "the fault is upstream of the reader, and walking to the reader would waste the trip");
    }

    /// <summary>
    /// The distinction the whole enum exists for: a live machine that cannot hold the reader is a
    /// different job from a machine that has gone.
    /// </summary>
    [Fact]
    public async Task A_live_machine_that_is_not_holding_the_reader_is_an_error()
    {
        var harness = await HarnessAsync();

        // Checking in every few seconds, but the last time it held the reader was five minutes ago.
        await ReaderDrivenByAgentAsync(harness.Db, beatAgo: TimeSpan.FromSeconds(3), seenAgo: TimeSpan.FromMinutes(5));

        (await SingleRowAsync(harness)).State.Should().Be(ReaderState.Error);
    }

    /// <summary>
    /// The reason the sighting is measured against the heartbeat rather than the wall clock. Both
    /// timestamps here are seconds old, so a wall-clock rule would call this connected — and it is
    /// not: the machine has checked in several times since it last had the reader.
    /// </summary>
    [Fact]
    public async Task A_reader_dropped_moments_ago_does_not_linger_as_connected()
    {
        var harness = await HarnessAsync();

        await ReaderDrivenByAgentAsync(
            harness.Db,
            beatAgo: TimeSpan.Zero,
            seenAgo: TimeSpan.FromSeconds(20));

        (await SingleRowAsync(harness)).State.Should().Be(ReaderState.Error);
    }

    [Fact]
    public async Task A_reader_that_has_never_been_held_by_its_machine_is_an_error_not_connected()
    {
        var harness = await HarnessAsync();

        await ReaderDrivenByAgentAsync(harness.Db, beatAgo: TimeSpan.FromSeconds(2), seenAgo: null);

        (await SingleRowAsync(harness)).State.Should().Be(ReaderState.Error);
    }

    [Fact]
    public async Task A_reader_switched_off_is_disabled_even_while_its_machine_holds_it()
    {
        var harness = await HarnessAsync();

        var reader = await ReaderDrivenByAgentAsync(
            harness.Db,
            beatAgo: TimeSpan.FromSeconds(2),
            seenAgo: TimeSpan.FromSeconds(2));

        reader.SetEnabled(false);
        await harness.Db.SaveChangesAsync();

        (await SingleRowAsync(harness)).State.Should().Be(
            ReaderState.Disabled,
            "somebody switched it off deliberately, and a deliberate choice is not a fault");
    }

    /// <summary>
    /// The other deployment. No agent exists anywhere, and the API on the shop's own network holds
    /// the socket itself — so the screen must not report every reader in the building as offline.
    /// </summary>
    [Fact]
    public async Task A_reader_this_server_is_holding_itself_is_connected_with_no_agent()
    {
        var harness = await HarnessAsync(new ReaderConnectionSnapshot(
            ServerHosted: true,
            [new ReaderConnectionState(7L, "Counter", "192.168.1.101:4001", 1L, Connected: true)]));

        var reader = RfidReader.Create(LocationId, "RFID-001", "ABC123", 4).Value;
        reader.MoveTo("192.168.1.101", 4001);
        harness.Db.RfidReaders.Add(reader);
        await harness.Db.SaveChangesAsync();

        (await SingleRowAsync(harness)).State.Should().Be(ReaderState.Connected);
    }

    [Fact]
    public async Task A_reader_this_server_is_failing_to_hold_is_an_error()
    {
        var harness = await HarnessAsync(new ReaderConnectionSnapshot(
            ServerHosted: true,
            [new ReaderConnectionState(7L, "Counter", "192.168.1.101:4001", 1L, Connected: false)]));

        var reader = RfidReader.Create(LocationId, "RFID-001", "ABC123", 4).Value;
        reader.MoveTo("192.168.1.101", 4001);
        harness.Db.RfidReaders.Add(reader);
        await harness.Db.SaveChangesAsync();

        (await SingleRowAsync(harness)).State.Should().Be(ReaderState.Error);
    }

    /// <summary>
    /// The number a shop actually asks for — how many of my readers are up — counted once on the
    /// server so that two screens cannot answer it differently.
    /// </summary>
    [Fact]
    public async Task The_summary_counts_every_state_across_seven_readers()
    {
        var harness = await HarnessAsync();

        // Four working, one whose PC has gone, one whose PC is fine but cannot hold the reader.
        await ReaderDrivenByAgentAsync(harness.Db, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "RFID-001");
        await ReaderDrivenByAgentAsync(harness.Db, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "RFID-002");
        await ReaderDrivenByAgentAsync(harness.Db, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "RFID-003");
        await ReaderDrivenByAgentAsync(harness.Db, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "RFID-004");
        await ReaderDrivenByAgentAsync(harness.Db, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3), "RFID-005");
        await ReaderDrivenByAgentAsync(harness.Db, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(9), "RFID-006");

        // And one nobody has commissioned yet.
        var fresh = RfidReader.Create(LocationId, "RFID-007", "NEW-ONE", 4).Value;
        fresh.MoveTo("192.168.1.107", 4001);
        harness.Db.RfidReaders.Add(fresh);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Handlers.Handle(new GetRfidTopologyQuery(LocationId), CancellationToken.None);

        var summary = result.Value.Summary;

        summary.Total.Should().Be(7);
        summary.Connected.Should().Be(4);
        summary.Offline.Should().Be(1);
        summary.Error.Should().Be(1);
        summary.Discovered.Should().Be(1);
        summary.Disabled.Should().Be(0);
    }
}
