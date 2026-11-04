using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>
/// The three-way match's bills (phase E2) are the payables module's supplier bills (phase E1): every line of a posted
/// bill or debit note that names a purchase order line. Drafts are not billed yet and a void bill no longer is, so neither
/// is matched. Quantities of a debit note count against the order (goods sent back); a wrong price is put right by voiding
/// the bill and billing again, not by a debit note. Costs are each before tax, in the store's currency.
/// </summary>
public sealed class SupplierBillLineSource(DataContext data) : IBillLineSource
{
    private static readonly SupplierBillStatus[] Billed = [SupplierBillStatus.Posted, SupplierBillStatus.PartPaid, SupplierBillStatus.Paid];

    public async Task<IReadOnlyList<BillLineInfo>> LinesForOrdersAsync(IReadOnlyCollection<Guid> purchaseOrderIds, CancellationToken cancellationToken = default)
    {
        var ids = purchaseOrderIds.ToList();
        return await LinesAsync(l => l.PurchaseOrderId != null && ids.Contains(l.PurchaseOrderId.Value), cancellationToken);
    }

    public async Task<IReadOnlyList<BillLineInfo>> LinesOfBillAsync(Guid billId, CancellationToken cancellationToken = default) =>
        await LinesAsync(l => l.SupplierBillId == billId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> OrdersBilledAsync(DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var q = from l in db.SupplierBillLines.AsNoTracking()
                join b in db.SupplierBills.AsNoTracking() on l.SupplierBillId equals b.Id
                where l.PurchaseOrderId != null && l.PurchaseOrderLineId != null && Billed.Contains(b.Status)
                      && (fromDate == null || b.BillDate >= fromDate) && (toDate == null || b.BillDate <= toDate)
                select l.PurchaseOrderId!.Value;
        return await q.Distinct().ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<BillLineInfo>> LinesAsync(System.Linq.Expressions.Expression<Func<SupplierBillLine, bool>> which, CancellationToken cancellationToken)
    {
        await using var db = data.Create();
        var rows = await (from l in db.SupplierBillLines.AsNoTracking().Where(which)
                          join b in db.SupplierBills.AsNoTracking() on l.SupplierBillId equals b.Id
                          where l.PurchaseOrderId != null && l.PurchaseOrderLineId != null && Billed.Contains(b.Status)
                          select new { Line = l, Bill = b }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new BillLineInfo(
            r.Bill.Id, r.Line.Id, string.IsNullOrWhiteSpace(r.Bill.SupplierReference) ? r.Bill.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : r.Bill.SupplierReference,
            r.Bill.BillDate, r.Bill.SupplierId, r.Line.PurchaseOrderId!.Value, r.Line.PurchaseOrderLineId!.Value,
            r.Bill.Kind == SupplierBillKind.DebitNote ? -r.Line.Quantity : r.Line.Quantity, r.Line.UnitPrice * r.Bill.ExchangeRate))];
    }
}
