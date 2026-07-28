using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;

namespace Retail25.Application.Carts.Services;

/// <summary>
/// Assembles the configuration snapshot a cart needs and runs the pricing engine over it.
/// <para>
/// All catalog rows are fetched in one round trip per table, keyed by the products actually in the
/// cart, so pricing a fifty-line bulk RFID cart costs a fixed number of queries rather than fifty.
/// </para>
/// </summary>
public sealed class CartPricingService : ICartPricingService
{
    public static readonly Error CartNotFound = new("cart.not_found", "The cart no longer exists.");
    public static readonly Error LocationNotConfigured = new("location.not_configured", "The location this cart belongs to is not set up.");
    public static readonly Error TaxNotConfigured = new("tax.not_configured", "No tax configuration is in effect for this location on this date.");
    public static readonly Error CurrencyNotConfigured = new("currency.not_configured", "The location has no base currency configured.");

    private readonly IApplicationDbContext _db;
    private readonly ICartStore _cartStore;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public CartPricingService(
        IApplicationDbContext db,
        ICartStore cartStore,
        ICurrentUser currentUser,
        IDateTime clock)
    {
        _db = db;
        _cartStore = cartStore;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<SalePricingResult>> QuoteAsync(Guid cartId, CancellationToken ct = default)
    {
        var cart = await _cartStore.GetAsync(cartId, ct);
        if (cart is null)
        {
            return Result.Failure<SalePricingResult>(CartNotFound);
        }

        var contextResult = await BuildContextAsync(cart, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<SalePricingResult>(contextResult.Error);
        }

        var lines = await _cartStore.GetLinesAsync(cartId, ct);
        var requests = await BuildLineRequestsAsync(lines, ct);
        var adjustments = await BuildAdjustmentsAsync(cartId, ct);

        return Result.Success(SalePricingEngine.Calculate(requests, contextResult.Value, adjustments));
    }

    /// <summary>
    /// Gathers the effective configuration for the cart's location on its business date. Every
    /// value is read rather than assumed; a store that has not been set up fails loudly here rather
    /// than quietly pricing at zero tax.
    /// </summary>
    private async Task<Result<PricingContext>> BuildContextAsync(Cart cart, CancellationToken ct)
    {
        var location = await _db.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == cart.LocationId, ct);

        if (location is null)
        {
            return Result.Failure<PricingContext>(LocationNotConfigured);
        }

        // The business date is the store's, not the server's: a sale rung at 00:30 belongs to the
        // trading day the store says it does.
        var businessDate = location.BusinessDateFor(_clock.Now);

        var taxConfigurations = await _db.TaxConfigurations
            .AsNoTracking()
            .Where(t => t.LocationId == cart.LocationId && t.EffectiveFrom <= businessDate)
            .OrderByDescending(t => t.EffectiveFrom)
            .ToListAsync(ct);

        var tax = taxConfigurations.Find(t => t.IsCurrentOn(businessDate));
        if (tax is null)
        {
            return Result.Failure<PricingContext>(TaxNotConfigured.With("businessDate", businessDate));
        }

        var currency = await _db.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == location.BaseCurrencyCode, ct);

        if (currency is null)
        {
            return Result.Failure<PricingContext>(CurrencyNotConfigured.With("code", location.BaseCurrencyCode));
        }

        var policy = await _db.PosPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.LocationId == cart.LocationId, ct)
            ?? PosPolicy.CreateDefault(cart.LocationId);

        var loyalty = await _db.LoyaltyPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == cart.LocationId, ct)
            ?? LoyaltyPolicy.CreateDisabled(cart.LocationId);

        var customer = cart.CustomerId is null
            ? null
            : await _db.CustomerPricingProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CustomerId == cart.CustomerId, ct);

        // Only the most recent override is in force; each new one replaces the last from its own
        // sequence onward.
        var taxOverride = await _db.CartTaxOverrides
            .AsNoTracking()
            .Where(o => o.CartId == cart.Id)
            .OrderByDescending(o => o.AppliesFromSequence)
            .FirstOrDefaultAsync(ct);

        var permissions = new PricingPermissions(
            CanSelectPriceLevel: _currentUser.HasPermission(PricingPermissionKeys.SelectPriceLevel),
            CanDiscount: _currentUser.HasPermission(PricingPermissionKeys.Discount),
            CanOverrideTax: _currentUser.HasPermission(PricingPermissionKeys.OverrideTax),
            CanOverridePrice: _currentUser.HasPermission(PricingPermissionKeys.OverridePrice));

        return Result.Success(new PricingContext(
            businessDate,
            tax,
            policy,
            RoundingPolicy.FromCurrency(currency),
            customer,
            taxOverride,
            loyalty,
            permissions));
    }

    /// <summary>
    /// Loads the catalog rows for every product in the cart. Batched by product id so the query
    /// count does not grow with the size of the cart.
    /// </summary>
    private async Task<IReadOnlyList<PricingLineRequest>> BuildLineRequestsAsync(
        IReadOnlyList<CartLine> lines,
        CancellationToken ct)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var variantIds = lines.Where(l => l.VariantId.HasValue).Select(l => l.VariantId!.Value).Distinct().ToList();

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var variants = variantIds.Count == 0
            ? []
            : await _db.ProductVariants
                .AsNoTracking()
                .Where(v => variantIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

        var prices = await _db.ProductPrices.AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId)).ToListAsync(ct);

        var breaks = await _db.PriceBreaks.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId)).ToListAsync(ct);

        var bonuses = await _db.BonusPricings.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId)).ToListAsync(ct);

        var salePricings = await _db.SalePricings.AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId)).ToListAsync(ct);

        var requests = new List<PricingLineRequest>(lines.Count);

        foreach (var line in lines.OrderBy(l => l.Sequence))
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                // The product was deleted while the cart was open. Skipping it silently would
                // change the total without explanation, so the line is left out and the caller's
                // reconciliation surfaces it.
                continue;
            }

            var catalog = new ProductPricingData(
                prices.Where(p => p.ProductId == line.ProductId).ToList(),
                breaks.Where(b => b.ProductId == line.ProductId).ToList(),
                bonuses.Find(b => b.ProductId == line.ProductId),
                salePricings.Where(s => s.ProductId == line.ProductId).ToList());

            ProductVariant? variant = null;
            if (line.VariantId.HasValue)
            {
                variants.TryGetValue(line.VariantId.Value, out variant);
            }

            var input = new LineInput(
                Sequence: line.Sequence,
                Product: product,
                Variant: variant,
                Quantity: line.Quantity,
                ManualUnitPrice: line.ManualUnitPrice,
                EmbeddedUnitPrice: line.EmbeddedUnitPrice,
                ManualDiscountPct: line.ManualDiscountPct,
                RequestedPriceLevel: line.RequestedPriceLevel,
                Tax1Override: line.Tax1Override,
                Tax2Override: line.Tax2Override,
                Type: line.LineType,
                Source: MapSource(line.Source));

            requests.Add(new PricingLineRequest(input, catalog, line.UnitCostSnapshot));
        }

        return requests;
    }

    private async Task<SaleAdjustments> BuildAdjustmentsAsync(Guid cartId, CancellationToken ct)
    {
        var rows = await _db.CartAdjustments
            .AsNoTracking()
            .Where(a => a.CartId == cartId)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return SaleAdjustments.None;
        }

        var coupons = rows
            .Where(a => a.Type == AdjustmentType.Coupon)
            .Select(a => new CouponCredit(a.Label, a.Amount))
            .ToList();

        var bottles = rows
            .Where(a => a.Type == AdjustmentType.BottleReturn)
            .Select(a => new BottleReturnCredit(a.Label, a.Amount))
            .ToList();

        // Only one subtotal discount can be in force; the most recently entered one wins.
        var discountRow = rows.LastOrDefault(a => a.Type == AdjustmentType.SubtotalDiscount);
        var subtotalDiscount = discountRow is null
            ? null
            : new SubtotalDiscount(
                discountRow.Percent > 0m ? discountRow.Percent : null,
                discountRow.Amount > 0m ? discountRow.Amount : null);

        var redeemLoyalty = rows.Exists(a => a.Type == AdjustmentType.LoyaltyReward);

        return new SaleAdjustments(coupons, bottles, subtotalDiscount, redeemLoyalty, SuspendAddOnCharge: false);
    }

    private static PriceSource MapSource(LineSource source) => source switch
    {
        LineSource.Rfid => PriceSource.Rfid,
        LineSource.Barcode => PriceSource.Barcode,
        LineSource.StockCode => PriceSource.StockCode,
        _ => PriceSource.Manual,
    };
}

/// <summary>
/// Permission keys the pricing path checks. These are the same strings as the seeded permission
/// catalogue in <c>Retail25.Infrastructure.Identity.Permissions</c>; they are repeated here rather
/// than referenced because Application must not depend on Infrastructure, and an architecture test
/// asserts the two lists agree.
/// </summary>
public static class PricingPermissionKeys
{
    public const string SelectPriceLevel = "pos.select_price_level";
    public const string Discount = "pos.discount";
    public const string OverrideTax = "pos.tax_override";
    public const string OverridePrice = "pos.price_override";
}
