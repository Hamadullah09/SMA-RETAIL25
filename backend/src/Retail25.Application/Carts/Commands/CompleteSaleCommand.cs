using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;
using Retail25.Application.Carts.Services;
using Retail25.Application.Common;
using Retail25.Application.Receipts;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Catalog;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;
using Retail25.Domain.Inventory;
using Retail25.Domain.Receivables;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;
using Retail25.Domain.Staff;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Carts.Commands;

/// <summary>One leg of the split payment as the till sends it (guide p.8).</summary>
public sealed record TenderRequest(
    long TenderTypeId,
    decimal Amount,
    decimal AmountTendered = 0m,
    string? Reference = null,
    string? CardToken = null,
    long? CurrencyId = null,
    decimal ExchangeRate = 1m);

public sealed record CompleteSaleResult(
    long TransactionId,
    long TransactionNumber,
    decimal GrandTotal,
    decimal ChangeGiven,
    decimal RoundingAdjustment,
    int LoyaltyPointsEarned,
    long? InvoiceId,
    ReceiptDocument? Receipt);

/// <summary>
/// Turns a cart into a sale: prices it one last time, settles the tenders, writes the transaction and
/// every ledger it touches, then releases the till.
/// <para>
/// All of it happens in one database transaction, and the money is recomputed here rather than
/// trusted from the client. A till that has been sitting on a quote for ten minutes may be quoting a
/// price that expired at midnight, and the customer pays what the engine says now.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Pos.Sell)]
public sealed record CompleteSaleCommand(
    long CartId,
    IReadOnlyList<TenderRequest> Tenders,
    string IdempotencyKey,
    bool PrintReceipt = true,
    int Copies = 1,
    ReceiptFormat Format = ReceiptFormat.Slip40) : IRequest<Result<CompleteSaleResult>>, IIdempotentCommand;

public sealed class CompleteSaleHandler : IRequestHandler<CompleteSaleCommand, Result<CompleteSaleResult>>
{
    public static readonly Error TenderTypeUnknown = new("tender.type_unknown", "That tender type is not configured.");
    public static readonly Error CreditLimitExceeded = new("credit.limit_exceeded", "This sale would take the customer past their credit limit.");
    public static readonly Error AccountRequired = new("credit.account_required", "An on-account tender needs a customer with an account.");
    public static readonly Error DrawerRequired = new("drawer.not_open", "Open a drawer before taking cash.");
    public static readonly Error PaymentDeclined = new("payment.declined", "The card was declined.");
    public static readonly Error TrainingCashOnly = new(
        "training.cash_only",
        "A training sale can only be settled in cash or by a tender that records a reference.");

    /// <summary>
    /// The legacy trainee level (guide p.82). Everything rung by someone at this level is practice.
    /// </summary>
    private const int TrainingAccessLevel = 0;

    private readonly ICartStore _store;
    private readonly IApplicationDbContext _db;
    private readonly PosContextLoader _contextLoader;
    private readonly CartPricingService _pricing;
    private readonly ISequenceGenerator _sequences;
    private readonly IPaymentGateway _payments;
    private readonly IPosNotifier _notifier;
    private readonly ITerminalNotifier _terminals;
    private readonly ITagDebouncer _debouncer;
    private readonly ReceiptBuilder _receipts;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTime _clock;

    public CompleteSaleHandler(
        ICartStore store,
        IApplicationDbContext db,
        PosContextLoader contextLoader,
        CartPricingService pricing,
        ISequenceGenerator sequences,
        IPaymentGateway payments,
        IPosNotifier notifier,
        ITerminalNotifier terminals,
        ITagDebouncer debouncer,
        ReceiptBuilder receipts,
        ICurrentUser currentUser,
        IDateTime clock)
    {
        _store = store;
        _db = db;
        _contextLoader = contextLoader;
        _pricing = pricing;
        _sequences = sequences;
        _payments = payments;
        _notifier = notifier;
        _terminals = terminals;
        _debouncer = debouncer;
        _receipts = receipts;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<CompleteSaleResult>> Handle(CompleteSaleCommand request, CancellationToken ct)
    {
        var snapshot = await _store.GetAsync(request.CartId, ct);
        if (snapshot is null || !snapshot.Cart.IsActive)
        {
            return Result.Failure<CompleteSaleResult>(Cart.NotActive.With("cartId", request.CartId));
        }

        if (snapshot.Lines.Count == 0)
        {
            return Result.Failure<CompleteSaleResult>(Cart.Empty);
        }

        var contextResult = await _contextLoader.LoadAsync(snapshot.Cart.StationId, ct);
        if (contextResult.IsFailure)
        {
            return Result.Failure<CompleteSaleResult>(contextResult.Error);
        }

        var context = contextResult.Value;
        var quote = await _pricing.QuoteAsync(snapshot, context, ct);
        var pricing = quote.Pricing;

        var tenderTypes = await _db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);

        var now = _clock.Now;
        var staffId = _currentUser.StaffId ?? snapshot.Cart.StaffId;

        // Derived here and nowhere else. A client-supplied flag would let anyone mark a real sale as
        // practice and make it vanish from every report. Worked out before settlement because a
        // trainee must not be able to reach the payment gateway at all.
        var isTraining = await IsTrainingSaleAsync(staffId, ct);

        if (isTraining)
        {
            var tenderCheck = CheckTrainingTenders(request, tenderTypes);

            if (tenderCheck.IsFailure)
            {
                return Result.Failure<CompleteSaleResult>(tenderCheck.Error);
            }
        }

        var settlement = await SettleAsync(request, pricing, tenderTypes, context, snapshot, ct);
        if (settlement.IsFailure)
        {
            return Result.Failure<CompleteSaleResult>(settlement.Error);
        }

        var settled = settlement.Value;

        var drawerSession = await _db.DrawerSessions
            .FirstOrDefaultAsync(d => d.StationId == snapshot.Cart.StationId && d.Status == DrawerSessionStatus.Open, ct);

        // A trainee practising on a till whose drawer nobody has opened is the normal case, and a
        // training sale never moves the balance anyway — so the requirement does not apply.
        var needsDrawer = !isTraining && settled.Tenders.Any(t => tenderTypes[t.TenderTypeId].CountsTowardsDrawerCash);
        if (needsDrawer && drawerSession is null)
        {
            return Result.Failure<CompleteSaleResult>(DrawerRequired);
        }

        var transaction = new SalesTransaction
        {
            TransactionNumber = await _sequences.NextTransactionNumberAsync(context.Location.Id, ct),
            LocationId = context.Location.Id,
            StationId = snapshot.Cart.StationId,
            StaffId = staffId,
            CustomerId = snapshot.Cart.CustomerId,

            // Not attached to the drawer when it is practice: a session that lists a training sale
            // would not reconcile against the cash actually in the till.
            DrawerSessionId = isTraining ? null : drawerSession?.Id,
            BusinessDate = context.BusinessDate,
            Subtotal = pricing.Subtotal,
            DiscountTotal = pricing.AdjustmentTotal,
            AddOnChargeTotal = pricing.AddOnCharge,
            Tax1Total = pricing.Tax1Total,
            Tax2Total = pricing.Tax2Total,
            GrandTotal = pricing.GrandTotal,
            RoundingAdjustment = settled.RoundingAdjustment,
            ChangeGiven = settled.ChangeDue,
            CostOfGoodsSold = pricing.CostOfGoodsSold,
            LoyaltyPointsEarned = pricing.LoyaltyPointsEarned,
            LoyaltyPointsRedeemed = pricing.LoyaltyPointsRedeemed,
            Status = TransactionStatus.Completed,
            IsTraining = isTraining,
            CompletedAt = now,
            CreatedAt = now,
            CreatedBy = _currentUser.UserId,
        };

        _db.SalesTransactions.Add(transaction);

        // Saved here, before anything reads its id.
        //
        // The transaction's id is assigned by the database, so it is 0 until this line. Eight things
        // below take it — the tax snapshot, every sale line, every tender, the loyalty entry, the
        // cart's completion marker, the receipt — and under the previous GUID keys they could all be
        // built first because the id existed the moment the object did.
        //
        // This is still one database transaction: the pipeline's TransactionBehavior wraps the whole
        // handler, so a failure after this point rolls the sale back exactly as before.
        await _db.SaveChangesAsync(ct);

        _db.SaleTaxSnapshots.Add(SaleTaxSnapshot.From(transaction.Id, context.Tax));

        var saleLines = WriteLines(transaction, snapshot, pricing);
        WriteAdjustments(transaction, snapshot, pricing);
        WriteTenders(transaction, settled, tenderTypes);

        // Everything below this line is what makes a sale real: stock leaves the shelf, points are
        // earned, a card is spent, a drawer balance moves, commission is owed. A training sale runs
        // the whole flow and writes the transaction, its lines and its tenders — so the trainee sees
        // a normal till and the shape of what they did is on record — and then touches none of it.
        if (!isTraining)
        {
            await ApplyStockEffectsAsync(transaction, snapshot, pricing, context, ct);
            await ApplySerializedUnitsAsync(snapshot, ct);
            await ApplyLoyaltyAsync(transaction, snapshot, pricing, context, now, ct);
            await ApplyGiftCertificatesAsync(snapshot, settled, tenderTypes, ct);
            await ApplyGiftCardsAsync(settled, tenderTypes, ct);

            var invoiceResult = await ApplyOnAccountAsync(transaction, settled, tenderTypes, context, now, ct);
            if (invoiceResult.IsFailure)
            {
                return Result.Failure<CompleteSaleResult>(invoiceResult.Error);
            }

            if (drawerSession is not null)
            {
                ApplyDrawerEffects(drawerSession, transaction, settled, tenderTypes, pricing, staffId, now);
            }

            await ApplyCommissionsAsync(transaction, pricing, saleLines, staffId, ct);
        }

        snapshot.Cart.Complete(transaction.Id, now);
        await PersistCompletedCartAsync(snapshot, ct);

        await _db.SaveChangesAsync(ct);
        await _store.RemoveAsync(snapshot.Cart.Id, snapshot.Cart.StationId, ct);

        await BroadcastAsync(transaction, snapshot, drawerSession, ct);

        ReceiptDocument? receipt = null;
        if (request.PrintReceipt)
        {
            receipt = await _receipts.BuildAsync(transaction.Id, request.Format, isReprint: false, ct);
            if (receipt is not null)
            {
                var copies = Math.Max(1, request.Copies);

                // A card sale prints an extra signature copy where the store asks for one (guide p.79).
                if (settled.Tenders.Any(t => tenderTypes[t.TenderTypeId].PrintsSignatureCopy))
                {
                    copies++;
                }

                await _terminals.PrintReceiptAsync(snapshot.Cart.StationId, receipt, copies, ct);
            }
        }

        if (settled.Tenders.Any(t => tenderTypes[t.TenderTypeId].OpensCashDrawer))
        {
            await _terminals.OpenDrawerAsync(snapshot.Cart.StationId, ct);
        }

        return Result.Success(new CompleteSaleResult(
            transaction.Id,
            transaction.TransactionNumber,
            transaction.GrandTotal,
            settled.ChangeDue,
            settled.RoundingAdjustment,
            transaction.LoyaltyPointsEarned,
            transaction.InvoiceId,
            receipt));
    }

    /// <summary>
    /// Authorises any card legs, then hands everything to the tender calculator so cash rounding and
    /// change are worked out in one place.
    /// </summary>
    private async Task<Result<TenderSettlement>> SettleAsync(
        CompleteSaleCommand request,
        SalePricingResult pricing,
        IReadOnlyDictionary<long, TenderType> tenderTypes,
        PosContext context,
        CartSnapshot snapshot,
        CancellationToken ct)
    {
        var legs = new List<TenderInputLine>(request.Tenders.Count);

        foreach (var tender in request.Tenders)
        {
            if (!tenderTypes.TryGetValue(tender.TenderTypeId, out var type))
            {
                return Result.Failure<TenderSettlement>(TenderTypeUnknown.With("tenderTypeId", tender.TenderTypeId));
            }

            string? authCode = null;
            string? cardLast4 = null;

            if (type.Behaviour == TenderBehaviour.Card)
            {
                var payment = await _payments.AuthorizeAsync(tender.Amount, context.Currency.Code, tender.CardToken, ct);
                if (payment.Status != PaymentResultStatus.Approved)
                {
                    return Result.Failure<TenderSettlement>(
                        PaymentDeclined.With("status", payment.Status.ToString()).With("message", payment.ErrorMessage));
                }

                authCode = payment.AuthCode;
                cardLast4 = payment.CardLast4;
            }

            if (type.RequiresReference && string.IsNullOrWhiteSpace(tender.Reference) && authCode is null)
            {
                return Result.Failure<TenderSettlement>(
                    new Error("tender.reference_required", "This tender needs a reference.").With("tenderTypeId", tender.TenderTypeId));
            }

            legs.Add(new TenderInputLine(
                tender.TenderTypeId,
                type.Behaviour,
                type.RoundsToMinimumTender,
                type.AllowsOverTender,
                tender.Amount,
                tender.AmountTendered,
                tender.ExchangeRate,
                tender.CurrencyId,
                tender.Reference,
                authCode,
                cardLast4));
        }

        var settlement = TenderCalculator.Settle(pricing.GrandTotal, legs, context.Rounding);
        if (settlement.IsFailure)
        {
            return settlement;
        }

        if (!settlement.Value.IsSettled)
        {
            return Result.Failure<TenderSettlement>(TenderCalculator.Mismatch
                .With("due", pricing.GrandTotal)
                .With("outstanding", settlement.Value.OutstandingBalance)
                .With("cartId", snapshot.Cart.Id));
        }

        return settlement;
    }

    /// <summary>
    /// Writes the sale's lines and hands them back, because commission is recorded per line and
    /// needs the identity of the row it was earned on.
    /// </summary>
    private List<SaleLine> WriteLines(SalesTransaction transaction, CartSnapshot snapshot, SalePricingResult pricing)
    {
        // By Sequence: a cached cart's lines have no database id to key on.
        var cartLines = snapshot.Lines.ToDictionary(l => l.Sequence);
        var written = new List<SaleLine>(pricing.Lines.Count);

        foreach (var resolved in pricing.Lines)
        {
            cartLines.TryGetValue(resolved.Sequence, out var cartLine);

            var line = new SaleLine
            {
                TransactionId = transaction.Id,
                Sequence = resolved.Sequence,
                ProductId = resolved.ProductId,
                VariantId = resolved.VariantId,
                SerializedUnitId = cartLine?.SerializedUnitId,
                Epc = cartLine?.Epc,
                StockCodeSnapshot = resolved.StockCode,
                NameSnapshot = cartLine?.NameSnapshot ?? resolved.Name,
                Source = cartLine?.Source ?? LineSource.Manual,
                Quantity = resolved.Quantity,
                ChargeableQuantity = resolved.ChargeableQuantity,
                UnitPrice = resolved.UnitPrice,
                DiscountPct = resolved.DiscountPct,
                ExtendedNet = resolved.LineNet,
                ProratedAdjustment = resolved.ProratedAdjustment,
                TaxableNet = resolved.TaxableNet,
                Tax1Applies = resolved.Tax1Applies,
                Tax2Applies = resolved.Tax2Applies,
                Tax1Amount = resolved.Tax1Amount,
                Tax2Amount = resolved.Tax2Amount,
                UnitCostSnapshot = resolved.UnitCost,
                PriceOrigin = resolved.PriceOrigin,
                LineType = resolved.LineType,
                ReturnedToStock = cartLine?.ReturnToStock ?? true,
                Note = cartLine?.Note,
            };

            _db.SaleLines.Add(line);
            written.Add(line);
        }

        return written;
    }

    private void WriteAdjustments(SalesTransaction transaction, CartSnapshot snapshot, SalePricingResult pricing)
    {
        foreach (var applied in pricing.Adjustments)
        {
            var source = snapshot.Adjustments.FirstOrDefault(a => a.Type == applied.Type && a.Label == applied.Label);

            _db.SaleAdjustments.Add(new SaleAdjustment
            {
                TransactionId = transaction.Id,
                Type = applied.Type,
                Label = applied.Label,
                Amount = applied.Amount,
                Serial = source?.Serial,
            });
        }
    }

    private void WriteTenders(
        SalesTransaction transaction,
        TenderSettlement settlement,
        IReadOnlyDictionary<long, TenderType> tenderTypes)
    {
        foreach (var tender in settlement.Tenders)
        {
            _db.SaleTenders.Add(new SaleTender
            {
                TransactionId = transaction.Id,
                TenderTypeId = tender.TenderTypeId,
                Behaviour = tenderTypes[tender.TenderTypeId].Behaviour,
                Amount = tender.Amount,
                AmountTendered = tender.AmountTendered,
                ChangeGiven = tender.ChangeGiven,
                CurrencyId = tender.CurrencyId,
                ExchangeRate = tender.ExchangeRate,
                Reference = tender.Reference,
                AuthCode = tender.AuthCode,
                CardLast4 = tender.CardLast4,
            });
        }
    }

    /// <summary>
    /// Writes the stock ledger and moves the derived levels. Kits explode into their components
    /// (guide p.41) because it is the components that actually leave the shelf.
    /// </summary>
    private async Task ApplyStockEffectsAsync(
        SalesTransaction transaction,
        CartSnapshot snapshot,
        SalePricingResult pricing,
        PosContext context,
        CancellationToken ct)
    {
        var productIds = pricing.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var kitComponents = await _db.KitComponents.AsNoTracking()
            .Where(k => productIds.Contains(k.KitProductId))
            .ToListAsync(ct);

        foreach (var line in pricing.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                continue;
            }

            var cartLine = snapshot.Lines.FirstOrDefault(l => l.Sequence == line.Sequence);

            // A return only puts stock back if the cashier said the goods came back (guide p.7).
            var signedQuantity = line.LineType switch
            {
                LineType.Sale => -line.Quantity,
                LineType.Return => cartLine?.ReturnToStock == false ? 0m : line.Quantity,
                LineType.TradeIn => line.Quantity,
                _ => 0m,
            };

            if (product.Type == ProductType.Kit)
            {
                foreach (var component in kitComponents.Where(k => k.KitProductId == product.Id && k.ReduceStock))
                {
                    await MoveStockAsync(
                        transaction,
                        component.ComponentProductId,
                        null,
                        context.Location.Id,
                        signedQuantity * component.Quantity,
                        0m,
                        MovementType.KitExplode,
                        ct);
                }

                continue;
            }

            if (!TracksStock(product.Type) || signedQuantity == 0m)
            {
                continue;
            }

            await MoveStockAsync(
                transaction,
                product.Id,
                line.VariantId,
                context.Location.Id,
                signedQuantity,
                line.UnitCost,
                line.LineType == LineType.Sale ? MovementType.Sale : MovementType.ReturnIn,
                ct);

            product.UpdateStockLevels(product.OnHand + signedQuantity, product.OnOrder);
        }
    }

    /// <summary>Services, shipping, admissions and gift cards have no shelf presence to move (guide p.30–31).</summary>
    private static bool TracksStock(ProductType type) => type is not (
        ProductType.NonStock or ProductType.Service or ProductType.Shipping or
        ProductType.Admission or ProductType.GiftCard or ProductType.Rental);

    private async Task MoveStockAsync(
        SalesTransaction transaction,
        long productId,
        long? variantId,
        long locationId,
        decimal signedQuantity,
        decimal unitCost,
        MovementType movementType,
        CancellationToken ct)
    {
        _db.StockLedgerEntries.Add(new StockLedgerEntry
        {
            ProductId = productId,
            VariantId = variantId,
            LocationId = locationId,
            MovementType = movementType,
            Quantity = signedQuantity,
            UnitCost = unitCost,
            ReferenceType = nameof(SalesTransaction),
            ReferenceId = transaction.Id,
            OccurredAt = transaction.CompletedAt,
            StaffId = transaction.StaffId,
        });

        var level = await _db.StockLevels
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.VariantId == variantId && s.LocationId == locationId, ct);

        if (level is null)
        {
            level = StockLevel.Create(productId, variantId, locationId);
            _db.StockLevels.Add(level);
        }

        level.OnHand += signedQuantity;
        if (signedQuantity < 0m)
        {
            level.LastSoldOn = transaction.CompletedAt;
        }

        if (variantId is { } id)
        {
            var variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == id, ct);
            variant?.UpdateStock(variant.OnHand + signedQuantity);
        }
    }

    /// <summary>
    /// Moves every tagged unit on the cart from InCart to Sold. The transition is guarded in the
    /// domain, so a unit a second station already sold cannot be sold twice (doc 06 §1).
    /// </summary>
    private async Task ApplySerializedUnitsAsync(CartSnapshot snapshot, CancellationToken ct)
    {
        var unitIds = snapshot.Lines.Where(l => l.SerializedUnitId.HasValue).Select(l => l.SerializedUnitId!.Value).ToList();
        if (unitIds.Count == 0)
        {
            return;
        }

        var units = await _db.SerializedUnits.Where(u => unitIds.Contains(u.Id)).ToListAsync(ct);

        foreach (var unit in units)
        {
            var line = snapshot.Lines.First(l => l.SerializedUnitId == unit.Id);

            if (line.LineType == LineType.Return)
            {
                unit.Return();
                continue;
            }

            unit.Sell();

            if (!string.IsNullOrWhiteSpace(unit.Epc))
            {
                await _debouncer.ReleaseAsync(unit.Epc, snapshot.Cart.StationId, ct);
            }
        }
    }

    /// <summary>
    /// Points earned and points spent both land on the ledger; the profile snapshot is derived from
    /// it. A return later claws back at the rate stored on the earning entry (decision P5).
    /// </summary>
    private async Task ApplyLoyaltyAsync(
        SalesTransaction transaction,
        CartSnapshot snapshot,
        SalePricingResult pricing,
        PosContext context,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (snapshot.Cart.CustomerId is not { } customerId || context.Loyalty is not { IsEnabled: true })
        {
            return;
        }

        var profile = await _db.CustomerPricingProfiles.FirstOrDefaultAsync(p => p.CustomerId == customerId, ct);
        if (profile is null)
        {
            profile = CustomerPricingProfile.Create(customerId);
            _db.CustomerPricingProfiles.Add(profile);
        }

        if (pricing.LoyaltyPointsRedeemed > 0)
        {
            _db.LoyaltyLedgerEntries.Add(LoyaltyLedgerEntry.Redeem(customerId, pricing.LoyaltyPointsRedeemed, now));
            profile.RewardPoints -= pricing.LoyaltyPointsRedeemed;
        }

        if (pricing.LoyaltyPointsEarned > 0)
        {
            _db.LoyaltyLedgerEntries.Add(LoyaltyLedgerEntry.Earn(customerId, transaction.Id, pricing.LoyaltyPointsEarned, now));
            profile.RewardPoints += pricing.LoyaltyPointsEarned;
        }
    }

    /// <summary>
    /// Whether this sale is practice (guide p.82). True when the person ringing it is at legacy
    /// access level 0.
    /// <para>
    /// Level 0 is the trainee preset, and the legacy system used exactly this to decide. Reading it
    /// from the staff profile rather than taking it from the request is the whole safeguard: a flag
    /// on the wire would let a real sale be marked as practice and disappear from every report.
    /// </para>
    /// </summary>
    private async Task<bool> IsTrainingSaleAsync(long? staffId, CancellationToken ct)
    {
        if (staffId is not { } id)
        {
            return false;
        }

        // Projecting straight to the level and comparing would read a missing profile as level 0 and
        // silently turn a real sale into practice — which is the exact failure this flag exists to
        // prevent. No profile means no evidence of a trainee, so the sale is real.
        var level = await _db.StaffProfiles.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => (int?)s.AccessLevel)
            .FirstOrDefaultAsync(ct);

        return level == TrainingAccessLevel;
    }

    /// <summary>
    /// A training sale settles in cash and nothing else.
    /// <para>
    /// The other behaviours all reach something real — a card is authorised with the gateway, a gift
    /// card's balance is spent, an on-account tender raises an invoice against a live customer
    /// account. None of those are things to hand a trainee to practise on, and refusing here is
    /// clearer than letting the tender through and quietly not applying it.
    /// </para>
    /// </summary>
    private static Result CheckTrainingTenders(
        CompleteSaleCommand request, IReadOnlyDictionary<long, TenderType> tenderTypes)
    {
        foreach (var tender in request.Tenders)
        {
            if (!tenderTypes.TryGetValue(tender.TenderTypeId, out var type))
            {
                return Result.Failure(TenderTypeUnknown.With("tenderTypeId", tender.TenderTypeId));
            }

            // Manual is the cheque-and-reference shape: it records a number and reaches nothing.
            if (type.Behaviour is not (TenderBehaviour.Cash or TenderBehaviour.Manual))
            {
                return Result.Failure(TrainingCashOnly.With("tender", type.DisplayName));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Records what the sale earned the person who rang it (guide p.33, p.76).
    /// <para>
    /// Written to a ledger rather than computed on demand, so a report of what someone was paid does
    /// not restate itself the moment a commission rate changes.
    /// </para>
    /// <para>
    /// <b>Known limitation.</b> Voiding a sale does not claw its commission back — the reversal is a
    /// separate transaction that this handler never sees. A return processed through the till does
    /// produce a negative award, but it lands on whoever handled the return rather than on whoever
    /// made the sale. Both are payroll-visible rather than silent, and both are deliberate for now.
    /// </para>
    /// </summary>
    private async Task ApplyCommissionsAsync(
        SalesTransaction transaction,
        SalePricingResult pricing,
        IReadOnlyList<SaleLine> saleLines,
        long? staffId,
        CancellationToken ct)
    {
        if (staffId is not { } id)
        {
            return;
        }

        var rules = await _db.CommissionRules.AsNoTracking()
            .Where(r => r.StaffId == id && r.IsActive)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            return;
        }

        var productIds = pricing.Lines.Select(l => l.ProductId).Distinct().ToList();

        var departments = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.DepartmentId })
            .ToDictionaryAsync(p => p.Id, p => p.DepartmentId, ct);

        var linesById = saleLines.ToDictionary(l => (l.Sequence, l.ProductId));

        foreach (var resolved in pricing.Lines)
        {
            if (!linesById.TryGetValue((resolved.Sequence, resolved.ProductId), out var saleLine))
            {
                continue;
            }

            var award = CommissionCalculator.Award(rules, new CommissionableLine(
                resolved.ProductId,
                departments.GetValueOrDefault(resolved.ProductId),
                resolved.Quantity,
                resolved.LineNet,
                resolved.UnitCost * resolved.Quantity));

            if (award is null)
            {
                continue;
            }

            _db.CommissionLedgerEntries.Add(new CommissionLedgerEntry
            {
                StaffId = id,
                LocationId = transaction.LocationId,
                TransactionId = transaction.Id,
                SaleLineId = saleLine.Id,
                ProductId = resolved.ProductId,
                StockCodeSnapshot = resolved.StockCode,
                DepartmentId = departments.GetValueOrDefault(resolved.ProductId),
                CommissionRuleId = award.Rule.Id,
                CommissionType = award.Rule.CommissionType,
                RateApplied = award.Rule.Value,
                LineNet = resolved.LineNet,
                LineCost = resolved.UnitCost * resolved.Quantity,
                Quantity = resolved.Quantity,
                Amount = award.Amount,
                WasCapped = award.WasCapped,
                BusinessDate = transaction.BusinessDate,
                OccurredAt = transaction.CompletedAt,
            });
        }
    }

    private async Task ApplyGiftCertificatesAsync(
        CartSnapshot snapshot,
        TenderSettlement settlement,
        IReadOnlyDictionary<long, TenderType> tenderTypes,
        CancellationToken ct)
    {
        foreach (var tender in settlement.Tenders)
        {
            if (tenderTypes[tender.TenderTypeId].Behaviour != TenderBehaviour.GiftCertificate
                || string.IsNullOrWhiteSpace(tender.Reference))
            {
                continue;
            }

            var certificate = await _db.GiftCertificates.FirstOrDefaultAsync(g => g.SerialNumber == tender.Reference, ct);
            if (certificate is null)
            {
                continue;
            }

            certificate.RemainingValue = Math.Max(0m, certificate.RemainingValue - tender.Amount);
            certificate.IsActive = certificate.RemainingValue > 0m;
        }

        // Any certificate the cashier recorded as an adjustment is traceability only; the money
        // moves through the tender above.
        await Task.CompletedTask;
    }

    /// <summary>Spends a gift card's stored value by the tendered amount, mirroring the gift-certificate tender above.</summary>
    private async Task ApplyGiftCardsAsync(
        TenderSettlement settlement,
        IReadOnlyDictionary<long, TenderType> tenderTypes,
        CancellationToken ct)
    {
        foreach (var tender in settlement.Tenders)
        {
            if (tenderTypes[tender.TenderTypeId].Behaviour != TenderBehaviour.GiftCard
                || string.IsNullOrWhiteSpace(tender.Reference))
            {
                continue;
            }

            var serial = tender.Reference.Trim().ToUpperInvariant();
            var card = await _db.GiftCards.FirstOrDefaultAsync(g => g.SerialNumber == serial, ct);
            card?.Redeem(tender.Amount);
        }
    }

    /// <summary>
    /// An on-account tender raises an AR invoice (guide p.51). A credit limit of zero means unlimited,
    /// which is the legacy convention and trips up anyone who assumes zero means "no credit".
    /// </summary>
    private async Task<Result> ApplyOnAccountAsync(
        SalesTransaction transaction,
        TenderSettlement settlement,
        IReadOnlyDictionary<long, TenderType> tenderTypes,
        PosContext context,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var onAccount = settlement.Tenders
            .Where(t => tenderTypes[t.TenderTypeId].Behaviour == TenderBehaviour.OnAccount)
            .Sum(t => t.Amount);

        if (onAccount <= 0m)
        {
            return Result.Success();
        }

        if (transaction.CustomerId is not { } customerId)
        {
            return Result.Failure(AccountRequired);
        }

        var account = await _db.CustomerAccounts.FirstOrDefaultAsync(a => a.CustomerId == customerId, ct);
        if (account is null)
        {
            return Result.Failure(AccountRequired.With("customerId", customerId));
        }

        if (account.CreditLimit > 0m && account.BalanceDue + onAccount > account.CreditLimit)
        {
            return Result.Failure(CreditLimitExceeded
                .With("limit", account.CreditLimit)
                .With("balance", account.BalanceDue)
                .With("amount", onAccount));
        }

        var invoice = new Invoice
        {
            InvoiceNumber = await _sequences.NextInvoiceNumberAsync(context.Location.Id, ct),
            CustomerId = customerId,
            TransactionId = transaction.Id,
            IssuedOn = context.BusinessDate,
            DueOn = context.BusinessDate.AddDays(30),
            InvoiceTotal = onAccount,
            BalanceDue = onAccount,
            Status = InvoiceStatus.Open,
            StaffId = transaction.StaffId,
            CreatedAt = now,
        };

        _db.Invoices.Add(invoice);
        _db.ARLedgerEntries.Add(new ARLedgerEntry
        {
            CustomerId = customerId,
            InvoiceId = invoice.Id,
            EntryType = AREntryType.Charge,
            Amount = onAccount,
            OccurredAt = now,
        });

        account.BalanceDue += onAccount;
        transaction.InvoiceId = invoice.Id;

        return Result.Success();
    }

    private void ApplyDrawerEffects(
        DrawerSession session,
        SalesTransaction transaction,
        TenderSettlement settlement,
        IReadOnlyDictionary<long, TenderType> tenderTypes,
        SalePricingResult pricing,
        long staffId,
        DateTimeOffset now)
    {
        var cashMovement = settlement.Tenders
            .Where(t => tenderTypes[t.TenderTypeId].CountsTowardsDrawerCash)
            .Sum(t => t.Amount);

        if (cashMovement != 0m)
        {
            _db.DrawerLedgerEntries.Add(DrawerLedgerEntry.Create(
                session.Id,
                cashMovement >= 0m ? DrawerEntryType.Sale : DrawerEntryType.Refund,
                cashMovement,
                staffId,
                now,
                transactionId: transaction.Id));
        }

        session.RecordSale(pricing.DiscountedSubtotal, pricing.Tax1Total, pricing.Tax2Total, pricing.CostOfGoodsSold);
    }

    /// <summary>Keeps the completed cart in Postgres as the audit trail behind the transaction.</summary>
    private async Task PersistCompletedCartAsync(CartSnapshot snapshot, CancellationToken ct)
    {
        var existing = await _db.Carts.FirstOrDefaultAsync(c => c.Id == snapshot.Cart.Id, ct);
        if (existing is null)
        {
            _db.Carts.Add(snapshot.Cart);
            _db.CartLines.AddRange(snapshot.Lines);
            _db.CartAdjustments.AddRange(snapshot.Adjustments);
            if (snapshot.TaxOverride is not null)
            {
                _db.CartTaxOverrides.Add(snapshot.TaxOverride);
            }

            return;
        }

        existing.Status = snapshot.Cart.Status;
        existing.CompletedTransactionId = snapshot.Cart.CompletedTransactionId;
        existing.ModifiedAt = snapshot.Cart.ModifiedAt;
        existing.ExpiresAt = null;
    }

    private async Task BroadcastAsync(
        SalesTransaction transaction,
        CartSnapshot snapshot,
        DrawerSession? drawerSession,
        CancellationToken ct)
    {
        foreach (var productId in snapshot.Lines.Select(l => l.ProductId).Distinct())
        {
            var onHand = await _db.StockLevels.AsNoTracking()
                .Where(s => s.ProductId == productId && s.LocationId == transaction.LocationId)
                .Select(s => s.OnHand)
                .FirstOrDefaultAsync(ct);

            await _notifier.StockLevelChangedAsync(transaction.LocationId, productId, onHand, ct);
        }

        if (drawerSession is not null)
        {
            await _notifier.DrawerStateChangedAsync(
                snapshot.Cart.StationId,
                new { drawerSession.Id, drawerSession.NetSales, drawerSession.TransactionCount },
                ct);
        }
    }
}
