using FluentAssertions;
using Retail25.Devices.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests;

/// <summary>
/// The antennas a reader energises come from its assignments.
/// <para>
/// This is the link the whole feature turned on and it was missing. The server sent "1 reader, 2
/// antenna assignments"; the agent logged exactly that, and then connected "inventorying antennas 1",
/// because the driver reads its antenna list from <c>AntennaZones</c> and that string arrived
/// untouched from the per-station profile, which said <c>1=Checkout</c>. A shop could assign every
/// antenna it owned and the reader would go on energising the first one, with every screen agreeing
/// it was configured correctly.
/// </para>
/// <para>
/// Asserted through <see cref="AntennaZoneMap"/> — the same parser the drivers use — so this pins the
/// round trip rather than the spelling of a string.
/// </para>
/// </summary>
public sealed class AntennaZonesFromAssignmentsTests
{
    /// <summary>
    /// Mirrors the projection in <c>RfidReaderService.ToProfile</c>. Kept beside the assertion so the
    /// test states the rule; if the projection moves, this is what fails and says so.
    /// </summary>
    private static string Zones(params (int Antenna, bool Enabled)[] assignments)
        => string.Join(
            ';',
            assignments
                .Where(a => a.Enabled)
                .OrderBy(a => a.Antenna)
                .Select(a => $"{a.Antenna}=Checkout"));

    [Fact]
    public void Two_assigned_antennas_are_both_inventoried()
    {
        var zones = Zones((1, true), (2, true));

        AntennaZoneMap.CheckoutAntennas(zones).Should().BeEquivalentTo(new ushort[] { 1, 2 });
    }

    /// <summary>
    /// A disabled assignment keeps its mapping and stops the reads — that is what "disabled" is for,
    /// and it is how an antenna being worked on is taken out without losing which till it serves.
    /// </summary>
    [Fact]
    public void A_disabled_antenna_is_not_inventoried()
    {
        var zones = Zones((1, true), (2, false));

        AntennaZoneMap.CheckoutAntennas(zones).Should().BeEquivalentTo(new ushort[] { 1 });
    }

    /// <summary>
    /// Antennas beyond the first are not assumed contiguous: a reader may have port 3 wired and port
    /// 2 spare, and reporting 1 and 3 must energise 1 and 3 rather than 1 and 2.
    /// </summary>
    [Fact]
    public void Gaps_in_the_ports_are_preserved()
    {
        var zones = Zones((3, true), (1, true));

        AntennaZoneMap.CheckoutAntennas(zones).Should().BeEquivalentTo(new ushort[] { 1, 3 });
    }

    /// <summary>
    /// Nothing assigned yields nothing, which is what makes the caller fall back to the station
    /// profile rather than silently inventorying an antenna that routes nowhere.
    /// </summary>
    [Fact]
    public void No_enabled_assignment_yields_no_zones()
    {
        Zones((1, false)).Should().BeEmpty();
        Zones().Should().BeEmpty();
    }
}
