using FluentAssertions;
using Retail25.Domain.Catalog;
using Xunit;

namespace Retail25.Domain.UnitTests.Catalog;

/// <summary>
/// Moving a tag onto a different item.
/// <para>
/// The operation is ordinary — tags get applied to the wrong thing at goods-in, and pre-encoded
/// label rolls get reused when a line is discontinued. What matters is where it is refused: a tag
/// that is already on somebody's cart, or already sold, is part of a sale that is happening or has
/// happened, and changing what it refers to rewrites that.
/// </para>
/// </summary>
public sealed class SerializedUnitReassignTests
{
    private static readonly Guid Location = Guid.NewGuid();

    private static SerializedUnit InStock()
    {
        var unit = SerializedUnit.Create(
            Guid.NewGuid(), Location, serialNumber: null, epc: "E28069150000600B40A75995", DateTimeOffset.UtcNow).Value;

        unit.Commission();
        return unit;
    }

    [Fact]
    public void A_tag_in_stock_moves_to_the_new_item()
    {
        var unit = InStock();
        var keyboard = Guid.NewGuid();

        unit.ReassignTo(keyboard).IsSuccess.Should().BeTrue();

        unit.ProductId.Should().Be(keyboard);
        unit.State.Should().Be(SerializedUnitState.InStock, "moving a tag does not change whether it is sellable");
    }

    /// <summary>
    /// The variant goes with it. A tag moved from a shirt to a mug that kept "large, blue" would
    /// deduct stock from a variant of an item it is no longer attached to.
    /// </summary>
    [Fact]
    public void Moving_a_tag_clears_a_variant_that_was_not_carried_over()
    {
        var unit = InStock();
        unit.AssignVariant(Guid.NewGuid());

        unit.ReassignTo(Guid.NewGuid());

        unit.VariantId.Should().BeNull();
    }

    /// <summary>
    /// The refusal that matters most. A tag on an open cart would change under the cashier mid-sale:
    /// the line was priced as one item and would be sold as another.
    /// </summary>
    [Fact]
    public void A_tag_on_a_cart_cannot_be_moved()
    {
        var unit = InStock();
        var original = unit.ProductId;
        unit.ClaimForCart();

        var moved = unit.ReassignTo(Guid.NewGuid());

        moved.IsFailure.Should().BeTrue();
        moved.Error.Code.Should().Be(SerializedUnit.CannotReassign.Code);
        unit.ProductId.Should().Be(original, "a refused move must not half-apply");
    }

    /// <summary>A sold tag is on a receipt and in a stock movement. Both would become untrue.</summary>
    [Fact]
    public void A_sold_tag_cannot_be_moved()
    {
        var unit = InStock();
        unit.ClaimForCart();
        unit.Sell();

        unit.ReassignTo(Guid.NewGuid()).IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// A provisioned tag has been created but never commissioned — it is not sellable, and the way
    /// to give it an item is to commission it rather than to move it.
    /// </summary>
    [Fact]
    public void A_tag_that_was_never_commissioned_cannot_be_moved()
    {
        var unit = SerializedUnit.Create(
            Guid.NewGuid(), Location, serialNumber: null, epc: "E28069150000600B40A78D95", DateTimeOffset.UtcNow).Value;

        unit.State.Should().Be(SerializedUnitState.Provisioned);
        unit.ReassignTo(Guid.NewGuid()).IsFailure.Should().BeTrue();
    }
}
