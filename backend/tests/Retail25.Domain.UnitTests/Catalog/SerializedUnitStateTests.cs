using FluentAssertions;
using Retail25.Domain.Catalog;
using Xunit;

namespace Retail25.Domain.UnitTests.Catalog;

/// <summary>
/// The state machine that decides whether a tagged item can be sold.
/// <para>
/// These exist because it silently did not. <c>Sell()</c> insisted on <c>InCart</c>, nothing ever
/// called <c>ClaimForCart()</c>, so every unit was still <c>InStock</c> when the sale completed;
/// the refusal came back as a <c>Result</c> that the caller discarded, the stock level went down,
/// and the tag stayed on the shelf ready to be rung again. On the live database that had left nine
/// units sitting on completed sale lines still marked <c>InStock</c> and two products on an on-hand
/// of −1.
/// </para>
/// </summary>
public sealed class SerializedUnitStateTests
{
    [Fact]
    public void A_unit_on_the_shelf_can_be_sold()
    {
        var unit = InStock();

        unit.Sell().IsSuccess.Should().BeTrue();
        unit.State.Should().Be(SerializedUnitState.Sold);
    }

    [Fact]
    public void A_unit_claimed_by_a_cart_can_be_sold()
    {
        var unit = InStock();
        unit.ClaimForCart().IsSuccess.Should().BeTrue();

        unit.Sell().IsSuccess.Should().BeTrue();
        unit.State.Should().Be(SerializedUnitState.Sold);
    }

    /// <summary>
    /// The one that matters. One physical thing cannot be sold to two people, and the refusal has
    /// to be a refusal rather than a value somebody can drop.
    /// </summary>
    [Fact]
    public void A_unit_already_sold_cannot_be_sold_again()
    {
        var unit = InStock();
        unit.Sell();

        var second = unit.Sell();

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be(SerializedUnit.InvalidStateTransition.Code);
        unit.State.Should().Be(SerializedUnitState.Sold, "a refused sale must not change anything");
    }

    [Fact]
    public void A_unit_still_being_provisioned_cannot_be_sold()
    {
        var unit = Provisioned("E28011700080020A7A6B6AE1");

        unit.Sell().IsFailure.Should().BeTrue();
        unit.State.Should().Be(SerializedUnitState.Provisioned);
    }

    [Fact]
    public void A_returned_unit_goes_back_on_the_shelf_and_can_be_sold_again()
    {
        var unit = InStock();
        unit.Sell();

        unit.Return().IsSuccess.Should().BeTrue();
        unit.State.Should().Be(SerializedUnitState.Returned);
    }

    [Fact]
    public void A_unit_that_was_never_sold_cannot_be_returned()
    {
        var unit = InStock();

        unit.Return().IsFailure.Should().BeTrue();
        unit.State.Should().Be(SerializedUnitState.InStock);
    }

    /// <summary>
    /// A second till cannot claim a unit the first one is holding. The durable guard against two
    /// tills selling one tag is this plus the row version, which every entity here carries as a
    /// concurrency token.
    /// </summary>
    [Fact]
    public void A_second_cart_cannot_claim_a_unit_already_claimed()
    {
        var unit = InStock();
        unit.ClaimForCart().IsSuccess.Should().BeTrue();

        unit.ClaimForCart().IsFailure.Should().BeTrue();
    }

    private static SerializedUnit Provisioned(string epc)
        => SerializedUnit.Create(productId: 1, locationId: 1, serialNumber: null, epc: epc, DateTimeOffset.UnixEpoch).Value;

    private static SerializedUnit InStock()
    {
        var unit = Provisioned("E28011700080020A7A6B6AE2");
        unit.Commission();
        return unit;
    }
}
