using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;

namespace RetailPlus.Server;

public sealed record MovementRow(DateTime At, string StoreCode, string LocationCode, string Plu, string Name, StockMovementKind Kind, decimal Quantity,
    decimal OnHandAfter, decimal UnitCost, long? DocumentNumber, string? Reason, string? User);
public sealed record TransferListRow(Guid Id, long Number, StockTransferStatus Status, string FromStore, string FromLocation, string ToStore, string ToLocation,
    DateTime CreatedAt, DateTime? ShippedAt, DateTime? ReceivedAt, int Lines, decimal Units, decimal Value);
public sealed record TransferLineInfo(string Plu, string Description, decimal QuantityShipped, decimal QuantityReceived, decimal UnitCost);
public sealed record TransferDetail(Guid Id, long Number, StockTransferStatus Status, string FromStore, string FromLocation, string ToStore, string ToLocation,
    DateTime CreatedAt, DateTime? ShippedAt, DateTime? ReceivedAt, int LineCount, decimal Units, decimal Value, IReadOnlyList<TransferLineInfo> Lines,
    string? CreatedBy, string? ShippedBy, string? ReceivedBy, string? Note);
public sealed record CountListRow(Guid Id, long Number, StockCountStatus Status, string StoreCode, string LocationCode, DateTime StartedAt, DateTime? PostedAt,
    int Lines, decimal VarianceUnits, decimal VarianceValue);
public sealed record CountLineInfo(string Plu, string Description, decimal Expected, decimal Counted, decimal Variance, decimal VarianceCost, bool NotCounted);
public sealed record CountDetail(Guid Id, long Number, StockCountStatus Status, string StoreCode, string LocationCode, DateTime StartedAt, DateTime? PostedAt,
    int LineCount, decimal VarianceUnits, decimal VarianceValue, IReadOnlyList<CountLineInfo> Lines, string? StartedBy, string? PostedBy, string? Note);
public sealed record GiftCardRow(Guid Id, string Number, GiftCardKind Kind, string StoreCode, DateTime IssuedAt, DateOnly? ExpiresOn, bool IsActive, string? Client,
    decimal Balance);
public sealed record GiftCardHistory(DateTime At, GiftCardTransactionKind Kind, decimal Amount, decimal BalanceAfter, string StoreCode, int StationNumber,
    long? InvoiceNumber, string? User, string? Note);
public sealed record GiftCardDetailInfo(Guid Id, string Number, GiftCardKind Kind, string StoreCode, DateTime IssuedAt, DateOnly? ExpiresOn, bool IsActive,
    string? Client, decimal Balance, IReadOnlyList<GiftCardHistory> History);

public static partial class Endpoints
{
    private static void MapStock(RouteGroupBuilder api)
    {
        api.MapGet("/stock/movements", MovementsAsync).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapGet("/transfers", TransfersAsync).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapGet("/transfers/{id:guid}", TransferAsync).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapGet("/stock-counts", CountsAsync).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapGet("/stock-counts/{id:guid}", CountAsync).RequireAuthorization(Need(Permissions.InventoryView));
        api.MapGet("/gift-cards", GiftCardsAsync).RequireAuthorization(Need(Permissions.GiftCardsIssue));
        api.MapGet("/gift-cards/{id:guid}", GiftCardAsync).RequireAuthorization(Need(Permissions.GiftCardsIssue));
    }

    private static async Task<IResult> MovementsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var moves = db.StockMovements.AsNoTracking();
        if (store is { } s) moves = moves.Where(m => m.StoreId == s);
        if (api.Id("itemId") is { } item) moves = moves.Where(m => m.ItemId == item);
        if (api.Text("from") is not null || api.Text("to") is not null)
        {
            var (from, to) = await api.PeriodAsync(store);
            var start = from.ToDateTime(TimeOnly.MinValue);
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            moves = moves.Where(m => m.At >= start && m.At < end);
        }
        if (api.Text("kind") is { } kindText)
            moves = Enum.TryParse<StockMovementKind>(kindText, true, out var kind) ? moves.Where(m => m.Kind == kind) : throw ApiException.Bad($"{kindText} is not a kind of stock movement.");
        var codes = await StoreCodesAsync(db);
        var rows = moves.OrderByDescending(m => m.At).ThenByDescending(m => m.CreatedAtUtc)
            .Join(db.Items, m => m.ItemId, i => i.Id, (m, i) => new { m, i.Plu, i.Name, i.LocationId })
            .Join(db.Locations, x => x.LocationId, l => l.Id, (x, l) => new { x.m, x.Plu, x.Name, Location = l.Code });
        return Results.Ok(await api.PageAsync(rows, x => new MovementRow(x.m.At, codes.GetValueOrDefault(x.m.StoreId) ?? "", x.Location, x.Plu, x.Name, x.m.Kind,
            x.m.Quantity, x.m.OnHandAfter, x.m.UnitCost, x.m.DocumentNumber, x.m.Reason, x.m.User)));
    }

    // Transfers -----------------------------------------------------------------------------------------------------------

    private static async Task<IResult> TransfersAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var transfers = db.StockTransfers.AsNoTracking();
        // A transfer shows at both the sending and the receiving store.
        if (store is { } s) transfers = transfers.Where(StockTransfer.OfStore(s));
        if (api.Text("status") is { } statusText)
            transfers = Enum.TryParse<StockTransferStatus>(statusText, true, out var status) ? transfers.Where(t => t.Status == status) : throw ApiException.Bad($"{statusText} is not a transfer status.");
        // The number, or a location (code or name) it leaves from or goes to.
        if (api.Text("search") is { } text)
        {
            long? number = SearchNumber(text);
            transfers = transfers.Where(t => (number != null && t.Number == number)
                                             || t.FromLocation!.Code.StartsWith(text) || (t.FromLocation.Name ?? "").Contains(text)
                                             || t.ToLocation!.Code.StartsWith(text) || (t.ToLocation.Name ?? "").Contains(text));
        }
        var codes = await StoreCodesAsync(db);
        var rows = transfers.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Number).Select(t => new
        {
            t.Id, t.Number, t.Status, t.StoreId, t.ToStoreId, From = t.FromLocation!.Code, To = t.ToLocation!.Code, t.CreatedAt, t.ShippedAt, t.ReceivedAt,
            Lines = t.Lines.Count, Units = t.Lines.Sum(l => l.QuantityShipped), Value = t.Lines.Sum(l => l.QuantityShipped * l.UnitCost),
        });
        return Results.Ok(await api.PageAsync(rows, t => new TransferListRow(t.Id, t.Number, t.Status, codes.GetValueOrDefault(t.StoreId) ?? "", t.From,
            codes.GetValueOrDefault(t.ToStoreId) ?? "", t.To, t.CreatedAt, t.ShippedAt, t.ReceivedAt, t.Lines, t.Units, Math.Round(t.Value, 2))));
    }

    /// <summary>A search that is a whole number may be a document number.</summary>
    private static long? SearchNumber(string text) =>
        long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    private static async Task<IResult> TransferAsync(Guid id, ApiRequest api)
    {
        await using var db = api.Root.Create();
        var t = await db.StockTransfers.AsNoTracking().Include(x => x.Lines).Include(x => x.FromLocation).Include(x => x.ToLocation).SingleOrDefaultAsync(x => x.Id == id)
                ?? throw ApiException.NotFound("That transfer");
        if (!api.User.CanSee(t.StoreId) && !api.User.CanSee(t.ToStoreId)) throw ApiException.NotFound("That transfer");
        var codes = await StoreCodesAsync(db);
        var lines = t.Lines.OrderBy(l => l.LineNumber).ToList();
        return Results.Ok(new TransferDetail(t.Id, t.Number, t.Status, codes.GetValueOrDefault(t.StoreId) ?? "", t.FromLocation?.Code ?? "", codes.GetValueOrDefault(t.ToStoreId) ?? "",
            t.ToLocation?.Code ?? "", t.CreatedAt, t.ShippedAt, t.ReceivedAt, lines.Count, lines.Sum(l => l.QuantityShipped), Math.Round(lines.Sum(l => l.QuantityShipped * l.UnitCost), 2),
            [.. lines.Select(l => new TransferLineInfo(l.Plu, l.Description, l.QuantityShipped, l.QuantityReceived, l.UnitCost))], t.CreatedBy, t.ShippedBy, t.ReceivedBy, t.Note));
    }

    // Stock counts ------------------------------------------------------------------------------------------------------

    private static async Task<IResult> CountsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var counts = db.StockCounts.AsNoTracking();
        if (store is { } s) counts = counts.Where(c => c.StoreId == s);
        if (api.Text("status") is { } statusText)
            counts = Enum.TryParse<StockCountStatus>(statusText, true, out var status) ? counts.Where(c => c.Status == status) : throw ApiException.Bad($"{statusText} is not a count status.");
        // The number, or the location (code or name) counted.
        if (api.Text("search") is { } text)
        {
            long? number = SearchNumber(text);
            counts = counts.Where(c => (number != null && c.Number == number) || c.Location!.Code.StartsWith(text) || (c.Location.Name ?? "").Contains(text));
        }
        var codes = await StoreCodesAsync(db);
        var rows = counts.OrderByDescending(c => c.StartedAt).ThenByDescending(c => c.Number).Select(c => new
        {
            c.Id, c.Number, c.Status, c.StoreId, Location = c.Location!.Code, c.StartedAt, c.PostedAt, Lines = c.Lines.Count,
            Units = c.Lines.Sum(l => l.Variance), Value = c.Lines.Sum(l => l.VarianceCost),
        });
        return Results.Ok(await api.PageAsync(rows, c => new CountListRow(c.Id, c.Number, c.Status, codes.GetValueOrDefault(c.StoreId) ?? "", c.Location, c.StartedAt,
            c.PostedAt, c.Lines, c.Units, c.Value)));
    }

    private static async Task<IResult> CountAsync(Guid id, ApiRequest api)
    {
        await using var db = api.Root.Create();
        var c = await db.StockCounts.AsNoTracking().Include(x => x.Lines).Include(x => x.Location).SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("That count");
        api.RequireStore(c.StoreId, "That count");
        var codes = await StoreCodesAsync(db);
        var lines = c.Lines.OrderBy(l => l.Plu, StringComparer.OrdinalIgnoreCase).ToList();
        return Results.Ok(new CountDetail(c.Id, c.Number, c.Status, codes.GetValueOrDefault(c.StoreId) ?? "", c.Location?.Code ?? "", c.StartedAt, c.PostedAt, lines.Count,
            lines.Sum(l => l.Variance), lines.Sum(l => l.VarianceCost),
            [.. lines.Select(l => new CountLineInfo(l.Plu, l.Description, l.Expected, l.Counted, l.Variance, l.VarianceCost, l.NotCounted))], c.StartedBy, c.PostedBy, c.Note));
    }

    // Gift cards --------------------------------------------------------------------------------------------------------

    private static string? ClientName(Client? c) => c is null ? null : !string.IsNullOrWhiteSpace(c.Company) ? c.Company : $"{c.FirstName} {c.LastName}".Trim();

    /// <summary>Cards are spent in every store; each store's administrator sees the cards their store issued.</summary>
    private static async Task<IResult> GiftCardsAsync(ApiRequest api)
    {
        var store = api.ListStore();
        await using var db = api.Root.Create();
        var cards = db.GiftCards.AsNoTracking();
        if (store is { } s) cards = cards.Where(c => c.StoreId == s);
        if (api.Bool("active") is { } active) cards = cards.Where(c => c.IsActive == active);
        if (api.Text("search") is { } t)
            cards = cards.Where(c => c.Number.Contains(t) || (c.Client != null && ((c.Client.Company ?? "").StartsWith(t) || (c.Client.LastName ?? "").StartsWith(t))));
        var codes = await StoreCodesAsync(db);
        var rows = cards.OrderByDescending(c => c.IssuedAt).Select(c => new
        {
            c.Id, c.Number, c.Kind, c.StoreId, c.IssuedAt, c.ExpiresOn, c.IsActive, c.Client, Balance = c.Transactions.Sum(x => x.Amount),
        });
        return Results.Ok(await api.PageAsync(rows, c => new GiftCardRow(c.Id, c.Number, c.Kind, codes.GetValueOrDefault(c.StoreId) ?? "", c.IssuedAt, c.ExpiresOn,
            c.IsActive, ClientName(c.Client), c.Balance)));
    }

    private static async Task<IResult> GiftCardAsync(Guid id, ApiRequest api)
    {
        await using var db = api.Root.Create();
        var card = await db.GiftCards.AsNoTracking().Include(c => c.Client).SingleOrDefaultAsync(c => c.Id == id) ?? throw ApiException.NotFound("That gift card");
        api.RequireStore(card.StoreId, "That gift card");
        var history = await db.GiftCardTransactions.AsNoTracking().Where(t => t.GiftCardId == id).OrderBy(t => t.At).ThenBy(t => t.CreatedAtUtc).ToListAsync();
        var codes = await StoreCodesAsync(db);
        decimal running = 0;
        var rows = history.Select(t => new GiftCardHistory(t.At, t.Kind, t.Amount, running += t.Amount, codes.GetValueOrDefault(t.StoreId) ?? "", t.StationNumber,
            t.InvoiceNumber, t.User, t.Note)).Reverse().ToList();
        return Results.Ok(new GiftCardDetailInfo(card.Id, card.Number, card.Kind, codes.GetValueOrDefault(card.StoreId) ?? "", card.IssuedAt, card.ExpiresOn, card.IsActive,
            ClientName(card.Client), running, rows));
    }
}
