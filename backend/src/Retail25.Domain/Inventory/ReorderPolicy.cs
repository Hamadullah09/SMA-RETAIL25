using System.Linq.Expressions;

namespace Retail25.Domain.Inventory;

/// <summary>Where a product sits against the point at which it should be reordered.</summary>
public enum ReorderStanding
{
    /// <summary>No reorder point is set, so there is nothing to be under. Never alerts.</summary>
    NotTracked = 0,

    /// <summary>Comfortably above the point. Nothing to do.</summary>
    Above = 1,

    /// <summary>Exactly on the point. Worth watching; not yet a shortage.</summary>
    AtPoint = 2,

    /// <summary>Under the point, counting stock already on order. This is the one that alerts.</summary>
    Below = 3,
}

/// <summary>
/// The single definition of "needs reordering".
/// <para>
/// There were three, and they disagreed. The stock-position report asked
/// <c>OnHand &lt;= ReorderPoint</c> and ignored anything already on order; the inventory browse asked
/// <c>OnHand + OnOrder &lt;= ReorderPoint</c>; the catalogue browse asked the same but also skipped
/// products with no reorder point set. Three screens, three answers, one question.
/// </para>
/// <para>
/// The visible symptom was the dashboard reporting 200 of 201 items needing attention, because
/// every product in the seeded catalogue holds one and reorders at one. An alert that always fires
/// is an alert nobody reads — but the threshold was not the fault. Those items genuinely are at
/// their reorder point, and the buyer's own generator would order every one of them.
/// </para>
/// <para>
/// The boundary is <c>&lt;=</c>, and deliberately. A reorder point is the level at which you order,
/// not the level you have already fallen through, and <c>PurchaseOrderCommands</c> generates orders
/// on exactly that test — <c>onHandPlusOnOrder &lt;= ReorderPoint</c>. A report that drew the line
/// anywhere else would tell a buyer an item was fine while the generator was ordering it. An
/// earlier revision of this file moved the report to a strict <c>&lt;</c> to quieten the alert and
/// would have done precisely that.
/// </para>
/// <para>
/// So the noise is answered by saying <em>which</em> rather than by changing the threshold:
/// <see cref="ReorderStanding.AtPoint"/> is "order now", <see cref="ReorderStanding.Below"/> is
/// "you are already short". Both need buying; only one is urgent, and a screen that separates them
/// is readable where a single flag over the whole catalogue is not.
/// </para>
/// </summary>
public static class ReorderPolicy
{
    /// <summary>
    /// What is genuinely available to sell, once stock promised to other orders is set aside and
    /// stock already on its way is counted. Reordering against on-hand alone orders things twice.
    /// </summary>
    public static decimal Cover(decimal onHand, decimal onOrder, decimal committed)
        => onHand - committed + onOrder;

    public static ReorderStanding Assess(decimal onHand, decimal onOrder, decimal committed, int reorderPoint)
    {
        if (reorderPoint <= 0)
        {
            return ReorderStanding.NotTracked;
        }

        var cover = Cover(onHand, onOrder, committed);

        if (cover < reorderPoint)
        {
            return ReorderStanding.Below;
        }

        return cover == reorderPoint ? ReorderStanding.AtPoint : ReorderStanding.Above;
    }

    /// <summary>
    /// Whether this needs buying. Both <see cref="ReorderStanding.AtPoint"/> and
    /// <see cref="ReorderStanding.Below"/> do — the same boundary the purchase-order generator
    /// orders on, so the report and the orders it justifies cannot disagree.
    /// </summary>
    public static bool NeedsReordering(decimal onHand, decimal onOrder, decimal committed, int reorderPoint)
        => Assess(onHand, onOrder, committed, reorderPoint) is ReorderStanding.AtPoint or ReorderStanding.Below;

    /// <summary>
    /// The same rule as an expression, because the callers that matter are database queries and a
    /// method call cannot be translated to SQL. Kept beside <see cref="Assess"/> so the two cannot
    /// drift — which is the failure this whole type exists to end.
    /// </summary>
    public static Expression<Func<TProduct, bool>> NeedsReorderingWhere<TProduct>(
        Expression<Func<TProduct, decimal>> onHand,
        Expression<Func<TProduct, decimal>> onOrder,
        Expression<Func<TProduct, decimal>> committed,
        Expression<Func<TProduct, int>> reorderPoint)
    {
        var parameter = Expression.Parameter(typeof(TProduct), "p");

        Expression Bind<TValue>(Expression<Func<TProduct, TValue>> selector)
            => new ParameterRebinder(selector.Parameters[0], parameter).Visit(selector.Body)!;

        var point = Bind(reorderPoint);
        var cover = Expression.Add(
            Expression.Subtract(Bind(onHand), Bind(committed)),
            Bind(onOrder));

        var body = Expression.AndAlso(
            Expression.GreaterThan(point, Expression.Constant(0)),
            Expression.LessThanOrEqual(cover, Expression.Convert(point, typeof(decimal))));

        return Expression.Lambda<Func<TProduct, bool>>(body, parameter);
    }

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
