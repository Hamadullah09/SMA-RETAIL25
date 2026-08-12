using FluentAssertions;
using Retail25.Domain.Inventory;
using Xunit;

namespace Retail25.Domain.UnitTests.Inventory;

/// <summary>
/// When a product actually needs reordering.
/// <para>
/// Three screens used to answer this differently — the stock-position report ignored stock already
/// on order, the inventory browse counted it, and only the catalogue browse skipped products with
/// no reorder point set. Three screens, three answers, one question.
/// </para>
/// <para>
/// The <c>&lt;=</c> boundary is <em>not</em> among the faults, and an earlier attempt at this fix
/// tried to make it one. Quietening the dashboard by moving the report to a strict <c>&lt;</c>
/// would have left it disagreeing with <c>PurchaseOrderCommands</c>, which orders on
/// <c>onHandPlusOnOrder &lt;= ReorderPoint</c> — a buyer would read that an item was fine while the
/// system was ordering it. An existing test said so in its name and its comment, and was right.
/// </para>
/// </summary>
public sealed class ReorderPolicyTests
{
    /// <summary>
    /// On the point is where you order — it still needs buying, it is simply not yet a shortage.
    /// The distinction is the whole point of the standing; the threshold is the purchase-order
    /// generator's and must not move.
    /// </summary>
    [Fact]
    public void Sitting_exactly_on_the_reorder_point_needs_buying_but_is_not_a_shortage()
    {
        ReorderPolicy.Assess(onHand: 1, onOrder: 0, committed: 0, reorderPoint: 1)
            .Should().Be(ReorderStanding.AtPoint);

        ReorderPolicy.NeedsReordering(onHand: 1, onOrder: 0, committed: 0, reorderPoint: 1)
            .Should().BeTrue("the purchase-order generator orders at this boundary too");
    }

    [Fact]
    public void Below_the_point_needs_reordering()
    {
        ReorderPolicy.Assess(onHand: 2, onOrder: 0, committed: 0, reorderPoint: 5)
            .Should().Be(ReorderStanding.Below);

        ReorderPolicy.NeedsReordering(onHand: 2, onOrder: 0, committed: 0, reorderPoint: 5)
            .Should().BeTrue();
    }

    [Fact]
    public void Above_the_point_is_left_alone()
    {
        ReorderPolicy.Assess(onHand: 40, onOrder: 0, committed: 0, reorderPoint: 5)
            .Should().Be(ReorderStanding.Above);
    }

    /// <summary>
    /// Stock already on its way covers the shortfall. Reordering against on-hand alone is how a
    /// buyer ends up ordering the same thing twice.
    /// </summary>
    [Fact]
    public void Stock_already_on_order_counts_towards_cover()
    {
        ReorderPolicy.NeedsReordering(onHand: 2, onOrder: 10, committed: 0, reorderPoint: 5)
            .Should().BeFalse();

        ReorderPolicy.Assess(onHand: 2, onOrder: 10, committed: 0, reorderPoint: 5)
            .Should().Be(ReorderStanding.Above);
    }

    /// <summary>Stock promised to somebody else is not stock you can sell.</summary>
    [Fact]
    public void Committed_stock_does_not_count_towards_cover()
    {
        ReorderPolicy.NeedsReordering(onHand: 6, onOrder: 0, committed: 4, reorderPoint: 5)
            .Should().BeTrue("only two are actually available against a point of five");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_product_with_no_reorder_point_never_alerts(int reorderPoint)
    {
        ReorderPolicy.Assess(onHand: 0, onOrder: 0, committed: 0, reorderPoint)
            .Should().Be(ReorderStanding.NotTracked);

        ReorderPolicy.NeedsReordering(onHand: 0, onOrder: 0, committed: 0, reorderPoint)
            .Should().BeFalse();
    }

    [Fact]
    public void Nothing_on_the_shelf_against_a_real_point_is_a_shortage()
    {
        ReorderPolicy.NeedsReordering(onHand: 0, onOrder: 0, committed: 0, reorderPoint: 3)
            .Should().BeTrue();
    }

    /// <summary>
    /// The seeded catalogue, which is the shape that exposed this: two hundred products holding one
    /// and reordering at one. They do all need buying — the generator would order every one — so the
    /// count is not the bug. What the dashboard could not say before is that none of them has
    /// actually run short, and that is what separates a list worth reading from a wall of red.
    /// </summary>
    [Fact]
    public void The_seeded_catalogue_is_at_its_point_rather_than_short()
    {
        var shape = Enumerable.Range(0, 200)
            .Select(_ => ReorderPolicy.Assess(onHand: 1, onOrder: 0, committed: 0, reorderPoint: 1))
            .ToList();

        shape.Should().OnlyContain(s => s == ReorderStanding.AtPoint);
        shape.Should().NotContain(ReorderStanding.Below);
    }

    /// <summary>
    /// The expression the database queries use must agree with the method the reports use. They are
    /// two spellings of one rule and drifting apart is the fault this type was created to end.
    /// </summary>
    [Theory]
    [InlineData(1, 0, 1, true)]
    [InlineData(2, 0, 5, true)]
    [InlineData(2, 10, 5, false)]
    [InlineData(0, 0, 0, false)]
    [InlineData(0, 0, 3, true)]
    [InlineData(40, 0, 5, false)]
    public void The_query_expression_agrees_with_the_method(decimal onHand, decimal onOrder, int point, bool expected)
    {
        var predicate = ReorderPolicy
            .NeedsReorderingWhere<Row>(r => r.OnHand, r => r.OnOrder, _ => 0m, r => r.ReorderPoint)
            .Compile();

        predicate(new Row(onHand, onOrder, point)).Should().Be(expected);
        ReorderPolicy.NeedsReordering(onHand, onOrder, 0m, point).Should().Be(expected);
    }

    private sealed record Row(decimal OnHand, decimal OnOrder, int ReorderPoint);
}
