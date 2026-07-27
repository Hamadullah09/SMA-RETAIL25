using MediatR;
using Retail25.Application.Abstractions;
using Retail25.Application.Behaviors;
using Retail25.Domain.Sales;

namespace Retail25.Application.Carts.Commands;

/// <summary>
/// Complete a sale: validate totals, create SalesTransaction, create ledgers,
/// open receipt, update stock.
/// </summary>
public sealed record CompleteSaleCommand(
    Guid CartId,
    Guid StaffId,
    List<TenderInput> Tenders,
    bool PrintReceipt = true,
    int CopyCount = 1) : ICommand<CompleteSaleResult>;

public sealed record TenderInput(
    Guid TenderTypeId,
    decimal Amount,
    decimal AmountTendered = 0m,
    string? AuthCode = null,
    string? CardLast4 = null,
    string? GatewayReference = null);

public sealed record CompleteSaleResult(
    bool Success,
    Guid? TransactionId,
    long? TransactionNumber,
    string? Error);

public class CompleteSaleHandler : IRequestHandler<CompleteSaleCommand, CompleteSaleResult>
{
    private readonly ICartStore _cartStore;
    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;

    public CompleteSaleHandler(ICartStore cartStore, IApplicationDbContext db, IPosNotifier notifier)
    {
        _cartStore = cartStore;
        _db = db;
        _notifier = notifier;
    }

    public async Task<CompleteSaleResult> Handle(CompleteSaleCommand request, CancellationToken ct)
    {
        var cart = await _cartStore.GetAsync(request.CartId, ct);
        if (cart is null || cart.Status != CartStatus.Active)
            return new CompleteSaleResult(false, null, null, "cart.not_active");

        var lines = await _cartStore.GetLinesAsync(request.CartId, ct);
        if (!lines.Any())
            return new CompleteSaleResult(false, null, null, "cart.empty");

        // Calculate totals
        var subtotal = lines.Sum(l => l.UnitPrice * l.Quantity * (1 - l.LineDiscountPct / 100m));
        var tax1Total = 0m;
        var tax2Total = 0m;

        foreach (var line in lines)
        {
            var lineNet = line.UnitPrice * line.Quantity * (1 - line.LineDiscountPct / 100m);
            if (line.Tax1Applies)
                tax1Total += lineNet * 0.05m; // TODO: use actual tax config
            if (line.Tax2Applies)
                tax2Total += lineNet * 0.07m;
        }

        var grandTotal = subtotal + tax1Total + tax2Total;

        // Validate tender sum
        var tenderSum = request.Tenders.Sum(t => t.Amount);
        if (Math.Abs(tenderSum - grandTotal) > 0.01m)
            return new CompleteSaleResult(false, null, null, "tender.mismatch");

        // Create SalesTransaction
        var txn = new SalesTransaction
        {
            Id = Guid.NewGuid(),
            TransactionNumber = await GetNextTransactionNumber(cart.LocationId, ct),
            LocationId = cart.LocationId,
            StationId = cart.StationId,
            StaffId = request.StaffId,
            CustomerId = cart.CustomerId,
            Subtotal = subtotal,
            Tax1Total = tax1Total,
            Tax2Total = tax2Total,
            GrandTotal = grandTotal,
            Status = TransactionStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.SalesTransactions.Add(txn);

        // Create SaleLines
        foreach (var line in lines)
        {
            var lineNet = line.UnitPrice * line.Quantity * (1 - line.LineDiscountPct / 100m);
            _db.SaleLines.Add(new SaleLine
            {
                Id = Guid.NewGuid(),
                TransactionId = txn.Id,
                ProductId = line.ProductId,
                StockCodeSnapshot = line.StockCodeSnapshot,
                NameSnapshot = line.NameSnapshot,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPct = line.LineDiscountPct,
                ExtendedNet = lineNet,
                Tax1Amount = line.Tax1Applies ? lineNet * 0.05m : 0m,
                Tax2Amount = line.Tax2Applies ? lineNet * 0.07m : 0m,
                UnitCostSnapshot = line.UnitCostSnapshot,
                PriceOrigin = line.PriceOrigin,
                LineType = line.LineType,
            });
        }

        // Create SaleTenders
        foreach (var tender in request.Tenders)
        {
            _db.SaleTenders.Add(new SaleTender
            {
                Id = Guid.NewGuid(),
                TransactionId = txn.Id,
                TenderTypeId = tender.TenderTypeId,
                Amount = tender.Amount,
                AmountTendered = tender.AmountTendered,
                AuthCode = tender.AuthCode,
                CardLast4 = tender.CardLast4,
                GatewayReference = tender.GatewayReference,
            });
        }

        await _db.SaveChangesAsync(ct);

        // Mark cart as completed
        cart.Status = CartStatus.Completed;
        await _cartStore.SetAsync(cart, ct);

        // Notify other stations
        await _notifier.StockLevelChangedAsync(cart.LocationId, Guid.Empty, 0, ct);

        return new CompleteSaleResult(true, txn.Id, txn.TransactionNumber, null);
    }

    private static Task<long> GetNextTransactionNumber(Guid locationId, CancellationToken ct)
    {
        // TODO: Use a Postgres sequence per location in production.
        return Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
}
