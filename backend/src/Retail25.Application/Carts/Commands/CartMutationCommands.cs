using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// The outcome of any change to a cart.
/// <para>
/// Every mutation returns the whole re-priced cart rather than just the line it touched. Pricing is
/// contextual — attaching a customer, crossing a volume break, suspending a tax — so a change to one
/// line routinely changes others. Returning a partial answer would leave the till to guess.
/// </para>
/// </summary>
/// <param name="Success">Whether the change was applied.</param>
/// <param name="Error">Stable error key when it was not.</param>
/// <param name="Quote">The cart as it now stands.</param>
public sealed record CartMutationResult(bool Success, string? Error, SalePricingResult? Quote)
{
    public static CartMutationResult Failed(string error) => new(false, error, null);
}

// ------------------------------------------------------------------------------------------------
// Line editing — the legacy item-detail window (guide p.6)
// ------------------------------------------------------------------------------------------------

/// <summary>
/// Changes a line already on the sale: quantity, a typed price or discount, a chosen price level,
/// or the tax keys. This is the item-detail window, which the legacy till pops up for every item
/// unless Fast Scan Mode is on.
/// </summary>
/// <param name="CartId">Cart being edited.</param>
/// <param name="LineId">Line being edited.</param>
/// <param name="Quantity">New quantity, if changing it.</param>
/// <param name="ManualPrice">A typed unit price. Pass null to leave it alone.</param>
/// <param name="ManualDiscountPct">A typed discount percentage.</param>
/// <param name="PriceLevel">Price level chosen with F5.</param>
/// <param name="Tax1Override">Tax 1 forced on or off with F6.</param>
/// <param name="Tax2Override">Tax 2 forced on or off with F7.</param>
/// <param name="ReturnToStock">For a return line, whether the goods go back on the shelf.</param>
public sealed record UpdateCartLineCommand(
    Guid CartId,
    Guid LineId,
    decimal? Quantity = null,
    decimal? ManualPrice = null,
    decimal? ManualDiscountPct = null,
    int? PriceLevel = null,
    bool? Tax1Override = null,
    bool? Tax2Override = null,
    bool? ReturnToStock = null) : IRequest<CartMutationResult>;

/// <summary>Removes a line from the sale (guide p.10).</summary>
/// <param name="CartId">Cart being edited.</param>
/// <param name="LineId">Line to remove.</param>
public sealed record RemoveCartLineCommand(Guid CartId, Guid LineId) : IRequest<CartMutationResult>;

// ------------------------------------------------------------------------------------------------
// Customer, credits, taxes
// ------------------------------------------------------------------------------------------------

/// <summary>
/// Attaches or clears the customer on a sale (guide p.9).
/// <para>
/// Not merely a label: the customer carries a usual discount, an assigned price level and tax
/// exemptions, so attaching one re-prices the whole cart.
/// </para>
/// </summary>
/// <param name="CartId">Cart being edited.</param>
/// <param name="CustomerId">Customer to attach, or null to clear (F9 Clear).</param>
public sealed record AttachCustomerToCartCommand(Guid CartId, Guid? CustomerId) : IRequest<CartMutationResult>;

/// <summary>
/// Applies a sale-wide credit: a subtotal discount, a coupon, a bottle deposit or a loyalty reward
/// (guide p.7, the F3 Credits menu).
/// </summary>
/// <param name="CartId">Cart being edited.</param>
/// <param name="Type">Which kind of credit.</param>
/// <param name="Label">What to print on the receipt.</param>
/// <param name="Amount">A fixed amount, for coupons and bottle returns.</param>
/// <param name="Percent">A percentage, for a subtotal discount.</param>
/// <param name="Serial">Serial number, for a gift certificate.</param>
public sealed record ApplyCartAdjustmentCommand(
    Guid CartId,
    AdjustmentType Type,
    string Label,
    decimal Amount = 0m,
    decimal Percent = 0m,
    string? Serial = null) : IRequest<CartMutationResult>;

/// <summary>Removes a credit that was applied in error.</summary>
/// <param name="CartId">Cart being edited.</param>
/// <param name="AdjustmentId">Credit to remove.</param>
public sealed record RemoveCartAdjustmentCommand(Guid CartId, Guid AdjustmentId) : IRequest<CartMutationResult>;

/// <summary>
/// Suspends or applies a tax for the rest of this sale (guide p.11, F11 → F6 Taxes).
/// <para>
/// Deliberately not retroactive. The guide is explicit that the change affects "only the items that
/// are not already on the POS screen", so the override records the sequence it was raised at and
/// lines already rung up keep the tax they were rung up with.
/// </para>
/// </summary>
/// <param name="CartId">Cart being edited.</param>
/// <param name="Tax1">Tax 1 forced on or off; null leaves it to the usual rules.</param>
/// <param name="Tax2">Tax 2 forced on or off.</param>
public sealed record OverrideCartTaxCommand(Guid CartId, bool? Tax1, bool? Tax2) : IRequest<CartMutationResult>;

// ------------------------------------------------------------------------------------------------
// Suspend and recall (guide p.11)
// ------------------------------------------------------------------------------------------------

/// <summary>
/// Puts a sale aside so the till is free for the next customer, without losing the work.
/// </summary>
/// <param name="CartId">Cart to suspend.</param>
/// <param name="HeldName">A label the cashier will recognise, e.g. the customer's name.</param>
public sealed record SuspendCartCommand(Guid CartId, string? HeldName) : IRequest<CartMutationResult>;

/// <summary>Brings a suspended sale back to a till.</summary>
/// <param name="CartId">Cart to resume.</param>
/// <param name="StationId">Which till is taking it — it need not be the one that suspended it.</param>
public sealed record ResumeCartCommand(Guid CartId, Guid StationId) : IRequest<CartMutationResult>;

// ------------------------------------------------------------------------------------------------
// Handlers
// ------------------------------------------------------------------------------------------------

/// <summary>
/// Handles every cart mutation.
/// <para>
/// They share a class because they share a shape: load the cart, check it is still open, change one
/// thing, re-price, save. Splitting them across nine files would repeat that skeleton nine times
/// without making any of them clearer.
/// </para>
/// </summary>
public class CartMutationHandlers :
    IRequestHandler<UpdateCartLineCommand, CartMutationResult>,
    IRequestHandler<RemoveCartLineCommand, CartMutationResult>,
    IRequestHandler<AttachCustomerToCartCommand, CartMutationResult>,
    IRequestHandler<ApplyCartAdjustmentCommand, CartMutationResult>,
    IRequestHandler<RemoveCartAdjustmentCommand, CartMutationResult>,
    IRequestHandler<OverrideCartTaxCommand, CartMutationResult>,
    IRequestHandler<SuspendCartCommand, CartMutationResult>,
    IRequestHandler<ResumeCartCommand, CartMutationResult>
{
    private readonly ICartStore _cartStore;
    private readonly IApplicationDbContext _db;
    private readonly ICartPricingService _pricing;
    private readonly IPosNotifier _notifier;

    public CartMutationHandlers(
        ICartStore cartStore,
        IApplicationDbContext db,
        ICartPricingService pricing,
        IPosNotifier notifier)
    {
        _cartStore = cartStore;
        _db = db;
        _pricing = pricing;
        _notifier = notifier;
    }

    public async Task<CartMutationResult> Handle(UpdateCartLineCommand request, CancellationToken ct)
    {
        var cart = await LoadActiveCartAsync(request.CartId, ct);
        if (cart is null)
        {
            return CartMutationResult.Failed("cart.not_active");
        }

        var lines = await _cartStore.GetLinesAsync(request.CartId, ct);
        var line = lines.FirstOrDefault(l => l.Id == request.LineId);

        if (line is null)
        {
            return CartMutationResult.Failed("cart_line.not_found");
        }

        // Only the fields the caller sent are touched. A till that omits a field means "leave it",
        // not "clear it" — otherwise changing a quantity would silently drop a tax override.
        if (request.Quantity is { } quantity)
        {
            if (quantity <= 0m)
            {
                return CartMutationResult.Failed("cart_line.quantity_invalid");
            }

            line.Quantity = quantity;
        }

        if (request.ManualPrice is not null)
        {
            line.ManualUnitPrice = request.ManualPrice;
        }

        if (request.ManualDiscountPct is not null)
        {
            line.ManualDiscountPct = request.ManualDiscountPct;
        }

        if (request.PriceLevel is not null)
        {
            line.RequestedPriceLevel = request.PriceLevel;
        }

        if (request.Tax1Override is not null)
        {
            line.Tax1Override = request.Tax1Override;
        }

        if (request.Tax2Override is not null)
        {
            line.Tax2Override = request.Tax2Override;
        }

        if (request.ReturnToStock is { } returnToStock)
        {
            line.ReturnToStock = returnToStock;
        }

        await _cartStore.SetLinesAsync(request.CartId, lines, ct);
        return await RepriceAsync(cart, ct);
    }

    public async Task<CartMutationResult> Handle(RemoveCartLineCommand request, CancellationToken ct)
    {
        var cart = await LoadActiveCartAsync(request.CartId, ct);
        if (cart is null)
        {
            return CartMutationResult.Failed("cart.not_active");
        }

        var lines = await _cartStore.GetLinesAsync(request.CartId, ct);
        var remaining = lines.Where(l => l.Id != request.LineId).ToList();

        if (remaining.Count == lines.Count)
        {
            return CartMutationResult.Failed("cart_line.not_found");
        }

        // Sequences are deliberately not renumbered. They anchor the non-retroactive tax override,
        // so closing a gap would silently move a line across an override boundary.
        await _cartStore.SetLinesAsync(request.CartId, remaining, ct);
        return await RepriceAsync(cart, ct);
    }

    public async Task<CartMutationResult> Handle(AttachCustomerToCartCommand request, CancellationToken ct)
    {
        var cart = await LoadActiveCartAsync(request.CartId, ct);
        if (cart is null)
        {
            return CartMutationResult.Failed("cart.not_active");
        }

        if (request.CustomerId is { } customerId)
        {
            var exists = await _db.Customers.AsNoTracking().AnyAsync(c => c.Id == customerId, ct);
            if (!exists)
            {
                return CartMutationResult.Failed("customer.not_found");
            }
        }

        cart.CustomerId = request.CustomerId;
        return await RepriceAsync(cart, ct);
    }

    public async Task<CartMutationResult> Handle(ApplyCartAdjustmentCommand request, CancellationToken ct)
    {
        var cart = await LoadActiveCartAsync(request.CartId, ct);
        if (cart is null)
        {
            return CartMutationResult.Failed("cart.not_active");
        }

        if (request.Amount <= 0m && request.Percent <= 0m)
        {
            return CartMutationResult.Failed("adjustment.value_required");
        }

        // Only one subtotal discount can be in force. Replacing rather than stacking matches the
        // legacy till, where re-entering a discount corrects it instead of compounding it.
        if (request.Type == AdjustmentType.SubtotalDiscount)
        {
            var existing = await _db.CartAdjustments
                .Where(a => a.CartId == request.CartId && a.Type == AdjustmentType.SubtotalDiscount)
                .ToListAsync(ct);

            _db.CartAdjustments.RemoveRange(existing);
        }

        _db.CartAdjustments.Add(CartAdjustment.Create(
            request.CartId,
            request.Type,
            request.Label,
            request.Amount,
            request.Percent,
            request.Serial));

        await _db.SaveChangesAsync(ct);

        return await RepriceAsync(cart, ct);
    }

    public async Task<CartMutationResult> Handle(RemoveCartAdjustmentCommand request, CancellationToken ct)
    {
        var cart = await LoadActiveCartAsync(request.CartId, ct);
        if (cart is null)
        {
            return CartMutationResult.Failed("cart.not_active");
        }

        var adjustment = await _db.CartAdjustments
            .FirstOrDefaultAsync(a => a.Id == request.AdjustmentId && a.CartId == request.CartId, ct);

        if (adjustment is null)
        {
            return CartMutationResult.Failed("adjustment.not_found");
        }

        _db.CartAdjustments.Remove(adjustment);
        await _db.SaveChangesAsync(ct);

        return await RepriceAsync(cart, ct);
    }

    public async Task<CartMutationResult> Handle(OverrideCartTaxCommand request, CancellationToken ct)
    {
        var cart = await LoadActiveCartAsync(request.CartId, ct);
        if (cart is null)
        {
            return CartMutationResult.Failed("cart.not_active");
        }

        // The sequence is the whole point: the override reaches what is rung up next, not what is
        // already on the screen (guide p.11).
        _db.CartTaxOverrides.Add(CartTaxOverride.Create(
            request.CartId,
            cart.NextLineSequence,
            request.Tax1,
            request.Tax2));

        await _db.SaveChangesAsync(ct);

        return await RepriceAsync(cart, ct);
    }

    public async Task<CartMutationResult> Handle(SuspendCartCommand request, CancellationToken ct)
    {
        var cart = await LoadActiveCartAsync(request.CartId, ct);
        if (cart is null)
        {
            return CartMutationResult.Failed("cart.not_active");
        }

        cart.Status = CartStatus.Suspended;
        cart.HeldName = request.HeldName;
        cart.Revision++;

        // A suspended sale must not be swept away by the abandoned-cart timer: it is waiting for a
        // customer who went to fetch something, and losing it is losing the customer's basket.
        cart.ExpiresAt = null;

        await _cartStore.SetAsync(cart, ct);
        await NotifyAsync(cart, null, ct);

        return new CartMutationResult(true, null, null);
    }

    public async Task<CartMutationResult> Handle(ResumeCartCommand request, CancellationToken ct)
    {
        var cart = await _cartStore.GetAsync(request.CartId, ct);

        if (cart is null || cart.Status != CartStatus.Suspended)
        {
            return CartMutationResult.Failed("cart.not_suspended");
        }

        // A sale can be picked up at a different till from the one that put it aside.
        cart.StationId = request.StationId;
        cart.Status = CartStatus.Active;
        cart.HeldName = null;

        return await RepriceAsync(cart, ct);
    }

    private async Task<Cart?> LoadActiveCartAsync(Guid cartId, CancellationToken ct)
    {
        var cart = await _cartStore.GetAsync(cartId, ct);
        return cart?.Status == CartStatus.Active ? cart : null;
    }

    /// <summary>
    /// Re-prices the cart, writes the engine's decisions back onto the lines, saves and broadcasts.
    /// Every mutation ends here, which is what keeps the till, a second station and a reconnecting
    /// browser showing the same numbers.
    /// </summary>
    private async Task<CartMutationResult> RepriceAsync(Cart cart, CancellationToken ct)
    {
        cart.Revision++;
        await _cartStore.SetAsync(cart, ct);

        var quoted = await _pricing.QuoteAsync(cart.Id, ct);
        if (quoted.IsFailure)
        {
            return CartMutationResult.Failed(quoted.Error.Code);
        }

        var quote = quoted.Value;
        var lines = await _cartStore.GetLinesAsync(cart.Id, ct);
        var bySequence = quote.Lines.ToDictionary(l => l.Sequence);

        foreach (var line in lines)
        {
            if (!bySequence.TryGetValue(line.Sequence, out var priced))
            {
                continue;
            }

            line.UnitPrice = priced.UnitPrice;
            line.PriceOrigin = priced.PriceOrigin;
            line.LineDiscountPct = priced.LineDiscountPct;
            line.Tax1Applies = priced.Tax1Applies;
            line.Tax2Applies = priced.Tax2Applies;
            line.ChargeableQuantity = priced.ChargeableQuantity;
            line.FreeQuantity = priced.FreeQuantity;
            line.NetAmount = priced.NetAmount;
            line.Tax1Amount = priced.Tax1Amount;
            line.Tax2Amount = priced.Tax2Amount;
        }

        await _cartStore.SetLinesAsync(cart.Id, lines, ct);
        await NotifyAsync(cart, quote, ct);

        return new CartMutationResult(true, null, quote);
    }

    private async Task NotifyAsync(Cart cart, SalePricingResult? quote, CancellationToken ct)
    {
        await _notifier.CartUpdatedAsync(cart.LocationId, cart.Id, cart, cart.Revision, ct);

        if (quote is not null)
        {
            await _notifier.TotalsChangedAsync(cart.LocationId, cart.Id, quote, ct);
        }
    }
}
