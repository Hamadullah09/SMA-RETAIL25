using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Server;

public sealed record SaleListRow(Guid Id, string StoreCode, int StationNumber, long InvoiceNumber, DateTime At, string? Staff, string? Client, decimal Units,
    decimal NetSales, decimal Tax, decimal Total, string Tenders, bool Voided);
public sealed record SaleLineInfo(int LineNumber, string? Plu, string Description, decimal Quantity, decimal Price, decimal Discount, decimal Tax, decimal Total);
public sealed record SalePaymentInfo(string Tender, decimal Amount, string? Currency, decimal ForeignAmount);
public sealed record SaleDetail(Guid Id, string StoreCode, int StationNumber, long InvoiceNumber, DateTime At, string? Staff, string? Client, decimal Units,
    decimal NetSales, decimal Tax, decimal Total, string Tenders, bool Voided, IReadOnlyList<SaleLineInfo> Lines, IReadOnlyList<SalePaymentInfo> Payments,
    string? VoidedBy, DateTime? VoidedAt);

public static partial class Endpoints
{
    private static void MapSales(RouteGroupBuilder api)
    {
        api.MapGet("/sales", SalesAsync).RequireAuthorization(Need(Permissions.ReportsView));
        api.MapGet("/sales/{id:guid}", SaleAsync).RequireAuthorization(Need(Permissions.ReportsView));
    }

    private static async Task<IResult> SalesAsync(ApiRequest api)
    {
        var store = api.ListStore();
        var (from, to) = await api.PeriodAsync(store);
        await using var db = api.Root.Create();
        var sales = SalesOf(db, store, from, to);
        if (api.Number("station") is { } station) sales = sales.Where(s => s.StationNumber == station);
        if (api.Id("userId") is { } user) sales = sales.Where(s => s.StaffId == user);
        switch (api.Text("status")?.ToLowerInvariant())
        {
            case "completed": sales = sales.Where(s => s.Status == SaleStatus.Completed); break;
            case "voided": sales = sales.Where(s => s.Status == SaleStatus.Voided); break;
            case null: break;
            default: throw ApiException.Bad("status is completed or voided.");
        }
        if (api.Text("search") is { } search)
        {
            long? number = long.TryParse(search, out var n) ? n : null;
            sales = sales.Where(s => s.InvoiceNumber == number || (s.ClientName != null && s.ClientName.Contains(search))
                                     || db.SaleLines.Any(l => l.SaleId == s.Id && l.Plu == search));
        }
        var codes = await StoreCodesAsync(db);
        var rows = sales.OrderByDescending(s => s.SoldAt).ThenByDescending(s => s.InvoiceNumber).Select(s => new
        {
            s.Id, s.StoreId, s.StationNumber, s.InvoiceNumber, s.SoldAt, s.Staff, s.ClientName, s.Subtotal, s.SubtotalDiscount, s.Tax1, s.Tax2, s.Total, s.Status,
            Units = s.Lines.Where(l => l.Kind == SaleLineKind.Sale || l.Kind == SaleLineKind.Return).Sum(l => l.Quantity),
            Tenders = s.Payments.Select(p => p.TenderType).ToList(),
        });
        return Results.Ok(await api.PageAsync(rows, s => new SaleListRow(s.Id, codes.GetValueOrDefault(s.StoreId) ?? "", s.StationNumber, s.InvoiceNumber, s.SoldAt,
            s.Staff, s.ClientName, s.Units, s.Subtotal - s.SubtotalDiscount, s.Tax1 + s.Tax2, s.Total, string.Join(", ", s.Tenders.Distinct()), s.Status == SaleStatus.Voided)));
    }

    private static async Task<IResult> SaleAsync(Guid id, ApiRequest api)
    {
        await using var db = api.Root.Create();
        var sale = await db.Sales.AsNoTracking().AsSplitQuery().Include(s => s.Lines).Include(s => s.Payments).SingleOrDefaultAsync(s => s.Id == id)
                   ?? throw ApiException.NotFound("That sale");
        api.RequireStore(sale.StoreId, "That sale");
        var codes = await StoreCodesAsync(db);
        return Results.Ok(new SaleDetail(sale.Id, codes.GetValueOrDefault(sale.StoreId) ?? "", sale.StationNumber, sale.InvoiceNumber, sale.SoldAt, sale.Staff,
            sale.ClientName, sale.Lines.Where(l => l.Kind is SaleLineKind.Sale or SaleLineKind.Return).Sum(l => l.Quantity), sale.Subtotal - sale.SubtotalDiscount,
            sale.Tax1 + sale.Tax2, sale.Total, string.Join(", ", sale.Payments.Select(p => p.TenderType).Distinct()), sale.Status == SaleStatus.Voided,
            [.. sale.Lines.OrderBy(l => l.LineNumber).Select(l => new SaleLineInfo(l.LineNumber, l.Plu, l.Description, l.Quantity, l.UnitPrice, l.DiscountAmount, l.Tax1 + l.Tax2, l.LineTotal))],
            [.. sale.Payments.Select(p => new SalePaymentInfo(p.TenderType, p.Amount, p.Currency, p.ForeignAmount))],
            sale.VoidedBy, sale.VoidedAt));
    }

    private static Task<Dictionary<Guid, string>> StoreCodesAsync(Data.RetailPlusDbContext db) => db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code);
}
