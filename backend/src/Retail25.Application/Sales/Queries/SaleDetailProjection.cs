using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Sales;

namespace Retail25.Application.Sales.Queries;

/// <summary>
/// One sale in full, as the sales log's drill-down and a customer's invoice history both show it.
/// <para>
/// Shared rather than written twice because the two are the same document seen by different people
/// — a manager reconciling the day and a cashier answering "what did I buy last month?" — and a
/// figure that differed between them would be a figure nobody could trust.
/// </para>
/// </summary>
internal static class SaleDetailProjection
{
    public static async Task<SaleDetailDto> ProjectAsync(
        IApplicationDbContext db,
        SalesTransaction transaction,
        CancellationToken ct)
    {
        var lines = await db.SaleLines.AsNoTracking()
            .Where(l => l.TransactionId == transaction.Id)
            .OrderBy(l => l.Sequence)
            .ToListAsync(ct);

        var tenders = await db.SaleTenders.AsNoTracking()
            .Where(t => t.TransactionId == transaction.Id)
            .ToListAsync(ct);

        // What has already gone back, so the screen offers only what is left. Read from the refund
        // lines themselves rather than a running total, so a refund that rolled back leaves nothing
        // behind to over-count.
        var lineIds = lines.Select(l => l.Id).ToList();

        var refunded = await db.SaleLines.AsNoTracking()
            .Where(l => l.RefundsSaleLineId != null && lineIds.Contains(l.RefundsSaleLineId!.Value))
            .GroupBy(l => l.RefundsSaleLineId!.Value)
            .Select(g => new { SaleLineId = g.Key, Quantity = g.Sum(l => -l.Quantity) })
            .ToDictionaryAsync(x => x.SaleLineId, x => x.Quantity, ct);

        var tenderNames = await db.TenderTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);
        var taxSnapshot = await db.SaleTaxSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.TransactionId == transaction.Id, ct);

        var stationCode = await db.Stations.AsNoTracking()
            .Where(s => s.Id == transaction.StationId).Select(s => s.StationCode).FirstOrDefaultAsync(ct);

        var staffName = await db.StaffProfiles.AsNoTracking()
            .Where(s => s.Id == transaction.StaffId).Select(s => s.FullName).FirstOrDefaultAsync(ct);

        var customerName = transaction.CustomerId is { } customerId
            ? await db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => c.FullName).FirstOrDefaultAsync(ct)
            : null;

        return new SaleDetailDto(
            transaction.Id,
            transaction.TransactionNumber,
            transaction.CompletedAt,
            transaction.Status,
            stationCode ?? string.Empty,
            staffName ?? string.Empty,
            customerName,
            lines.Select(l => new SaleDetailLineDto(
                l.Sequence,
                l.StockCodeSnapshot ?? string.Empty,
                l.NameSnapshot ?? string.Empty,
                l.Quantity,
                l.UnitPrice,
                l.DiscountPct,
                l.ExtendedNet,
                l.Tax1Amount,
                l.Tax2Amount,
                l.PriceOrigin,
                l.LineType,
                l.Epc,
                l.Id,
                refunded.GetValueOrDefault(l.Id),
                l.LineType == LineType.Sale ? l.Quantity - refunded.GetValueOrDefault(l.Id) : 0m)).ToList(),
            tenders.Select(t => new SaleDetailTenderDto(
                tenderNames.TryGetValue(t.TenderTypeId, out var name) ? name : "Tender",
                t.Amount,
                t.AmountTendered,
                t.ChangeGiven,
                t.Reference ?? t.AuthCode)).ToList(),
            transaction.Subtotal,
            transaction.DiscountTotal,
            taxSnapshot?.Tax1Name ?? string.Empty,
            transaction.Tax1Total,
            taxSnapshot?.Tax2Name ?? string.Empty,
            transaction.Tax2Total,
            transaction.AddOnChargeTotal,
            transaction.GrandTotal,
            transaction.ChangeGiven,
            transaction.ReversesTransactionId,
            transaction.VoidedByTransactionId,
            transaction.VoidReason);
    }
}
