using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;
using Retail25.Application.Common;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Customers;
using Retail25.Domain.Inventory;
using Retail25.Domain.Receivables;
using Retail25.Domain.Sales;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Sales.Commands;

public sealed record VoidSaleResult(long ReversalTransactionId, long ReversalNumber, decimal ReversedTotal);

/// <summary>
/// Voids a completed sale by writing a reversing transaction (guide p.14).
/// <para>
/// The original is never edited. Every ledger the sale touched — stock, drawer, loyalty, AR — gets an
/// equal and opposite entry, so replaying the ledgers from the beginning still lands on the right
/// numbers. That is the whole reason the legacy system needed <c>Rebuild</c> and this one does not.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.VoidSale)]
[SupportsSupervisorApproval]
/// <param name="ApprovalId">
/// A supervisor grant obtained after the first attempt answered 428. Single-use and scoped to this
/// action, so it cannot be banked and spent on a second void.
/// </param>
public sealed record VoidSaleCommand(
    long TransactionId,
    string IdempotencyKey,
    string? Reason = null,
    long? ApprovedByStaffId = null,
    long? ApprovalId = null) : IRequest<Result<VoidSaleResult>>, IIdempotentCommand;

public sealed class VoidSaleHandler : IRequestHandler<VoidSaleCommand, Result<VoidSaleResult>>
{
    public static readonly Error NotFound = new("sale.not_found", "No such sale.");
    public static readonly Error RequiresSupervisor = new("sale.requires_supervisor", "A supervisor must approve this void.");

    private readonly IApplicationDbContext _db;
    private readonly ISequenceGenerator _sequences;
    private readonly IPosNotifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public VoidSaleHandler(
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

    public async Task<Result<VoidSaleResult>> Handle(VoidSaleCommand request, CancellationToken ct)
    {
        var original = await _db.SalesTransactions.FirstOrDefaultAsync(t => t.Id == request.TransactionId, ct);
        if (original is null)
        {
            return Result.Failure<VoidSaleResult>(NotFound.With("transactionId", request.TransactionId));
        }

        var now = _clock.Now;
        var policy = await _db.PosPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.LocationId == original.LocationId, ct);
        var approver = request.ApprovedByStaffId ?? _currentUser.StaffId;

        if (policy?.RequireSupervisorToVoid == true)
        {
            // A cashier who already holds the permission needs no override; anyone else needs a
            // grant, and the grant is spent here so it cannot void a second sale.
            if (!_currentUser.HasPermission(PermissionKeys.Pos.VoidSale) || request.ApprovalId is not null)
            {
                var stepUp = await ConsumeApprovalAsync(request.ApprovalId, ct);
                if (stepUp.IsFailure)
                {
                    return Result.Failure<VoidSaleResult>(stepUp.Error);
                }

                approver = stepUp.Value;
            }

            if (approver is null)
            {
                return Result.Failure<VoidSaleResult>(RequiresSupervisor);
            }
        }

        var reversal = new SalesTransaction
        {
            TransactionNumber = await _sequences.NextTransactionNumberAsync(original.LocationId, ct),
            LocationId = original.LocationId,
            StationId = _currentUser.StationId ?? original.StationId,
            StaffId = _currentUser.StaffId ?? original.StaffId,
            CustomerId = original.CustomerId,
            DrawerSessionId = await CurrentDrawerSessionIdAsync(_currentUser.StationId ?? original.StationId, ct),
            BusinessDate = original.BusinessDate,
            Subtotal = -original.Subtotal,
            DiscountTotal = -original.DiscountTotal,
            AddOnChargeTotal = -original.AddOnChargeTotal,
            Tax1Total = -original.Tax1Total,
            Tax2Total = -original.Tax2Total,
            GrandTotal = -original.GrandTotal,
            RoundingAdjustment = -original.RoundingAdjustment,
            CostOfGoodsSold = -original.CostOfGoodsSold,
            LoyaltyPointsEarned = -original.LoyaltyPointsEarned,
            LoyaltyPointsRedeemed = -original.LoyaltyPointsRedeemed,
            Status = TransactionStatus.Reversal,
            ReversesTransactionId = original.Id,
            VoidReason = request.Reason,
            VoidApprovedByStaffId = approver,
            CompletedAt = now,
            CreatedAt = now,
            CreatedBy = _currentUser.UserId,
        };

        _db.SalesTransactions.Add(reversal);

        // Saved before its id is read. Without this the original sale records "voided by transaction
        // 0" — which points at nothing, so the reversal can never be found from the sale it reverses.
        // Still inside the pipeline's transaction, so a later failure rolls both back together.
        await _db.SaveChangesAsync(ct);

        var voided = original.Void(reversal.Id, approver ?? 0L, request.Reason, now);
        if (voided.IsFailure)
        {
            return Result.Failure<VoidSaleResult>(voided.Error);
        }

        var taxSnapshot = await _db.SaleTaxSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.TransactionId == original.Id, ct);
        if (taxSnapshot is not null)
        {
            _db.SaleTaxSnapshots.Add(new SaleTaxSnapshot
            {
                TransactionId = reversal.Id,
                Tax1Name = taxSnapshot.Tax1Name,
                Tax1Rate = taxSnapshot.Tax1Rate,
                Tax2Name = taxSnapshot.Tax2Name,
                Tax2Rate = taxSnapshot.Tax2Rate,
                Tax2Compound = taxSnapshot.Tax2Compound,
                AddOnName = taxSnapshot.AddOnName,
                AddOnRate = taxSnapshot.AddOnRate,
                AddOnTaxable = taxSnapshot.AddOnTaxable,
                TaxInclusive = taxSnapshot.TaxInclusive,
                TaxRegistrationNumber = taxSnapshot.TaxRegistrationNumber,
            });
        }

        await ReverseLinesAsync(original, reversal, now, ct);
        await ReverseTendersAsync(original, reversal, ct);
        await ReverseLoyaltyAsync(original, now, ct);
        await ReverseReceivablesAsync(original, now, ct);
        await ReverseDrawerAsync(original, reversal, now, ct);

        await _db.SaveChangesAsync(ct);

        await _notifier.CartUpdatedAsync(original.LocationId, reversal.Id, new { reversal.Id, reversal.TransactionNumber }, 0, ct);

        return Result.Success(new VoidSaleResult(reversal.Id, reversal.TransactionNumber, original.GrandTotal));
    }

    /// <summary>
    /// Spends a supervisor grant, returning who gave it. The grant is checked against this exact
    /// action, so an approval obtained for something else cannot unlock a void.
    /// </summary>
    private async Task<Result<long?>> ConsumeApprovalAsync(long? approvalId, CancellationToken ct)
    {
        if (approvalId is not { } id)
        {
            return Result.Failure<long?>(RequiresSupervisor);
        }

        var approval = await _db.SupervisorApprovals.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (approval is null)
        {
            return Result.Failure<long?>(RequiresSupervisor.With("approvalId", id));
        }

        var consumed = approval.Consume(nameof(VoidSaleCommand), _clock.Now);
        if (consumed.IsFailure)
        {
            return Result.Failure<long?>(consumed.Error);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success<long?>(approval.ApprovedByStaffId);
    }

    private async Task<long?> CurrentDrawerSessionIdAsync(long stationId, CancellationToken ct)
        => await _db.DrawerSessions.AsNoTracking()
            .Where(d => d.StationId == stationId && d.Status == DrawerSessionStatus.Open)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(ct);

    private async Task ReverseLinesAsync(
        SalesTransaction original,
        SalesTransaction reversal,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var lines = await _db.SaleLines.AsNoTracking().Where(l => l.TransactionId == original.Id).ToListAsync(ct);

        foreach (var line in lines)
        {
            _db.SaleLines.Add(new SaleLine
            {
                TransactionId = reversal.Id,
                Sequence = line.Sequence,
                ProductId = line.ProductId,
                VariantId = line.VariantId,
                SerializedUnitId = line.SerializedUnitId,
                Epc = line.Epc,
                StockCodeSnapshot = line.StockCodeSnapshot,
                NameSnapshot = line.NameSnapshot,
                Source = line.Source,
                Quantity = -line.Quantity,
                ChargeableQuantity = -line.ChargeableQuantity,
                UnitPrice = line.UnitPrice,
                DiscountPct = line.DiscountPct,
                ExtendedNet = -line.ExtendedNet,
                ProratedAdjustment = -line.ProratedAdjustment,
                TaxableNet = -line.TaxableNet,
                Tax1Applies = line.Tax1Applies,
                Tax2Applies = line.Tax2Applies,
                Tax1Amount = -line.Tax1Amount,
                Tax2Amount = -line.Tax2Amount,
                UnitCostSnapshot = line.UnitCostSnapshot,
                PriceOrigin = line.PriceOrigin,
                LineType = line.LineType,
                Note = line.Note,
            });

            // Stock goes back the way it came, unless nothing moved in the first place.
            if (line.Quantity != 0m)
            {
                var restored = line.LineType == LineType.Sale ? line.Quantity : -line.Quantity;

                _db.StockLedgerEntries.Add(new StockLedgerEntry
                {
                    ProductId = line.ProductId,
                    VariantId = line.VariantId,
                    LocationId = original.LocationId,
                    MovementType = MovementType.Adjustment,
                    Quantity = restored,
                    UnitCost = line.UnitCostSnapshot,
                    ReferenceType = nameof(SalesTransaction),
                    ReferenceId = reversal.Id,
                    Reason = "Void",
                    OccurredAt = now,
                    StaffId = reversal.StaffId,
                });

                var level = await _db.StockLevels.FirstOrDefaultAsync(
                    s => s.ProductId == line.ProductId && s.VariantId == line.VariantId && s.LocationId == original.LocationId,
                    ct);

                if (level is not null)
                {
                    level.OnHand += restored;
                }

                var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId, ct);
                product?.UpdateStockLevels(product.OnHand + restored, product.OnOrder);
            }

            // A voided sale puts its tags back in stock (doc 06 §1).
            if (line.SerializedUnitId is { } unitId)
            {
                var unit = await _db.SerializedUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct);
                if (unit is { State: SerializedUnitState.Sold })
                {
                    unit.Return();
                }
            }
        }
    }

    private async Task ReverseTendersAsync(SalesTransaction original, SalesTransaction reversal, CancellationToken ct)
    {
        var tenders = await _db.SaleTenders.AsNoTracking().Where(t => t.TransactionId == original.Id).ToListAsync(ct);

        foreach (var tender in tenders)
        {
            _db.SaleTenders.Add(new SaleTender
            {
                TransactionId = reversal.Id,
                TenderTypeId = tender.TenderTypeId,
                Behaviour = tender.Behaviour,
                Amount = -tender.Amount,
                AmountTendered = -tender.AmountTendered,
                ChangeGiven = -tender.ChangeGiven,
                CurrencyId = tender.CurrencyId,
                ExchangeRate = tender.ExchangeRate,
                Reference = tender.Reference,
                AuthCode = tender.AuthCode,
                CardLast4 = tender.CardLast4,
                GatewayReference = tender.GatewayReference,
            });
        }
    }

    private async Task ReverseLoyaltyAsync(SalesTransaction original, DateTimeOffset now, CancellationToken ct)
    {
        if (original.CustomerId is not { } customerId || original.LoyaltyPointsEarned == 0)
        {
            return;
        }

        _db.LoyaltyLedgerEntries.Add(
            LoyaltyLedgerEntry.Clawback(customerId, original.Id, original.LoyaltyPointsEarned, now));

        var profile = await _db.CustomerPricingProfiles.FirstOrDefaultAsync(p => p.CustomerId == customerId, ct);
        if (profile is not null)
        {
            profile.RewardPoints -= original.LoyaltyPointsEarned;
        }
    }

    private async Task ReverseReceivablesAsync(SalesTransaction original, DateTimeOffset now, CancellationToken ct)
    {
        if (original.InvoiceId is not { } invoiceId)
        {
            return;
        }

        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is null)
        {
            return;
        }

        _db.ARLedgerEntries.Add(new ARLedgerEntry
        {
            CustomerId = invoice.CustomerId,
            InvoiceId = invoice.Id,
            EntryType = AREntryType.Void,
            Amount = -invoice.BalanceDue,
            OccurredAt = now,
        });

        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.CustomerId == invoice.CustomerId, ct);
        if (account is not null)
        {
            account.BalanceDue -= invoice.BalanceDue;
        }

        invoice.BalanceDue = 0m;
        invoice.Status = InvoiceStatus.Void;
        invoice.ModifiedAt = now;
    }

    private async Task ReverseDrawerAsync(
        SalesTransaction original,
        SalesTransaction reversal,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (reversal.DrawerSessionId is not { } sessionId)
        {
            return;
        }

        var tenderTypes = await _db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var tenders = await _db.SaleTenders.AsNoTracking().Where(t => t.TransactionId == original.Id).ToListAsync(ct);

        var cash = tenders
            .Where(t => tenderTypes.TryGetValue(t.TenderTypeId, out var type) && type.CountsTowardsDrawerCash)
            .Sum(t => t.Amount);

        if (cash == 0m)
        {
            return;
        }

        _db.DrawerLedgerEntries.Add(DrawerLedgerEntry.Create(
            sessionId,
            DrawerEntryType.Refund,
            -cash,
            reversal.StaffId,
            now,
            "Void",
            reversal.Id));

        var session = await _db.DrawerSessions.FirstOrDefaultAsync(d => d.Id == sessionId, ct);
        session?.RecordSale(-original.Subtotal, -original.Tax1Total, -original.Tax2Total, -original.CostOfGoodsSold);
    }
}
