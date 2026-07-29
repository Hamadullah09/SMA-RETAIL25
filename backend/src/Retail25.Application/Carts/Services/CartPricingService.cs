using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Carts.Dtos;
using Retail25.Domain.Catalog;
using Retail25.Domain.Customers;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;

namespace Retail25.Application.Carts.Services;

/// <summary>A priced cart plus everything the client needs to render it.</summary>
public sealed record CartQuote(CartSnapshot Snapshot, PosContext Context, SalePricingResult Pricing, CartDto Dto);

/// <summary>
/// The seam between storage and the pure pricing engine: it gathers products, price levels, breaks,
/// promotions and the customer profile for the lines actually on the cart, runs the engine, writes
/// the answer back onto the lines and shapes the DTO.
/// <para>
/// The cart is re-priced on every quote rather than incrementally patched. That is deliberate:
/// attaching a customer mid-sale changes the price of items already rung, and any design that only
/// prices new lines gets that wrong (guide p.52).
/// </para>
/// </summary>
public sealed class CartPricingService
{
    private readonly IApplicationDbContext _db;

    public CartPricingService(IApplicationDbContext db) => _db = db;

    public async Task<CartQuote> QuoteAsync(CartSnapshot snapshot, PosContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);

        var lines = snapshot.OrderedLines;
        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();

        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var prices = await _db.ProductPrices.AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId))
            .ToListAsync(ct);

        var breaks = await _db.PriceBreaks.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId))
            .ToListAsync(ct);

        var bonuses = await _db.BonusPricings.AsNoTracking()
            .Where(b => productIds.Contains(b.ProductId))
            .ToListAsync(ct);

        var sales = await _db.SalePricings.AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .ToListAsync(ct);

        var variantIds = lines.Where(l => l.VariantId.HasValue).Select(l => l.VariantId!.Value).Distinct().ToList();
        var variants = variantIds.Count == 0
            ? []
            : await _db.ProductVariants.AsNoTracking()
                .Where(v => variantIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

        var (customerProfile, customerDto) = await LoadCustomerAsync(snapshot.Cart.CustomerId, ct);

        var pricingContext = new PricingContext(
            context.BusinessDate,
            context.Tax,
            context.Policy,
            customerProfile,
            snapshot.TaxOverride,
            context.Loyalty,
            context.Rules,
            context.Rounding);

        var inputs = new List<LineInput>(lines.Count);
        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                continue;
            }

            variants.TryGetValue(line.VariantId ?? Guid.Empty, out var variant);

            inputs.Add(new LineInput(
                line.Id,
                line.Sequence,
                product,
                variant,
                line.Quantity,
                line.ManualUnitPrice,
                line.ManualDiscountPct,
                line.RequestedPriceLevel,
                line.Tax1Override,
                line.Tax2Override,
                line.LineType,
                line.Source,
                line.EmbeddedPrice,
                prices.Where(p => p.ProductId == product.Id).ToList(),
                breaks.Where(b => b.ProductId == product.Id).ToList(),
                bonuses.FirstOrDefault(b => b.ProductId == product.Id),
                sales.FirstOrDefault(s => s.ProductId == product.Id && s.IsActive(context.BusinessDate)),
                product.AvgCost));
        }

        var adjustments = snapshot.Adjustments
            .Where(a => a.Type != AdjustmentType.GiftCertificate)
            .Select(a => new AdjustmentInput(a.Type, a.Label, a.Amount, a.Percent))
            .ToList();

        var result = SalePricingEngine.Calculate(inputs, adjustments, pricingContext);

        // Write the engine's answer back onto the lines so the UI has something to show between quotes.
        var byLine = result.Lines.ToDictionary(l => l.LineId);
        foreach (var line in snapshot.Lines)
        {
            if (!byLine.TryGetValue(line.Id, out var resolved))
            {
                continue;
            }

            line.ApplyQuote(
                resolved.UnitPrice,
                resolved.PriceOrigin,
                resolved.DiscountPct,
                resolved.Tax1Applies,
                resolved.Tax2Applies,
                resolved.ExtendedNet,
                resolved.Tax1Amount,
                resolved.Tax2Amount);

            if (!products.TryGetValue(line.ProductId, out var product))
            {
                continue;
            }

            line.StockCodeSnapshot = product.StockCode;
            line.UnitCostSnapshot = product.AvgCost;

            // An unknown item carries the description the cashier typed; the placeholder product is
            // only there to satisfy the foreign key, and its name must not overwrite theirs.
            if (line.Source != LineSource.Unknown)
            {
                line.NameSnapshot = product.Name;
            }
        }

        var dto = BuildDto(snapshot, result, customerDto, variants);
        return new CartQuote(snapshot, context, result, dto);
    }

    private async Task<(CustomerPricingProfile? Profile, CartCustomerDto? Dto)> LoadCustomerAsync(Guid? customerId, CancellationToken ct)
    {
        if (customerId is not { } id)
        {
            return (null, null);
        }

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
        {
            return (null, null);
        }

        var profile = await _db.CustomerPricingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.CustomerId == id, ct)
            ?? CustomerPricingProfile.Create(id);

        var account = await _db.CustomerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.CustomerId == id, ct);

        return (profile, new CartCustomerDto(
            customer.Id,
            customer.CustomerNumber,
            customer.FullName,
            profile.PriceLevel,
            profile.UsualDiscountPct,
            profile.ExemptTax1,
            profile.ExemptTax2,
            profile.RewardPoints,
            account?.BalanceDue ?? 0m,
            account?.CreditLimit ?? 0m));
    }

    private static CartDto BuildDto(
        CartSnapshot snapshot,
        SalePricingResult pricing,
        CartCustomerDto? customer,
        IReadOnlyDictionary<Guid, ProductVariant> variants)
    {
        var resolvedByLine = pricing.Lines.ToDictionary(l => l.LineId);

        var lineDtos = snapshot.OrderedLines.Select(line =>
        {
            resolvedByLine.TryGetValue(line.Id, out var resolved);
            var variant = line.VariantId is { } vid && variants.TryGetValue(vid, out var v) ? v : null;

            return new CartLineDto(
                line.Id,
                line.Sequence,
                line.ProductId,
                line.VariantId,
                line.StockCodeSnapshot ?? string.Empty,
                line.NameSnapshot ?? string.Empty,
                variant is null ? null : FormatVariant(variant),
                line.Epc,
                null,
                line.Source,
                line.LineType,
                line.Quantity,
                resolved?.ChargeableQuantity ?? line.Quantity,
                line.UnitPrice,
                line.PriceOrigin,
                line.LineDiscountPct,
                line.ExtendedNet,
                line.Tax1Applies,
                line.Tax2Applies,
                line.Tax1Amount,
                line.Tax2Amount,
                line.RequestedPriceLevel,
                line.ManualUnitPrice.HasValue,
                line.Note);
        }).ToList();

        var totals = new CartTotalsDto(
            pricing.Subtotal,
            pricing.AdjustmentTotal,
            pricing.Tax1Name,
            pricing.Tax1Total,
            pricing.Tax2Name,
            pricing.Tax2Total,
            pricing.AddOnChargeName,
            pricing.AddOnCharge,
            pricing.GrandTotal,
            pricing.TaxInclusive,
            pricing.LoyaltyPointsEarned,
            pricing.LoyaltyPointsRedeemed,
            lineDtos.Count);

        var adjustmentDtos = snapshot.Adjustments
            .Select(a => new CartAdjustmentDto(a.Id, a.Type, a.Label, a.Amount, a.Serial))
            .ToList();

        return new CartDto(
            snapshot.Cart.Id,
            snapshot.Cart.StationId,
            snapshot.Cart.LocationId,
            snapshot.Cart.StaffId,
            snapshot.Cart.Status,
            snapshot.Cart.Revision,
            snapshot.Cart.HeldName,
            customer,
            lineDtos,
            adjustmentDtos,
            totals,
            snapshot.TaxOverride?.Tax1,
            snapshot.TaxOverride?.Tax2);
    }

    private static string FormatVariant(ProductVariant variant)
        => string.Join(" / ", new[] { variant.Dim1Value, variant.Dim2Value, variant.Dim3Value }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
}
