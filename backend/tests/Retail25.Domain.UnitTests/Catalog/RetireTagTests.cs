using FluentAssertions;
using Retail25.Domain.Catalog;
using Xunit;

namespace Retail25.Domain.UnitTests.Catalog;

/// <summary>
/// Withdrawing a tag when a shop is re-tagged.
/// <para>
/// The physical stock is unchanged; the label on it is being replaced. So the old EPC has to stop
/// being sellable without the record of what it did being lost — which is why this is a state and
/// not a delete.
/// </para>
/// </summary>
public sealed class RetireTagTests
{
    private static SerializedUnit InStock()
    {
        var unit = SerializedUnit.Create(1, 1, null, "E28011700000020A7A6B6AE1", DateTimeOffset.UtcNow).Value;
        unit.Commission();
        return unit;
    }

    [Fact]
    public void A_tag_in_stock_is_withdrawn()
    {
        var unit = InStock();

        unit.Retire().IsSuccess.Should().BeTrue();
        unit.State.Should().Be(SerializedUnitState.Void);
    }

    /// <summary>
    /// A sold unit is what a receipt points at. Rewriting its state would rewrite history rather
    /// than end it, so retiring is refused and the caller counts it as left alone.
    /// </summary>
    [Fact]
    public void A_sold_tag_is_left_exactly_as_it_is()
    {
        var unit = InStock();
        unit.Sell();

        unit.Retire().IsFailure.Should().BeTrue();
        unit.State.Should().Be(SerializedUnitState.Sold);
    }

    /// <summary>Idempotent: re-running a retirement must not report the same tag twice.</summary>
    [Fact]
    public void Retiring_twice_is_refused_the_second_time()
    {
        var unit = InStock();

        unit.Retire().IsSuccess.Should().BeTrue();
        unit.Retire().IsFailure.Should().BeTrue();
    }
}
