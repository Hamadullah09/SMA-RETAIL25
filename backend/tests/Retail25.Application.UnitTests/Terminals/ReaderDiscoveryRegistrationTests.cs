using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Terminals;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Configuration;
using Retail25.Domain.Terminals;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.Application.UnitTests.Terminals;

/// <summary>
/// What a sweep is allowed to change about the reader list.
///
/// <para>
/// The whole value of automatic discovery rests on one thing: an administrator assigns antennas to
/// stations once, and a reader moving address never undoes that work. These tests are that promise,
/// stated as behaviour rather than as a comment.
/// </para>
/// </summary>
public sealed class ReaderDiscoveryRegistrationTests
{
    private const long LocationId = 1L;
    private const string DeviceKey = "PC-001";

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"discovery-{Guid.NewGuid():N}")
            .ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed record Harness(
        ApplicationDbContext Db,
        RecordReaderDiscoveryHandler Handler,
        IRfidNotifier Notifier);

    private static async Task<Harness> HarnessAsync()
    {
        var db = NewDb();

        db.Locations.Add(Location.Create("Test Store", "TST", "PKR", "UTC", TimeOnly.MinValue).Value);
        db.Devices.Add(Device.Create(LocationId, DeviceKey, "Front counter PC").Value);

        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTime>();
        clock.Now.Returns(DateTimeOffset.UtcNow);

        var notifier = Substitute.For<IRfidNotifier>();

        return new Harness(db, new RecordReaderDiscoveryHandler(db, clock, notifier), notifier);
    }

    private static DiscoveredReaderContract Sighting(
        string host,
        string? serial,
        int antennas = 4,
        bool reported = true)
        => new(host, 4001, "UhfSerial", serial, "8.2", antennas, reported);

    private static RecordReaderDiscoveryCommand Report(params DiscoveredReaderContract[] readers)
        => new(LocationId, DeviceKey, readers);

    [Fact]
    public async Task A_reader_nobody_has_seen_before_is_registered()
    {
        var (db, handler, _) = await HarnessAsync();

        var result = await handler.Handle(Report(Sighting("192.168.1.101", "ABC123")), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Added.Should().Be(1);
        result.Value.Found.Should().Be(1);

        var reader = await db.RfidReaders.SingleAsync();
        reader.SerialNumber.Should().Be("ABC123");
        reader.Host.Should().Be("192.168.1.101");
        reader.ReaderKey.Should().Be("RFID-001", "a discovered reader gets a name a person can say aloud");
    }

    [Fact]
    public async Task Seven_readers_are_all_registered_and_named_apart()
    {
        var (db, handler, _) = await HarnessAsync();

        var sightings = Enumerable.Range(1, 7)
            .Select(n => Sighting($"192.168.1.{100 + n}", $"SERIAL-{n:D2}"))
            .ToArray();

        var result = await handler.Handle(Report(sightings), CancellationToken.None);

        result.Value.Added.Should().Be(7);

        var readers = await db.RfidReaders.ToListAsync();
        readers.Should().HaveCount(7);
        readers.Select(r => r.ReaderKey).Should().OnlyHaveUniqueItems("two readers sharing a key is one reader");
        readers.Select(r => r.SerialNumber).Should().BeEquivalentTo(sightings.Select(s => s.SerialNumber));
    }

    /// <summary>
    /// Scenario C, and the reason serial number is the identity rather than the address.
    /// </summary>
    [Fact]
    public async Task A_reader_that_changed_address_keeps_its_antenna_assignments()
    {
        var (db, handler, _) = await HarnessAsync();

        await handler.Handle(Report(Sighting("192.168.1.101", "ABC123")), CancellationToken.None);

        var reader = await db.RfidReaders.SingleAsync();

        // An administrator points antenna 2 at a till. This is the work that must survive.
        db.ReaderAntennaAssignments.Add(ReaderAntennaAssignment.Create(reader.Id, 2, stationId: 42L).Value);
        await db.SaveChangesAsync();

        // Overnight the router hands out a different lease. Same box, same serial, new address.
        var result = await handler.Handle(Report(Sighting("192.168.1.150", "ABC123")), CancellationToken.None);

        result.Value.Added.Should().Be(0, "the same serial is the same reader, however it is addressed");
        result.Value.Moved.Should().Be(1);

        (await db.RfidReaders.CountAsync()).Should().Be(1, "a lease change must not spawn a second reader");

        var after = await db.RfidReaders.SingleAsync();
        after.Id.Should().Be(reader.Id);
        after.Host.Should().Be("192.168.1.150", "the new address is recorded");

        var assignment = await db.ReaderAntennaAssignments.SingleAsync();
        assignment.ReaderId.Should().Be(reader.Id);
        assignment.AntennaNumber.Should().Be(2);
        assignment.StationId.Should().Be(42L, "the administrator's work survives the move untouched");
    }

    /// <summary>
    /// The other half of identity: a genuinely different reader that happens to inherit the old
    /// one's address must not inherit its assignments with it.
    /// </summary>
    [Fact]
    public async Task A_different_reader_at_a_familiar_address_is_a_different_reader()
    {
        var (db, handler, _) = await HarnessAsync();

        await handler.Handle(Report(Sighting("192.168.1.101", "ABC123")), CancellationToken.None);
        var result = await handler.Handle(Report(Sighting("192.168.1.101", "XYZ789")), CancellationToken.None);

        result.Value.Added.Should().Be(1);
        (await db.RfidReaders.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_reader_that_was_not_seen_this_time_is_left_alone()
    {
        var (db, handler, _) = await HarnessAsync();

        await handler.Handle(
            Report(Sighting("192.168.1.101", "ABC123"), Sighting("192.168.1.102", "DEF456")),
            CancellationToken.None);

        // The second reader is mid-reboot and answers nothing. A sweep proves presence, never absence.
        await handler.Handle(Report(Sighting("192.168.1.101", "ABC123")), CancellationToken.None);

        (await db.RfidReaders.CountAsync()).Should().Be(2, "a missed scan is not evidence a reader is gone");
    }

    [Fact]
    public async Task An_antenna_count_the_reader_reported_is_taken_over_the_default()
    {
        var (db, handler, _) = await HarnessAsync();

        await handler.Handle(
            Report(Sighting("192.168.1.101", "ABC123", antennas: 8, reported: true)),
            CancellationToken.None);

        (await db.RfidReaders.SingleAsync()).AntennaCount.Should().Be(8);
    }

    /// <summary>
    /// A silent reader must not shrink a count somebody already corrected. Shrinking it would strand
    /// every assignment on the ports it removed.
    /// </summary>
    [Fact]
    public async Task An_assumed_antenna_count_never_narrows_a_known_one()
    {
        var (db, handler, _) = await HarnessAsync();

        await handler.Handle(
            Report(Sighting("192.168.1.101", "ABC123", antennas: 8, reported: true)),
            CancellationToken.None);

        // The next sweep catches the reader in a mood where it will not say, so the agent fell back
        // to the family default of four.
        await handler.Handle(
            Report(Sighting("192.168.1.101", "ABC123", antennas: 4, reported: false)),
            CancellationToken.None);

        (await db.RfidReaders.SingleAsync()).AntennaCount.Should().Be(8);
    }

    /// <summary>
    /// Two readers that report no serial at all must stay two readers.
    /// <para>
    /// This is the shape a new shop actually arrives in. Readers leave the factory with the identifier
    /// field unwritten, so a pair of them reports nothing rather than something unique, and identity
    /// falls back to the address. Collapsing them would list one reader where two are screwed to the
    /// wall, and route one box's reads to the other box's till.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_readers_with_no_serial_at_different_addresses_are_two_readers()
    {
        var (db, handler, _) = await HarnessAsync();

        var result = await handler.Handle(
            Report(Sighting("192.168.0.178", null), Sighting("192.168.0.179", null)),
            CancellationToken.None);

        result.Value.Added.Should().Be(2);

        var readers = await db.RfidReaders.ToListAsync();
        readers.Should().HaveCount(2);
        readers.Select(r => r.Host).Should().BeEquivalentTo(["192.168.0.178", "192.168.0.179"]);
        readers.Select(r => r.ReaderKey).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// The other half: a reader with no serial is still followed by address, because that is the only
    /// identity it has. A second sighting at the same place is the same reader, not a new one.
    /// </summary>
    [Fact]
    public async Task A_reader_with_no_serial_is_recognised_again_at_the_same_address()
    {
        var (db, handler, _) = await HarnessAsync();

        await handler.Handle(Report(Sighting("192.168.0.178", null)), CancellationToken.None);
        var result = await handler.Handle(Report(Sighting("192.168.0.178", null)), CancellationToken.None);

        result.Value.Added.Should().Be(0);
        (await db.RfidReaders.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Two readers may never be recorded on one address, however confidently a till reports it.
    /// <para>
    /// One socket, two readers, is physically impossible — and a shop reached it. A till that could
    /// not reach its reader fell back to searching, adopted the first address that accepted a
    /// connection, and reported that as where it had found itself. The address belonged to another
    /// reader. Both rows landed on it, both sessions fought over the one socket, and neither read a
    /// tag again until somebody edited the database.
    /// </para>
    /// <para>
    /// The agent no longer wanders like that, but this is the layer that persists: a wrong address
    /// written once outlives every restart, so it is refused here as well.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_reader_is_not_moved_onto_an_address_another_reader_already_holds()
    {
        var (db, handler, _) = await HarnessAsync();

        await handler.Handle(
            Report(Sighting("192.168.0.178", "AAA"), Sighting("192.168.0.179", "BBB")),
            CancellationToken.None);

        var clock = Substitute.For<IDateTime>();
        clock.Now.Returns(DateTimeOffset.UtcNow);

        var registry = new DeviceRegistryHandlers(
            db,
            clock,
            Substitute.For<IRfidNotifier>(),
            NullLogger<DeviceRegistryHandlers>.Instance);

        var keys = await db.RfidReaders.OrderBy(r => r.ReaderKey).Select(r => r.ReaderKey).ToListAsync();

        // The second reader claims it found itself at the first one's address.
        await registry.Handle(
            new ReportDeviceStatusCommand(
                LocationId,
                DeviceKey,
                "PC",
                null,
                null,
                "0.1",
                [new ReaderHealthReport(keys[1], null, Connected: true, "192.168.0.178", 4001)]),
            CancellationToken.None);

        var readers = await db.RfidReaders.OrderBy(r => r.ReaderKey).ToListAsync();

        readers[1].Host.Should().Be(
            "192.168.0.179",
            "the address was already taken, so the claim is refused rather than written");

        readers.Select(r => r.Host).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Findings_from_a_machine_nobody_registered_are_refused()
    {
        var (_, handler, _) = await HarnessAsync();

        var result = await handler.Handle(
            new RecordReaderDiscoveryCommand(LocationId, "PC-UNKNOWN", [Sighting("192.168.1.101", "ABC123")]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("device.not_found");
    }
}
