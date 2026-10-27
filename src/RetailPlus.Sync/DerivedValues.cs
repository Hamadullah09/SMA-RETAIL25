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
/// A client's bonus points are the sum of their loyalty transactions (earned, redeemed, corrected, brought from Retail
/// Plus), which every till and the server write for each change; loyalty transactions go to every store, so the sum is the
/// same everywhere.
/// </summary>
public sealed class PointsFromLoyalty : IDerivedRule
{
    private readonly HashSet<Guid> _clients = [];

    public void Observe(SyncTable table, IReadOnlyDictionary<string, object?> row, bool inserted, PropertyValues? before)
    {
        if (table.ClrType == typeof(LoyaltyTransaction) && row.GetValueOrDefault(nameof(LoyaltyTransaction.ClientId)) is Guid client) _clients.Add(client);
        else if (table.ClrType == typeof(Client) && row.GetValueOrDefault(nameof(Client.Id)) is Guid id) _clients.Add(id);
    }

    public async Task ApplyAsync(RetailPlusDbContext db, CancellationToken cancellationToken)
    {
        foreach (var chunk in _clients.Chunk(500))
        {
            var ids = chunk.ToList();
            var sums = await db.LoyaltyTransactions.Where(t => ids.Contains(t.ClientId)).GroupBy(t => t.ClientId)
                .Select(g => new { g.Key, Sum = g.Sum(t => t.Points) }).ToDictionaryAsync(x => x.Key, x => x.Sum, cancellationToken);
            foreach (var client in await db.Clients.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken))
                client.Points = sums.GetValueOrDefault(client.Id);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}
