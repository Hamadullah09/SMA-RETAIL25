using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;
using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;
using Retail25.Domain.Inventory;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// Commits a sale: prices the cart one last time, freezes every number onto an immutable
/// transaction, moves stock, moves loyalty points and closes the cart.
/// </summary>
/// <param name="CartId">The cart being sold.</param>
/// <param name="StaffId">Who rang it up.</param>
/// <param name="Tenders">How it was paid for, one entry per tender in a split.</param>
/// <param name="PrintReceipt">Whether to print.</param>
/// <param name="CopyCount">Number of copies.</param>
public sealed record CompleteSaleCommand(
    Guid CartId,
    Guid StaffId,
    List<TenderInput> Tenders,
    bool PrintReceipt = true,
    int CopyCount = 1) : ICommand<CompleteSaleResult>;

/// <summary>One tender in a split payment.</summary>
/// <param name="TenderTypeId">Which configured tender was used.</param>
/// <param name="Amount">Amount applied to the balance.</param>
/// <param name="AmountTendered">Amount physically handed over, for cash change.</param>
/// <param name="AuthCode">Authorisation code from the gateway, for cards.</param>
/// <param name="CardLast4">Masked card number, for the receipt.</param>
/// <param name="GatewayReference">Gateway's own reference, for reconciliation.</param>
public sealed record TenderInput(
    Guid TenderTypeId,
    decimal Amount,
    decimal AmountTendered = 0m,
    string? AuthCode = null,
    string? CardLast4 = null,
    string? GatewayReference = null);

/// <summary>
/// Outcome of committing a sale.
/// </summary>
/// <param name="Success">Whether the sale was committed.</param>
/// <param name="TransactionId">Identity of the new transaction.</param>
/// <param name="TransactionNumber">Human-facing number printed on the receipt.</param>
/// <param name="Error">Stable error key when the sale was refused.</param>
/// <param name="ChangeDue">Cash to hand back, already rounded to the smallest coin.</param>
/// <param name="GrandTotal">What the sale came to.</param>
public sealed record CompleteSaleResult(
    bool Success,
    Guid? TransactionId,
    long? TransactionNumber,
    string? Error,
    decimal ChangeDue = 0m,
    decimal GrandTotal = 0m);

public class CompleteSaleHandler : IRequestHandler<CompleteSaleCommand, CompleteSaleResult>
{
    private readonly ICartStore _cartStore;
    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;
    private readonly ICartPricingService _pricing;
    private readonly IDateTime _clock;

    public CompleteSaleHandler(
        ICartStore cartStore,
        IApplicationDbContext db,
        IPosNotifier notifier,
        ICartPricingService pricing,
        IDateTime clock)
    {
        _cartStore = cartStore;
        _db = db;
        _notifier = notifier;
        _pricing = pricing;
        _clock = clock;
    }

    public async Task<CompleteSaleResult> Handle(CompleteSaleCommand request, CancellationToken ct)
    {
        var cart = await _cartStore.GetAsync(request.CartId, ct);
        if (cart is null || cart.Status != CartStatus.Active)
        {
            return new CompleteSaleResult(false, null, null, "cart.not_active");
        }

        var cartLines = await _cartStore.GetLinesAsync(request.CartId, ct);
        if (cartLines.Count == 0)
        {
            return new CompleteSaleResult(false, null, null, "cart.empty");
        }

        // The authoritative total is computed here, at commit time, from configuration — never
        // taken from the client, and never from a stale quote the browser has been holding.
        var quoted = await _pricing.QuoteAsync(request.CartId, ct);
        if (quoted.IsFailure)
        {
            return new CompleteSaleResult(false, null, null, quoted.Error.Code);
        }

        var quote = quoted.Value;

        var currency = await ResolveCurrencyAsync(cart.LocationId, ct);
        if (currency is null)
        {
            return new CompleteSaleResult(false, null, null, "currency.not_configured");
        }

        var rounding = RoundingPolicy.FromCurrency(currency);

        var tenderResult = await ValidateTendersAsync(request.Tenders, quote.GrandTotal, rounding, ct);
        if (tenderResult.Error is not null)
        {
            return new CompleteSaleResult(false, null, null, tenderResult.Error);
        }

        var now = _clock.Now;
        var costOfGoods = CalculateCostOfGoods(quote, cartLines);

        var transaction = new SalesTransaction
        {
            TransactionNumber = await NextTransactionNumberAsync(cart.LocationId, ct),
            LocationId = cart.LocationId,
            StationId = cart.StationId,
            StaffId = request.StaffId,
            CustomerId = cart.CustomerId,
            Subtotal = quote.Subtotal,
            DiscountTotal = quote.AdjustmentTotal,
            AddOnChargeTotal = quote.AddOnCharge,
            Tax1Total = quote.Tax1Total,
            Tax2Total = quote.Tax2Total,
            GrandTotal = quote.GrandTotal,
            CostOfGoodsSold = costOfGoods,
            LoyaltyPointsEarned = quote.LoyaltyPointsEarned,
            LoyaltyPointsRedeemed = quote.LoyaltyPointsRedeemed,
            Status = TransactionStatus.Completed,
            CompletedAt = now,
            CreatedAt = now,
        };

        _db.SalesTransactions.Add(transaction);

        WriteSaleLines(transaction, quote, cartLines);
        WriteTenders(transaction, request.Tenders);
        WriteStockMovements(transaction, quote, cartLines, request.StaffId, now);
        await WriteLoyaltyMovementsAsync(transaction, cart, quote, now, ct);

        await _db.SaveChangesAsync(ct);

        cart.Status = CartStatus.Completed;
        cart.Revision++;
        await _cartStore.SetAsync(cart, ct);

        await NotifyStockChangesAsync(cart.LocationId, quote, ct);

        return new CompleteSaleResult(
            true,
            transaction.Id,
            transaction.TransactionNumber,
            null,
            tenderResult.ChangeDue,
            quote.GrandTotal);
    }

    /// <summary>
    /// Checks the tenders cover the sale. Cash is allowed to exceed the total — the surplus comes
    /// back as change, rounded to the smallest coin in circulation (guide p.84). Anything else must
    /// settle exactly, because there is nothing to hand back on a card.
    /// </summary>
    private async Task<(string? Error, decimal ChangeDue)> ValidateTendersAsync(
        IReadOnlyList<TenderInput> tenders,
        decimal grandTotal,
        RoundingPolicy rounding,
        CancellationToken ct)
    {
        if (tenders.Count == 0)
        {
            return ("tender.required", 0m);
        }

        var tenderTypeIds = tenders.Select(t => t.TenderTypeId).Distinct().ToList();

        var tenderTypes = await _db.TenderTypes
            .AsNoTracking()
            .Where(t => tenderTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        if (tenderTypes.Count != tenderTypeIds.Count)
        {
            return ("tender.unknown_type", 0m);
        }

        var applied = rounding.Round(tenders.Sum(t => t.Amount));
        var overTenderCapacity = 0m;

        foreach (var tender in tenders)
        {
            var type = tenderTypes[tender.TenderTypeId];

            if (type.RequiresReference && string.IsNullOrWhiteSpace(tender.AuthCode) && string.IsNullOrWhiteSpace(tender.GatewayReference))
            {
                return ("tender.reference_required", 0m);
            }

            if (type.AllowsOverTender)
            {
                var handedOver = tender.AmountTendered > 0m ? tender.AmountTendered : tender.Amount;
                overTenderCapacity += Math.Max(0m, handedOver - tender.Amount);
            }
        }

        var shortfall = rounding.Round(grandTotal - applied);

        // A cash sale rounds to the nearest coin, so the applied total is allowed to differ from
        // the exact total by less than one coin without being called short.
        var tolerance = rounding.MinimumTender > 0m ? rounding.MinimumTender / 2m : 0m;

        if (shortfall > tolerance)
        {
            return ("tender.insufficient", 0m);
        }

        var change = rounding.RoundToMinimumTender(overTenderCapacity + Math.Max(0m, -shortfall));
        return (null, change);
    }

    /// <summary>
    /// Freezes the quote onto immutable sale lines. Nothing here is recomputed later: a reprint in
    /// a year's time reads these columns, so a subsequent price or tax-rate change cannot alter a
    /// document that has already been issued (guide p.56).
    /// </summary>
    private void WriteSaleLines(SalesTransaction transaction, SalePricingResult quote, IReadOnlyList<CartLine> cartLines)
    {
        var cartBySequence = cartLines.ToDictionary(l => l.Sequence);

        foreach (var priced in quote.Lines)
        {
            cartBySequence.TryGetValue(priced.Sequence, out var cartLine);

            _db.SaleLines.Add(new SaleLine
            {
                TransactionId = transaction.Id,
                ProductId = priced.ProductId,
                VariantId = priced.VariantId,
                StockCodeSnapshot = cartLine?.StockCodeSnapshot,
                NameSnapshot = cartLine?.NameSnapshot,
                Quantity = priced.ChargeableQuantity,
                UnitPrice = priced.UnitPrice,
                DiscountPct = priced.LineDiscountPct,
                ExtendedNet = priced.NetAmount,
                Tax1Amount = priced.Tax1Amount,
                Tax2Amount = priced.Tax2Amount,
                UnitCostSnapshot = cartLine?.UnitCostSnapshot ?? 0m,
                PriceOrigin = priced.PriceOrigin,
                LineType = priced.LineType,
            });
        }
    }

    private void WriteTenders(SalesTransaction transaction, IReadOnlyList<TenderInput> tenders)
    {
        foreach (var tender in tenders)
        {
            _db.SaleTenders.Add(new SaleTender
            {
                TransactionId = transaction.Id,
                TenderTypeId = tender.TenderTypeId,
                Amount = tender.Amount,
                AmountTendered = tender.AmountTendered,
                AuthCode = tender.AuthCode,
                CardLast4 = tender.CardLast4,
                GatewayReference = tender.GatewayReference,
            });
        }
    }

    /// <summary>
    /// Writes one ledger movement per line. Bonus giveaways move stock too — the customer walks out
    /// with them even though nothing was charged — so the stock quantity, not the charged quantity,
    /// is what leaves.
    /// </summary>
    private void WriteStockMovements(
        SalesTransaction transaction,
        SalePricingResult quote,
        IReadOnlyList<CartLine> cartLines,
        Guid staffId,
        DateTimeOffset now)
    {
        var cartBySequence = cartLines.ToDictionary(l => l.Sequence);

        foreach (var priced in quote.Lines)
        {
            cartBySequence.TryGetValue(priced.Sequence, out var cartLine);

            // A return only restocks if the goods came back in saleable condition, which is the
            // cashier's call at the till (guide p.7).
            if (priced.LineType != LineType.Sale && cartLine is { ReturnToStock: false })
            {
                continue;
            }

            var movement = priced.LineType switch
            {
                LineType.Return => MovementType.ReturnIn,
                LineType.TradeIn => MovementType.TradeInIn,
                _ => MovementType.Sale,
            };

            var signedQuantity = priced.LineType == LineType.Sale
                ? -priced.StockQuantity
                : priced.StockQuantity;

            _db.StockLedgerEntries.Add(StockLedgerEntry.Create(
                productId: priced.ProductId,
                locationId: transaction.LocationId,
                movementType: movement,
                quantity: signedQuantity,
                unitCost: cartLine?.UnitCostSnapshot ?? 0m,
                occurredAt: now,
                variantId: priced.VariantId,
                referenceType: nameof(SalesTransaction),
                referenceId: transaction.Id,
                staffId: staffId));
        }
    }

    /// <summary>
    /// Records points earned and spent. The ledger is the record; the balance on the customer is a
    /// derived snapshot, so a disputed balance can always be rebuilt by replaying the entries.
    /// </summary>
    private async Task WriteLoyaltyMovementsAsync(
        SalesTransaction transaction,
        Cart cart,
        SalePricingResult quote,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (cart.CustomerId is not { } customerId)
        {
            return;
        }

        if (quote.LoyaltyPointsEarned == 0 && quote.LoyaltyPointsRedeemed == 0)
        {
            return;
        }

        // Earning and spending are recorded separately rather than netted, so a statement can show
        // the customer what they gained and what they used on the same visit.
        if (quote.LoyaltyPointsEarned > 0)
        {
            _db.LoyaltyLedgerEntries.Add(
                LoyaltyLedgerEntry.Earn(customerId, transaction.Id, quote.LoyaltyPointsEarned, now));
        }

        if (quote.LoyaltyPointsRedeemed > 0)
        {
            var redemption = LoyaltyLedgerEntry.Redeem(customerId, quote.LoyaltyPointsRedeemed, now);
            redemption.TransactionId = transaction.Id;
            _db.LoyaltyLedgerEntries.Add(redemption);
        }

        var profile = await _db.CustomerPricingProfiles
            .FirstOrDefaultAsync(p => p.CustomerId == customerId, ct);

        if (profile is not null)
        {
            profile.RewardPoints += quote.LoyaltyPointsEarned - quote.LoyaltyPointsRedeemed;
        }
    }

    /// <summary>
    /// Cost of goods at the moment of sale, taken from the frozen unit costs so later cost changes
    /// cannot rewrite margin history (guide p.14).
    /// </summary>
    private static decimal CalculateCostOfGoods(SalePricingResult quote, IReadOnlyList<CartLine> cartLines)
    {
        var cartBySequence = cartLines.ToDictionary(l => l.Sequence);
        var total = 0m;

        foreach (var priced in quote.Lines.Where(l => l.LineType == LineType.Sale))
        {
            if (cartBySequence.TryGetValue(priced.Sequence, out var cartLine))
            {
                total += cartLine.UnitCostSnapshot * priced.StockQuantity;
            }
        }

        return decimal.Round(total, 4, MidpointRounding.AwayFromZero);
    }

    private async Task<Currency?> ResolveCurrencyAsync(Guid locationId, CancellationToken ct)
    {
        var code = await _db.Locations
            .AsNoTracking()
            .Where(l => l.Id == locationId)
            .Select(l => l.BaseCurrencyCode)
            .FirstOrDefaultAsync(ct);

        return code is null
            ? null
            : await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, ct);
    }

    /// <summary>
    /// Allocates the next receipt number for the location. Numbers are per location so two stores
    /// do not collide, and they come from the database rather than a clock so they are gapless and
    /// safe under concurrency.
    /// </summary>
    private async Task<long> NextTransactionNumberAsync(Guid locationId, CancellationToken ct)
    {
        var highest = await _db.SalesTransactions
            .AsNoTracking()
            .Where(t => t.LocationId == locationId)
            .MaxAsync(t => (long?)t.TransactionNumber, ct);

        return (highest ?? 0L) + 1L;
    }

    private async Task NotifyStockChangesAsync(Guid locationId, SalePricingResult quote, CancellationToken ct)
    {
        foreach (var productId in quote.Lines.Select(l => l.ProductId).Distinct())
        {
            var onHand = await _db.Products
                .AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => p.OnHand)
                .FirstOrDefaultAsync(ct);

            await _notifier.StockLevelChangedAsync(locationId, productId, onHand, ct);
        }
    }
}
