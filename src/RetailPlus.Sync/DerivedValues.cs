using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RetailPlus.Core.Model;
using RetailPlus.Data;

namespace RetailPlus.Sync;

/// <summary>
/// A column the system keeps (see <see cref="SyncColumns"/>) that the applier works out again after a package, from the
/// rows it is derived from, instead of taking it from the package. One instance serves one package.
/// </summary>
public interface IDerivedRule
{
    /// <summary>Sees every row applied: its values, whether it is new here, and its values before (for a changed row).</summary>
    void Observe(SyncTable table, IReadOnlyDictionary<string, object?> row, bool inserted, PropertyValues? before);

    /// <summary>Puts the derived values right, inside the package's transaction.</summary>
    Task ApplyAsync(RetailPlusDbContext db, CancellationToken cancellationToken);
}

/// <summary>An item's on-hand count, and each matrix line's, is the sum of its stock movements.</summary>
public sealed class StockFromMovements : IDerivedRule
{
    private readonly HashSet<Guid> _items = [];

    public void Observe(SyncTable table, IReadOnlyDictionary<string, object?> row, bool inserted, PropertyValues? before)
    {
        if (table.ClrType == typeof(StockMovement) && row.GetValueOrDefault(nameof(StockMovement.ItemId)) is Guid moved) _items.Add(moved);
        else if (table.ClrType == typeof(Item) && row.GetValueOrDefault(nameof(Item.Id)) is Guid item) _items.Add(item);
        else if (table.ClrType == typeof(ItemVariant) && row.GetValueOrDefault(nameof(ItemVariant.ItemId)) is Guid owner) _items.Add(owner);
    }

    public async Task ApplyAsync(RetailPlusDbContext db, CancellationToken cancellationToken)
    {
        foreach (var chunk in _items.Chunk(500))
        {
            var ids = chunk.ToList();
            var sums = await db.StockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
                .Select(g => new { g.Key, Sum = g.Sum(m => m.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Sum, cancellationToken);
            foreach (var item in await db.Items.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken))
                item.OnHand = sums.GetValueOrDefault(item.Id);
            var lines = await db.StockMovements.Where(m => m.VariantId != null && ids.Contains(m.ItemId)).GroupBy(m => m.VariantId!.Value)
                .Select(g => new { g.Key, Sum = g.Sum(m => m.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Sum, cancellationToken);
            foreach (var variant in await db.ItemVariants.Where(v => ids.Contains(v.ItemId)).ToListAsync(cancellationToken))
                variant.OnHand = lines.GetValueOrDefault(variant.Id);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}

/// <summary>
/// A client's balance is the sum of the balances of their invoices (not voided, not quotes) in this database: every
/// invoice and payment changes both by the same amount. On the server that is every store's invoices; on a till, its
/// store's.
/// </summary>
public sealed class BalanceFromInvoices : IDerivedRule
{
    private readonly HashSet<Guid> _clients = [];

    public void Observe(SyncTable table, IReadOnlyDictionary<string, object?> row, bool inserted, PropertyValues? before)
    {
        if (table.ClrType == typeof(Client) && row.GetValueOrDefault(nameof(Client.Id)) is Guid client) _clients.Add(client);
        if (table.ClrType != typeof(Invoice)) return;
        if (row.GetValueOrDefault(nameof(Invoice.ClientId)) is Guid now) _clients.Add(now);
        if (before?[nameof(Invoice.ClientId)] is Guid was) _clients.Add(was);
    }

    public async Task ApplyAsync(RetailPlusDbContext db, CancellationToken cancellationToken)
    {
        foreach (var chunk in _clients.Chunk(500))
        {
            var ids = chunk.ToList();
            var sums = await db.Invoices.Where(i => i.ClientId != null && ids.Contains(i.ClientId.Value) && !i.IsVoided && i.Kind != InvoiceKind.Quote)
                .GroupBy(i => i.ClientId!.Value).Select(g => new { g.Key, Sum = g.Sum(i => i.Balance) }).ToDictionaryAsync(x => x.Key, x => x.Sum, cancellationToken);
            foreach (var client in await db.Clients.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken))
                client.Balance = sums.GetValueOrDefault(client.Id);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}

/// <summary>
/// Bonus points cannot be worked out from the sales alone (a client may bring points from Retail Plus), so each sale
/// arriving from another database does here what it did where it was rung up: a new completed sale adds the points
/// earned less those redeemed (and moves the last purchase date on), a sale arriving voided takes them back. A client
/// arriving in the same package already counts that package's sales.
/// </summary>
public sealed class PointsFromSales : IDerivedRule
{
    private readonly Dictionary<Guid, (decimal Points, DateOnly? Purchase)> _changes = [];
    private readonly HashSet<Guid> _newClients = [];

    public void Observe(SyncTable table, IReadOnlyDictionary<string, object?> row, bool inserted, PropertyValues? before)
    {
        if (table.ClrType == typeof(Client) && inserted && row.GetValueOrDefault(nameof(Client.Id)) is Guid newClient) _newClients.Add(newClient);
        if (table.ClrType != typeof(Sale) || row.GetValueOrDefault(nameof(Sale.ClientId)) is not Guid client) return;
        var status = row.GetValueOrDefault(nameof(Sale.Status)) as SaleStatus?;
        var points = (row.GetValueOrDefault(nameof(Sale.PointsEarned)) as decimal? ?? 0) - (row.GetValueOrDefault(nameof(Sale.PointsRedeemed)) as decimal? ?? 0);
        var change = _changes.GetValueOrDefault(client);
        if (inserted && status == SaleStatus.Completed)
        {
            var day = row.GetValueOrDefault(nameof(Sale.SoldAt)) is DateTime soldAt ? DateOnly.FromDateTime(soldAt) : (DateOnly?)null;
            _changes[client] = (change.Points + points, Max(change.Purchase, day));
        }
        else if (!inserted && status == SaleStatus.Voided && before?[nameof(Sale.Status)] is SaleStatus.Completed)
            _changes[client] = (change.Points - points, change.Purchase);
    }

    public async Task ApplyAsync(RetailPlusDbContext db, CancellationToken cancellationToken)
    {
        var ids = _changes.Keys.Where(c => !_newClients.Contains(c)).ToList();
        foreach (var chunk in ids.Chunk(500))
        {
            var list = chunk.ToList();
            foreach (var client in await db.Clients.Where(c => list.Contains(c.Id)).ToListAsync(cancellationToken))
            {
                var (points, purchase) = _changes[client.Id];
                client.Points += points;
                client.LastPurchase = Max(client.LastPurchase, purchase);
            }
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }

    private static DateOnly? Max(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
