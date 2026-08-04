using FluentAssertions;
using Retail25.Domain.Terminals;
using Xunit;

namespace Retail25.Domain.UnitTests.Terminals;

/// <summary>
/// The reader's local pre-filter — zoning, an RSSI floor, a read-count floor.
/// <para>
/// Every one of these is the difference between a till that rings up the customer's own coat and one
/// that does not, so each is a column an operator edits on site. Which means each is also something
/// an operator can get wrong, and the failure mode that matters is the silent one: a setting that
/// rejects everything while the reader reports itself perfectly healthy.
/// </para>
/// </summary>
public sealed class ReaderProfileTests
{
    private static ReaderProfile Profile(string zones, int rssiFloor = -70, int minimumReads = 2)
    {
        var profile = ReaderProfile.CreateDefault(TestIds.Next());

        profile.AntennaZones = zones;
        profile.RssiThresholdDbm = rssiFloor;
        profile.MinimumReadCount = minimumReads;

        return profile;
    }

    /// <summary>
    /// Both separators work.
    /// <para>
    /// The comma is the regression. Only <c>;</c> was accepted originally, so an operator typing
    /// <c>1=Checkout,2=Checkout</c> — which reads perfectly naturally, and is what a four-antenna
    /// D2184 was actually configured with on this project — left every antenna Unassigned. Every tag
    /// was then filtered, the reader reported healthy, and nothing reached a sale. Nothing in the
    /// logs said why.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("1=Checkout;2=Checkout;3=Exit;4=Exit")]
    [InlineData("1=Checkout,2=Checkout,3=Exit,4=Exit")]
    [InlineData("1=Checkout; 2=Checkout, 3=Exit ,4=Exit")]
    public void Antenna_zones_parse_with_either_separator(string zones)
    {
        var profile = Profile(zones);

        profile.ZoneFor(1).Should().Be(AntennaZone.Checkout);
        profile.ZoneFor(2).Should().Be(AntennaZone.Checkout);
        profile.ZoneFor(3).Should().Be(AntennaZone.Exit);
        profile.ZoneFor(4).Should().Be(AntennaZone.Exit);
    }

    [Fact]
    public void An_unlisted_antenna_feeds_nothing()
    {
        var profile = Profile("1=Checkout");

        profile.ZoneFor(3).Should().Be(AntennaZone.Unassigned);
        profile.Accepts(antenna: 3, rssiDbm: -40, readCount: 10).Should().BeFalse();
    }

    [Fact]
    public void A_read_on_a_checkout_antenna_that_is_loud_and_repeated_is_accepted()
        => Profile("1=Checkout").Accepts(antenna: 1, rssiDbm: -55, readCount: 3).Should().BeTrue();

    [Fact]
    public void An_exit_antenna_does_not_feed_the_cart()
        => Profile("1=Checkout;2=Exit").Accepts(antenna: 2, rssiDbm: -40, readCount: 10).Should().BeFalse();

    [Fact]
    public void A_read_quieter_than_the_floor_is_refused()
        => Profile("1=Checkout").Accepts(antenna: 1, rssiDbm: -90, readCount: 10).Should().BeFalse();

    [Fact]
    public void A_single_stray_read_is_not_enough()
        => Profile("1=Checkout", minimumReads: 2).Accepts(antenna: 1, rssiDbm: -40, readCount: 1).Should().BeFalse();

    /// <summary>
    /// An unmeasured signal is not an infinitely weak one. R2000-family readers leave the RSSI byte
    /// empty in real-time inventory mode; treating that as a number below the floor discards every
    /// read from such a reader.
    /// </summary>
    [Fact]
    public void An_unmeasured_signal_skips_the_proximity_test_but_not_the_others()
    {
        var profile = Profile("1=Checkout", minimumReads: 2);

        profile.Accepts(antenna: 1, rssiDbm: ReaderProfile.UnmeasuredRssi, readCount: 2).Should().BeTrue();

        profile.Accepts(antenna: 1, rssiDbm: ReaderProfile.UnmeasuredRssi, readCount: 1)
            .Should().BeFalse("the read-count floor still applies");

        profile.Accepts(antenna: 2, rssiDbm: ReaderProfile.UnmeasuredRssi, readCount: 5)
            .Should().BeFalse("zoning still applies");
    }

    [Fact]
    public void A_malformed_zone_entry_is_ignored_rather_than_throwing()
    {
        var profile = Profile("1=Checkout;garbage;=;3=NotAZone");

        profile.ZoneFor(1).Should().Be(AntennaZone.Checkout, "a broken entry must not take the good ones with it");
        profile.ZoneFor(3).Should().Be(AntennaZone.Unassigned, "an unrecognised zone name feeds nothing");
    }
}
