using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Inventory;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales.Pricing;

namespace Retail25.Application.Sales.Commands;

/// <summary>One line the customer is giving back, and how much of it.</summary>
public sealed record RefundLineRequest(long SaleLineId, decimal Quantity);

/// <summary>How the money goes back.</summary>
public sealed record RefundTenderRequest(long TenderTypeId, decimal Amount, string? Reference = null);

public sealed record RefundSaleResult(
    long RefundTransactionId,
    long RefundTransactionNumber,
    decimal RefundedTotal);

/// <summary>
/// Gives part of a sale back, as its own transaction (guide p.14).
/// <para>
/// The original is never edited — the same rule the void path follows, and for the same reason: a
/// receipt printed a year ago must still add up, and a ledger replayed from the start must land on
/// today's numbers. A refund is therefore a second transaction that points at the first, carrying
/// negative lines for exactly what came back.
/// </para>
/// <para>
/// This is the difference between a refund and ringing a negative line, which is all the till could
/// do before: a negative line knows nothing about what was sold, so nothing stops a customer being
/// given back three of the two shirts they bought, or the same tagged jacket being returned twice.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Return)]
public sealed record RefundSaleCommand(
    long TransactionId,
    IReadOnlyList<RefundLineRequest> Lines,
    IReadOnlyList<RefundTenderRequest> Tenders,
    string IdempotencyKey,
    string? Reason = null) : IRequest<Result<RefundSaleResult>>, IIdempotentCommand;

public sealed class RefundSaleHandler : IRequestHandler<RefundSaleCommand, Result<RefundSaleResult>>
{
    public static readonly Error NotFound = new("sale.not_found", "No such sale.");
    public static readonly Error AlreadyVoided = new("sale.voided", "That sale was voided; there is nothing to refund.");
    public static readonly Error NotASale = new("refund.not_a_sale", "Only a completed sale can be refunded.");
    public static readonly Error NothingSelected = new("refund.nothing_selected", "Choose what is being returned.");
    public static readonly Error LineNotOnSale = new("refund.line_not_on_sale", "That line is not part of this sale.");
    public static readonly Error QuantityNotPositive = new("refund.quantity_not_positive", "A returned quantity must be more than zero.");
    public static readonly Error MoreThanWasSold = new("refund.exceeds_sold", "That is more than was sold, or more than is left to return.");
    public static readonly Error WholeUnitsOnly = new("refund.whole_unit_required", "A tagged item is returned as the one unit it is.");
    public static readonly Error UnitNotSold = new("refund.unit_not_returnable", "That tagged unit is not out on this sale — it may already have come back.");
    public static readonly Error TenderMismatch = new("refund.tender_mismatch", "The refund tenders do not add up to the amount being returned.");
    public static readonly Error TenderTypeUnknown = new("tender.type_unknown", "That tender type is not configured.");

    private readonly IApplicationDbContext _db;
    private readonly ISequenceGenerator _sequences;
    private readonly IPosNotifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public RefundSaleHandler(
        IApplicationDbContext db,
        ISequenceGenerator sequences,
        IPosNotifier notifier,
        ICurrentUser currentUser,
        IDateTime clock)
    {
        _db = db;
        _sequences = sequences;
        _notifier = notifier;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<RefundSaleResult>> Handle(RefundSaleCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Lines.Count == 0)
        {
            return Result.Failure<RefundSaleResult>(NothingSelected);
        }

        var original = await _db.SalesTransactions.FirstOrDefaultAsync(t => t.Id == request.TransactionId, ct);
        if (original is null)
        {
            return Result.Failure<RefundSaleResult>(NotFound.With("transactionId", request.TransactionId));
        }

        if (original.IsVoided)
        {
            return Result.Failure<RefundSaleResult>(AlreadyVoided.With("transactionId", original.Id));
        }

        if (original.Status != TransactionStatus.Completed)
        {
            return Result.Failure<RefundSaleResult>(NotASale.With("status", original.Status.ToString()));
        }

        var soldLines = await _db.SaleLines.AsNoTracking()
            .Where(l => l.TransactionId == original.Id)
            .ToListAsync(ct);

        var alreadyBack = await AlreadyRefundedAsync(soldLines.Select(l => l.Id).ToList(), ct);

        var currency = await CurrencyForAsync(original.LocationId, ct);
        var rounding = MoneyRounding.FromCurrency(currency);

        var planned = new List<PlannedRefund>(request.Lines.Count);

        foreach (var wanted in request.Lines)
        {
            var line = soldLines.Find(l => l.Id == wanted.SaleLineId);
            if (line is null)
            {
                return Result.Failure<RefundSaleResult>(LineNotOnSale.With("saleLineId", wanted.SaleLineId));
            }

            if (wanted.Quantity <= 0m)
            {
                return Result.Failure<RefundSaleResult>(QuantityNotPositive.With("saleLineId", wanted.SaleLineId));
            }

            var remaining = line.Quantity - alreadyBack.GetValueOrDefault(line.Id);
            if (wanted.Quantity > remaining)
            {
                return Result.Failure<RefundSaleResult>(MoreThanWasSold
                    .With("saleLineId", line.Id)
                    .With("requested", wanted.Quantity)
                    .With("remaining", remaining));
            }

            // A tagged unit is one physical thing. Half of it cannot come back.
            if (line.SerializedUnitId is { } unitId)
            {
                if (wanted.Quantity != line.Quantity)
                {
                    return Result.Failure<RefundSaleResult>(WholeUnitsOnly.With("saleLineId", line.Id));
                }

                var unit = await _db.SerializedUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct);
                if (unit is null || unit.State != SerializedUnitState.Sold)
                {
                    return Result.Failure<RefundSaleResult>(UnitNotSold
                        .With("epc", line.Epc)
                        .With("state", unit?.State.ToString()));
                }
            }

            planned.Add(Plan(line, wanted.Quantity, rounding));
        }

        var refundTotal = planned.Sum(p => p.Gross);

        var tenderCheck = await CheckTendersAsync(request, refundTotal, rounding, ct);
        if (tenderCheck.IsFailure)
        {
            return Result.Failure<RefundSaleResult>(tenderCheck.Error);
        }

        var now = _clock.Now;
        var stationId = _currentUser.StationId ?? original.StationId;

        var refund = new SalesTransaction
        {
            TransactionNumber = await _sequences.NextTransactionNumberAsync(original.LocationId, ct),
            LocationId = original.LocationId,
            StationId = stationId,
            StaffId = _currentUser.StaffId ?? original.StaffId,
            CustomerId = original.CustomerId,
            DrawerSessionId = await CurrentDrawerSessionIdAsync(stationId, ct),
            BusinessDate = DateOnly.FromDateTime(now.UtcDateTime),
            Subtotal = -planned.Sum(p => p.Net),
            Tax1Total = -planned.Sum(p => p.Tax1),
            Tax2Total = -planned.Sum(p => p.Tax2),
            GrandTotal = -refundTotal,
            CostOfGoodsSold = -planned.Sum(p => p.Cost),
            Status = TransactionStatus.Reversal,
            ReversesTransactionId = original.Id,
            VoidReason = request.Reason,
            CompletedAt = now,
            CreatedAt = now,
            CreatedBy = _currentUser.UserId,
        };

        _db.SalesTransactions.Add(refund);

        // Saved before its id is read: every line, tender and ledger entry below references it, and
        // a refund whose rows point at transaction 0 cannot be found from the sale it belongs to.
        // Still inside the pipeline's transaction, so a later failure rolls the whole thing back.
        await _db.SaveChangesAsync(ct);

        await CopyTaxSnapshotAsync(original, refund, ct);

        foreach (var item in planned)
        {
            await WriteRefundLineAsync(original, refund, item, now, ct);
        }

        WriteTenders(refund, request);
        await ApplyDrawerAsync(refund, request, planned, now, ct);

        await _db.SaveChangesAsync(ct);

        await _notifier.CartUpdatedAsync(
            original.LocationId,
            refund.Id,
            new { refund.Id, refund.TransactionNumber, RefundOf = original.TransactionNumber },
            0,
            ct);

        return Result.Success(new RefundSaleResult(refund.Id, refund.TransactionNumber, refundTotal));
    }

    /// <summary>
    /// How much of each line has already gone back, across every earlier refund.
    /// <para>
    /// Counted from the refund lines themselves rather than from a running total on the sale, so a
    /// refund that rolled back leaves nothing behind to over-count.
    /// </para>
    /// </summary>
    private async Task<Dictionary<long, decimal>> AlreadyRefundedAsync(List<long> saleLineIds, CancellationToken ct)
        => await _db.SaleLines.AsNoTracking()
            .Where(l => l.RefundsSaleLineId != null && saleLineIds.Contains(l.RefundsSaleLineId!.Value))
            .GroupBy(l => l.RefundsSaleLineId!.Value)
            .Select(g => new { SaleLineId = g.Key, Quantity = g.Sum(l => -l.Quantity) })
            .ToDictionaryAsync(x => x.SaleLineId, x => x.Quantity, ct);

    /// <summary>
    /// Works out what one returned line is worth, pro-rata when only part of it comes back.
    /// <para>
    /// Rounded once per line per tax, never on a running total — the same rule the pricing engine
    /// follows, and the reason a half-returned basket still reconciles to the penny.
    /// </para>
    /// </summary>
    private static PlannedRefund Plan(SaleLine line, decimal quantity, MoneyRounding rounding)
    {
        var share = line.Quantity == 0m ? 0m : quantity / line.Quantity;

        var net = rounding.Round(line.ExtendedNet * share);
        var tax1 = rounding.Round(line.Tax1Amount * share);
        var tax2 = rounding.Round(line.Tax2Amount * share);
        var cost = rounding.Round(line.UnitCostSnapshot * quantity);

        return new PlannedRefund(line, quantity, net, tax1, tax2, cost);
    }

    private async Task<Result> CheckTendersAsync(
        RefundSaleCommand request,
        decimal refundTotal,
        MoneyRounding rounding,
        CancellationToken ct)
    {
        foreach (var tender in request.Tenders)
        {
            if (!await _db.TenderTypes.AsNoTracking().AnyAsync(t => t.Id == tender.TenderTypeId, ct))
            {
                return Result.Failure(TenderTypeUnknown.With("tenderTypeId", tender.TenderTypeId));
            }
        }

        // Amounts are stated as positives — the customer is handed this much — and the ledger rows
        // below carry the sign. Anything else lets a "refund" of -100 take money instead.
        if (request.Tenders.Any(t => t.Amount <= 0m))
        {
            return Result.Failure(QuantityNotPositive.With("reason", "a refund tender must be positive"));
        }

        var offered = rounding.Round(request.Tenders.Sum(t => t.Amount));
        if (offered != rounding.Round(refundTotal))
        {
            return Result.Failure(TenderMismatch
                .With("due", refundTotal)
                .With("offered", offered));
        }

        return Result.Success();
    }

    private async Task WriteRefundLineAsync(
        SalesTransaction original,
        SalesTransaction refund,
        PlannedRefund item,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var line = item.Line;

        _db.SaleLines.Add(new SaleLine
        {
            TransactionId = refund.Id,
            RefundsSaleLineId = line.Id,
            Sequence = line.Sequence,
            ProductId = line.ProductId,
            VariantId = line.VariantId,
            SerializedUnitId = line.SerializedUnitId,
            Epc = line.Epc,
            SerialNumber = line.SerialNumber,
            StockCodeSnapshot = line.StockCodeSnapshot,
            NameSnapshot = line.NameSnapshot,
            Source = line.Source,
            Quantity = -item.Quantity,
            ChargeableQuantity = -item.Quantity,
            UnitPrice = line.UnitPrice,
            DiscountPct = line.DiscountPct,
            ExtendedNet = -item.Net,
            TaxableNet = -item.Net,
            Tax1Applies = line.Tax1Applies,
            Tax2Applies = line.Tax2Applies,
            Tax1Amount = -item.Tax1,
            Tax2Amount = -item.Tax2,
            UnitCostSnapshot = line.UnitCostSnapshot,
            PriceOrigin = line.PriceOrigin,
            LineType = LineType.Return,
            ReturnedToStock = true,
            Note = line.Note,
        });

        _db.StockLedgerEntries.Add(new StockLedgerEntry
        {
            ProductId = line.ProductId,
            VariantId = line.VariantId,
            LocationId = original.LocationId,
            MovementType = MovementType.ReturnIn,
            Quantity = item.Quantity,
            UnitCost = line.UnitCostSnapshot,
            ReferenceType = nameof(SalesTransaction),
            ReferenceId = refund.Id,
            Reason = "Refund",
            OccurredAt = now,
            StaffId = refund.StaffId,
        });

        var level = await _db.StockLevels.FirstOrDefaultAsync(
            s => s.ProductId == line.ProductId && s.VariantId == line.VariantId && s.LocationId == original.LocationId,
            ct);

        if (level is null)
        {
            level = StockLevel.Create(line.ProductId, line.VariantId, original.LocationId);
            _db.StockLevels.Add(level);
        }

        level.OnHand += item.Quantity;

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId, ct);
        product?.UpdateStockLevels(product.OnHand + item.Quantity, product.OnOrder);

        // The exact physical unit goes back on the shelf. Checked as Sold above, and the domain
        // guards the transition again here — this is the thing that stops one jacket being refunded
        // twice, which a negative line could never see.
        if (line.SerializedUnitId is { } unitId)
        {
            var unit = await _db.SerializedUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct);
            unit?.Return();
        }
    }

    private void WriteTenders(SalesTransaction refund, RefundSaleCommand request)
    {
        foreach (var tender in request.Tenders)
        {
            _db.SaleTenders.Add(new SaleTender
            {
                TransactionId = refund.Id,
                TenderTypeId = tender.TenderTypeId,
                Amount = -tender.Amount,
                AmountTendered = -tender.Amount,
                ChangeGiven = 0m,
                Reference = tender.Reference,
            });
        }
    }

    /// <summary>
    /// Takes the money out of the drawer it came from, and out of the session's takings.
    /// <para>
    /// Two separate things, exactly as a sale does them: a ledger entry for the cash that physically
    /// left, and the session totals so the end-of-day count reconciles. A refund settled on a card
    /// moves no cash but still reduces the day's net sales, which is why the totals are updated
    /// whether or not there was a cash leg.
    /// </para>
    /// </summary>
    private async Task ApplyDrawerAsync(
        SalesTransaction refund,
        RefundSaleCommand request,
        IReadOnlyList<PlannedRefund> planned,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (refund.DrawerSessionId is not { } sessionId)
        {
            return;
        }

        var session = await _db.DrawerSessions.FirstOrDefaultAsync(d => d.Id == sessionId, ct);
        if (session is null)
        {
            return;
        }

        var cashTypeIds = await _db.TenderTypes.AsNoTracking()
            .Where(t => t.CountsTowardsDrawerCash)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var cashBack = request.Tenders.Where(t => cashTypeIds.Contains(t.TenderTypeId)).Sum(t => t.Amount);

        if (cashBack != 0m)
        {
            _db.DrawerLedgerEntries.Add(DrawerLedgerEntry.Create(
                session.Id,
                DrawerEntryType.Refund,
                -cashBack,
                refund.StaffId,
                now,
                transactionId: refund.Id));
        }

        session.RecordSale(
            -planned.Sum(p => p.Net),
            -planned.Sum(p => p.Tax1),
            -planned.Sum(p => p.Tax2),
            -planned.Sum(p => p.Cost));
    }

    private async Task CopyTaxSnapshotAsync(SalesTransaction original, SalesTransaction refund, CancellationToken ct)
    {
        var snapshot = await _db.SaleTaxSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TransactionId == original.Id, ct);

        if (snapshot is null)
        {
            return;
        }

        // Carried across rather than read from configuration: the refund has to be computed at the
        // rates the customer actually paid, which a later rate change must not disturb (guide p.56).
        _db.SaleTaxSnapshots.Add(new SaleTaxSnapshot
        {
            TransactionId = refund.Id,
            Tax1Name = snapshot.Tax1Name,
            Tax1Rate = snapshot.Tax1Rate,
            Tax2Name = snapshot.Tax2Name,
            Tax2Rate = snapshot.Tax2Rate,
            Tax2Compound = snapshot.Tax2Compound,
            AddOnName = snapshot.AddOnName,
            AddOnRate = snapshot.AddOnRate,
            AddOnTaxable = snapshot.AddOnTaxable,
            TaxInclusive = snapshot.TaxInclusive,
            TaxRegistrationNumber = snapshot.TaxRegistrationNumber,
        });
    }

    private async Task<long?> CurrentDrawerSessionIdAsync(long stationId, CancellationToken ct)
        => await _db.DrawerSessions.AsNoTracking()
            .Where(d => d.StationId == stationId && d.Status == DrawerSessionStatus.Open)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(ct);

    private async Task<Currency> CurrencyForAsync(long locationId, CancellationToken ct)
    {
        var code = await _db.Locations.AsNoTracking()
            .Where(l => l.Id == locationId)
            .Select(l => l.BaseCurrencyCode)
            .FirstOrDefaultAsync(ct);

        return await _db.Currencies.AsNoTracking().FirstAsync(c => c.Code == code, ct);
    }

    private sealed record PlannedRefund(
        SaleLine Line,
        decimal Quantity,
        decimal Net,
        decimal Tax1,
        decimal Tax2,
        decimal Cost)
    {
        public decimal Gross => Net + Tax1 + Tax2;
    }
}
