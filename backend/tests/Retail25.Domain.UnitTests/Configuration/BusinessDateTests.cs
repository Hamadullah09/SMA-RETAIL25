using FluentAssertions;
using Retail25.Domain.Configuration;
using Xunit;

namespace Retail25.Domain.UnitTests.Configuration;

/// <summary>
/// Which day a sale belongs to.
/// <para>
/// Pinned because getting it wrong is invisible until somebody reconciles a till: the sale is
/// recorded, the money is right, and only the date it is filed under is wrong. A shop in Karachi
/// ran with the hosting server's time zone — US Pacific — and its business day rolled over at noon
/// local, so every morning's takings appeared under the previous day. Nothing errored.
/// </para>
/// </summary>
public sealed class BusinessDateTests
{
    private static Location At(string timeZoneId, TimeOnly dayStart)
        => Location.Create("Main Store", "TST", "PKR", timeZoneId, dayStart).Value;

    /// <summary>The ordinary case: the shop's own zone, days starting at midnight.</summary>
    [Theory]
    [InlineData("2026-08-18T00:30:00+05:00", "2026-08-18")]
    [InlineData("2026-08-18T10:00:00+05:00", "2026-08-18")]
    [InlineData("2026-08-18T23:59:00+05:00", "2026-08-18")]
    public void A_sale_belongs_to_the_day_it_was_rung_in_the_shops_own_zone(string instant, string expected)
    {
        var date = At("Pakistan Standard Time", TimeOnly.MinValue)
            .BusinessDateFor(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));

        date.Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The bug, kept as a test so the shape of it is on record.
    /// <para>
    /// With the server's zone instead of the shop's, a 10am sale in Karachi is still the previous
    /// afternoon in California, so it is filed a day early. This is what the live system did.
    /// </para>
    /// </summary>
    [Fact]
    public void The_hosting_servers_zone_files_a_morning_sale_under_the_previous_day()
    {
        var morningInKarachi = DateTimeOffset.Parse(
            "2026-08-18T10:00:00+05:00",
            System.Globalization.CultureInfo.InvariantCulture);

        At("Pacific Standard Time", TimeOnly.MinValue)
            .BusinessDateFor(morningInKarachi)
            .Should()
            .Be(new DateOnly(2026, 8, 17), "midnight in California is noon in Karachi");

        At("Pakistan Standard Time", TimeOnly.MinValue)
            .BusinessDateFor(morningInKarachi)
            .Should()
            .Be(new DateOnly(2026, 8, 18));
    }

    /// <summary>
    /// A late-night shop sets the day to start at its closing time, so a sale at 1am is filed with
    /// the evening it belongs to rather than opening a new day mid-shift.
    /// </summary>
    [Fact]
    public void A_day_start_after_midnight_keeps_a_late_shift_together()
    {
        var location = At("Pakistan Standard Time", new TimeOnly(4, 0));

        location.BusinessDateFor(DateTimeOffset.Parse("2026-08-19T01:00:00+05:00", System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(new DateOnly(2026, 8, 18), "1am is still the 18th's trading");

        location.BusinessDateFor(DateTimeOffset.Parse("2026-08-19T05:00:00+05:00", System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(new DateOnly(2026, 8, 19));
    }
}
