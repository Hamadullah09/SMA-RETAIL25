using FluentAssertions;
using Retail25.Domain.Trolleys;
using Xunit;

namespace Retail25.Domain.UnitTests.Trolleys;

/// <summary>
/// What a trolley weighs before anything is in it.
/// <para>
/// Kept per trolley because the fleet is not uniform — this shop's run about 2.2 to 2.5 kg — and
/// anything that later checks a basket against a scale has to subtract the right one. A single
/// fleet-wide figure would start every trolley up to 150 g out, which is more than many items weigh.
/// </para>
/// </summary>
public sealed class TrolleyTareTests
{
    [Fact]
    public void A_trolley_records_the_weight_it_was_created_with()
        => Trolley.Create(1, 1, "301", "Self checkout 1", 2.35m)
            .Value.TareWeightKg.Should().Be(2.35m);

    /// <summary>
    /// Unknown and "weighs nothing" are different claims. A trolley nobody has weighed must not
    /// report a tare of zero to whatever does the arithmetic later.
    /// </summary>
    [Fact]
    public void A_trolley_nobody_has_weighed_has_no_tare_rather_than_zero()
        => Trolley.Create(1, 1, "302").Value.TareWeightKg.Should().BeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    public void A_weight_of_zero_or_less_is_refused(decimal tare)
    {
        var result = Trolley.Create(1, 1, "303", null, tare);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("trolley.tare_invalid");
    }

    /// <summary>Weighing one properly overwrites the assumption it was created with.</summary>
    [Fact]
    public void Weighing_a_trolley_replaces_the_assumed_figure()
    {
        var trolley = Trolley.Create(1, 1, "304", null, 2.35m).Value;

        trolley.SetTareWeight(2.482m).IsSuccess.Should().BeTrue();
        trolley.TareWeightKg.Should().Be(2.482m, "trolleys are weighed to the gram or not at all");
    }

    /// <summary>
    /// Clearing it back to unknown is a real thing to want: a trolley whose wheels have been
    /// replaced no longer weighs what the sticker said, and unknown beats stale.
    /// </summary>
    [Fact]
    public void A_tare_can_be_cleared_back_to_unknown()
    {
        var trolley = Trolley.Create(1, 1, "305", null, 2.35m).Value;

        trolley.SetTareWeight(null).IsSuccess.Should().BeTrue();
        trolley.TareWeightKg.Should().BeNull();
    }
}
