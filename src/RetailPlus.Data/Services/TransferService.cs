using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>A row of the Transfers list. <see cref="Incoming"/>: this store receives it.</summary>
public sealed record TransferRow(Guid Id, long Number, string From, string To, StockTransferStatus Status, DateTime CreatedAt, DateTime? ShippedAt,
    DateTime? ReceivedAt, int Lines, decimal Shipped, decimal Received, bool Incoming, string? Note);

/// <summary>A location a transfer can go to, with its store.</summary>
public sealed record TransferDestination(Guid LocationId, string LocationCode, string? LocationName, Guid StoreId, string StoreCode, string StoreName)
{
    public string Text => $"{StoreCode} · {LocationCode} {LocationName}".Trim();
}

/// <summary>What receiving did: the items created at the destination, and the lines that came short or over.</summary>
public sealed record TransferReceipt(decimal Received, IReadOnlyList<string> CreatedItems, IReadOnlyList<string> ShortLines, IReadOnlyList<string> OverLines);

/// <summary>
/// Transfers of stock between locations, of one store or of two. Ship and receive may happen on different databases:
/// the sending till ships, the synchronisation brings the transfer to the receiving store's tills (see
/// <see cref="StockTransfer.BelongsTo"/>), and one of them receives it.
/// </summary>
public sealed class TransferService(DataContext data)
{
    /// <summary>Every location a transfer can go to: this store's and the other stores' this database knows.</summary>
    public async Task<List<TransferDestination>> DestinationsAsync()
    {
        await using var db = data.Create();
        return await db.Locations.AsNoTracking()
            .Join(db.Stores.AsNoTracking(), l => l.StoreId, s => s.Id, (l, s) => new TransferDestination(l.Id, l.Code, l.Name, s.Id, s.Code, s.Name))
            .OrderBy(d => d.StoreCode).ThenBy(d => d.LocationCode).ToListAsync();
    }

    public async Task<StockTransfer> CreateAsync(Guid fromLocationId, Guid toLocationId, string? note, string? user)
    {
        if (fromLocationId == toLocationId) throw new BusinessRuleException("A transfer goes to another location.");
        await using var db = data.Create();
        var from = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == fromLocationId) ?? throw new BusinessRuleException("The sending location does not exist.");
        var to = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == toLocationId) ?? throw new BusinessRuleException("The receiving location does not exist.");
        var transfer = new StockTransfer
        {
            StoreId = from.StoreId, ToStoreId = to.StoreId, FromLocationId = from.Id, ToLocationId = to.Id, Status = StockTransferStatus.Draft,
            CreatedAt = DateTime.Now, CreatedBy = user, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 200)],
        };
        transfer.Number = await db.NextNumberAsync(RetailPlusDbContext.Sequences.TransferNumbers);
        db.StockTransfers.Add(transfer);
        await db.SaveChangesAsync();
        return transfer;
    }

    /// <summary>The transfers this store sends or receives (every transfer when the database works for no store).</summary>
    public async Task<List<TransferRow>> ListAsync(bool openOnly = false, int take = 500)
    {
        await using var db = data.Create();
        var q = db.StockTransfers.AsNoTracking().AsQueryable();
        Guid? store = data.Station?.StoreId;
        if (store is Guid s) q = q.Where(StockTransfer.OfStore(s));
        if (openOnly) q = q.Where(t => t.Status == StockTransferStatus.Draft || t.Status == StockTransferStatus.Shipped);
        var rows = await q.OrderByDescending(t => t.CreatedAt).Take(take)
            .Select(t => new
            {
                t.Id, t.Number, From = t.FromLocation!.Code, To = t.ToLocation!.Code, t.Status, t.CreatedAt, t.ShippedAt, t.ReceivedAt, t.ToStoreId, t.StoreId,
                Lines = t.Lines.Count, t.Note,
            }).ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        var quantities = (await db.StockTransferLines.AsNoTracking().Where(l => ids.Contains(l.StockTransferId))
                .Select(l => new { l.StockTransferId, l.QuantityShipped, l.QuantityReceived }).ToListAsync())
            .GroupBy(l => l.StockTransferId).ToDictionary(g => g.Key, g => (Shipped: g.Sum(l => l.QuantityShipped), Received: g.Sum(l => l.QuantityReceived)));
        var codes = await db.Stores.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code);
        string Place(Guid storeId, string location) => storeId == store || codes.Count < 2 ? location : $"{codes.GetValueOrDefault(storeId)} · {location}";
        return rows.Select(r =>
        {
            var q2 = quantities.GetValueOrDefault(r.Id);
            return new TransferRow(r.Id, r.Number, Place(r.StoreId, r.From), Place(r.ToStoreId, r.To), r.Status, r.CreatedAt, r.ShippedAt, r.ReceivedAt, r.Lines,
                q2.Shipped, q2.Received, store is Guid here && r.ToStoreId == here && r.StoreId != here, r.Note);
        }).ToList();
    }

    public async Task<StockTransfer?> GetAsync(Guid id)
    {
        await using var db = data.Create();
        return await db.StockTransfers.AsNoTracking().Include(t => t.FromLocation).Include(t => t.ToLocation)
            .Include(t => t.Lines.OrderBy(l => l.LineNumber)).SingleOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>Adds an item of the sending location by code, or more of it when it is on the transfer already.</summary>
    public async Task<StockTransferLine> AddItemAsync(Guid transferId, string code, decimal quantity)
    {
        if (quantity <= 0) throw new BusinessRuleException("Enter a quantity above zero.");
        await using var db = data.Create();
        var transfer = await DraftAsync(db, transferId);
        var (item, _) = await new InventoryService(data).FindByCodeAsync(transfer.FromLocationId, code);
        if (item is null) throw new BusinessRuleException($"No item has the code {code.Trim()} at the sending location.");
        if (!SalesService.IsStocked(item.ItemType)) throw new BusinessRuleException($"{item.Name} has no stock count, so it cannot be transferred.");
        var line = await db.StockTransferLines.FirstOrDefaultAsync(l => l.StockTransferId == transferId && l.SourceItemId == item.Id);
        if (line is null)
        {
            int last = await db.StockTransferLines.Where(l => l.StockTransferId == transferId).Select(l => (int?)l.LineNumber).MaxAsync() ?? 0;
            line = new StockTransferLine
            {
                StockTransferId = transferId, LineNumber = last + 1, SourceItemId = item.Id, Plu = item.Plu, Description = item.Name, Barcode = item.Barcode,
                Size = item.Size, Department = item.Department?.Name, ItemType = item.ItemType, Taxable1 = item.Taxable1, Taxable2 = item.Taxable2,
                UnitCost = item.AverageCost > 0 ? item.AverageCost : item.LastCost, Price = item.Price1,
            };
            db.StockTransferLines.Add(line);
        }
        line.QuantityShipped += quantity;
        await db.SaveChangesAsync();
        return line;
    }

    /// <summary>Sets a line's quantity: what is sent on a draft, what arrived on a shipped transfer (before Receive).</summary>
    public async Task SetQuantityAsync(Guid lineId, decimal quantity)
    {
        if (quantity < 0) throw new BusinessRuleException("A quantity cannot be below zero.");
        await using var db = data.Create();
        var line = await db.StockTransferLines.SingleAsync(l => l.Id == lineId);
        var transfer = await db.StockTransfers.SingleAsync(t => t.Id == line.StockTransferId);
        if (transfer.Status == StockTransferStatus.Draft) line.QuantityShipped = quantity;
        else throw new BusinessRuleException($"Transfer {transfer.Number} is {transfer.Status.ToString().ToLowerInvariant()}; enter what arrived when you receive it.");
        await db.SaveChangesAsync();
    }

    public async Task RemoveLineAsync(Guid lineId)
    {
        await using var db = data.Create();
        var line = await db.StockTransferLines.SingleAsync(l => l.Id == lineId);
        await DraftAsync(db, line.StockTransferId);
        db.StockTransferLines.Remove(line);
        await db.SaveChangesAsync();
    }

    /// <summary>Ships the transfer: each line's quantity leaves the sending location's stock (a TransferOut movement).</summary>
    public async Task ShipAsync(Guid transferId, string? user)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var transfer = await DraftAsync(db, transferId);
        var lines = await db.StockTransferLines.Where(l => l.StockTransferId == transferId && l.QuantityShipped > 0).ToListAsync();
        if (lines.Count == 0) throw new BusinessRuleException("Add the items to send first.");
        var now = DateTime.Now;
        foreach (var line in lines)
        {
            decimal qty = line.QuantityShipped;
            if (await db.Items.Where(i => i.Id == line.SourceItemId && i.LocationId == transfer.FromLocationId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, i => i.OnHand - qty).SetProperty(i => i.UpdatedAtUtc, DateTime.UtcNow)) == 0)
                throw new BusinessRuleException($"{line.Plu} {line.Description} is no longer at the sending location.");
            var onHand = await db.Items.Where(i => i.Id == line.SourceItemId).Select(i => i.OnHand).SingleAsync();
            var movement = InventoryService.Record(db, line.SourceItemId, StockMovementKind.TransferOut, -qty, onHand, now, transfer.Number,
                $"Transfer {transfer.Number} to {await LocationTextAsync(db, transfer.ToLocationId)}", Trim(user, 50), userId: db.ActingUserId, unitCost: line.UnitCost);
            movement.StoreId = transfer.StoreId;
        }
        // Empty lines are not sent.
        db.StockTransferLines.RemoveRange(await db.StockTransferLines.Where(l => l.StockTransferId == transferId && l.QuantityShipped <= 0).ToListAsync());
        transfer.Status = StockTransferStatus.Shipped;
        transfer.ShippedAt = now;
        transfer.ShippedBy = user;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Receives a shipped transfer into the receiving location: each line's quantity received (all that was shipped
    /// unless <paramref name="received"/> says otherwise, by line id) goes into the location's item with the same stock
    /// code, which is created from the sending item's details when the location does not have it. Short and over
    /// receipts are kept on the lines.
    /// </summary>
    public async Task<TransferReceipt> ReceiveAsync(Guid transferId, IReadOnlyDictionary<Guid, decimal>? received, string? user)
    {
        await using var db = data.Create();
        await using var tx = await db.Database.BeginTransactionAsync();
        var transfer = await db.StockTransfers.SingleOrDefaultAsync(t => t.Id == transferId) ?? throw new BusinessRuleException("That transfer no longer exists.");
        if (transfer.Status != StockTransferStatus.Shipped)
            throw new BusinessRuleException(transfer.Status == StockTransferStatus.Draft
                ? $"Transfer {transfer.Number} has not been shipped yet."
                : $"Transfer {transfer.Number} is {transfer.Status.ToString().ToLowerInvariant()}.");
        var destination = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == transfer.ToLocationId)
                          ?? throw new BusinessRuleException("This database does not have the receiving location yet.");
        var lines = await db.StockTransferLines.Where(l => l.StockTransferId == transferId).OrderBy(l => l.LineNumber).ToListAsync();
        var now = DateTime.Now;
        var created = new List<string>();
        var shortLines = new List<string>();
        var overLines = new List<string>();
        string from = await LocationTextAsync(db, transfer.FromLocationId);
        foreach (var line in lines)
        {
            decimal qty = received is not null && received.TryGetValue(line.Id, out var q) ? q : line.QuantityShipped;
            if (qty < 0) throw new BusinessRuleException($"{line.Plu}: a quantity received cannot be below zero.");
            var item = await db.Items.FirstOrDefaultAsync(i => i.LocationId == destination.Id && i.Plu == line.Plu && !i.IsDeleted);
            if (item is null)
            {
                item = await NewItemAsync(db, line, destination.Id);
                db.Items.Add(item);
                await db.SaveChangesAsync();
                line.CreatedAtDestination = true;
                created.Add($"{item.Plu} {item.Name}");
            }
            if (qty > 0)
            {
                item.AverageCost = Reordering.AverageCostAfterReceipt(item.OnHand, item.AverageCost, qty, line.UnitCost, 0);
                if (item.LastCost == 0) item.LastCost = line.UnitCost;
                item.OnHand += qty;
                item.LastReceivedDate = DateOnly.FromDateTime(now);
                item.LastReceivedQuantity = qty;
                var movement = InventoryService.Record(db, item.Id, StockMovementKind.TransferIn, qty, item.OnHand, now, transfer.Number,
                    $"Transfer {transfer.Number} from {from}", Trim(user, 50), userId: db.ActingUserId, unitCost: line.UnitCost);
                movement.StoreId = destination.StoreId;
            }
            line.QuantityReceived = qty;
            line.DestinationItemId = item.Id;
            if (qty < line.QuantityShipped) shortLines.Add($"{line.Plu} {line.Description}: {qty:0.###} of {line.QuantityShipped:0.###}");
            else if (qty > line.QuantityShipped) overLines.Add($"{line.Plu} {line.Description}: {qty:0.###} for {line.QuantityShipped:0.###}");
        }
        transfer.Status = StockTransferStatus.Received;
        transfer.ReceivedAt = now;
        transfer.ReceivedBy = user;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new TransferReceipt(lines.Sum(l => l.QuantityReceived), created, shortLines, overLines);
    }

    /// <summary>Cancels a transfer that was not shipped; nothing moved, so stock is not touched.</summary>
    public async Task CancelAsync(Guid transferId)
    {
        await using var db = data.Create();
        var transfer = await DraftAsync(db, transferId);
        transfer.Status = StockTransferStatus.Cancelled;
        transfer.CancelledAt = DateTime.Now;
        await db.SaveChangesAsync();
    }

    private static async Task<StockTransfer> DraftAsync(RetailPlusDbContext db, Guid transferId)
    {
        var transfer = await db.StockTransfers.SingleOrDefaultAsync(t => t.Id == transferId) ?? throw new BusinessRuleException("That transfer no longer exists.");
        if (transfer.Status != StockTransferStatus.Draft)
            throw new BusinessRuleException($"Transfer {transfer.Number} is {transfer.Status.ToString().ToLowerInvariant()}; it cannot be changed.");
        return transfer;
    }

    private static async Task<string> LocationTextAsync(RetailPlusDbContext db, Guid locationId) =>
        await db.Locations.Where(l => l.Id == locationId).Join(db.Stores, l => l.StoreId, s => s.Id, (l, s) => s.Code + " " + l.Code).SingleOrDefaultAsync() ?? "another location";

    /// <summary>
    /// The receiving location's copy of the item: all details of the sending item when this database has it, otherwise
    /// what the transfer line carries. Stock and orders start at zero; a barcode already used at the location is left off.
    /// </summary>
    private static async Task<Item> NewItemAsync(RetailPlusDbContext db, StockTransferLine line, Guid locationId)
    {
        var source = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == line.SourceItemId);
        Item item;
        if (source is not null)
        {
            item = source;
            item.Id = Guid.Empty;
            item.CreatedAtUtc = default;
            item.Version = Guid.Empty;
            item.LocationId = locationId;
            item.OnHand = 0;
            item.OnOrder = 0;
            item.CustomerOrdered = 0;
            item.OnHandBeforeYearEnd = 0;
            item.LastReceivedQuantity = 0;
            item.IsFlagged = false;
        }
        else
        {
            Guid? department = null;
            if (!string.IsNullOrWhiteSpace(line.Department))
            {
                var name = line.Department.Trim().ToUpperInvariant();
                department = await db.Departments.Where(d => d.Name == name).Select(d => (Guid?)d.Id).FirstOrDefaultAsync();
                if (department is null)
                {
                    var d = new Department { Name = name };
                    db.Departments.Add(d);
                    department = d.Id;
                }
            }
            item = new Item
            {
                LocationId = locationId, Plu = line.Plu, Name = Trim(line.Description, 30)!, Barcode = line.Barcode, Size = line.Size, ItemType = line.ItemType,
                Taxable1 = line.Taxable1, Taxable2 = line.Taxable2, LastCost = line.UnitCost, AverageCost = line.UnitCost, Price1 = line.Price,
                DepartmentId = department, MarginPercent = line.Price > 0 ? Money.MarginPercent(line.Price, line.UnitCost) : 0,
            };
        }
        if (item.Barcode is not null)
        {
            var forms = Upc.Forms(item.Barcode);
            if (await db.Items.AnyAsync(i => i.LocationId == locationId && !i.IsDeleted && i.Barcode != null && forms.Contains(i.Barcode))) item.Barcode = null;
        }
        return item;
    }

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
