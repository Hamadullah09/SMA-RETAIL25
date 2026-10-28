using System.Globalization;
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

/// <summary>
/// A user has at most one open shift, but two tills without a connection can each punch the same user in. When the
/// entries meet (on the server, or on a till that receives the other till's entry), every open shift of a user that has a
/// later shift is closed when that later one began: the user cannot still have been in. The entry says so in its edit
/// reason, and the closing is written to the audit log. The result depends on the entries only (times, then ids), so
/// every database closes the same shifts at the same times, whichever receives them first.
/// </summary>
public sealed class OpenShiftsClosed : IDerivedRule
{
    /// <summary>Who closed the shift, in the entry and in the audit log.</summary>
    public const string Editor = "Sync";

    private readonly HashSet<Guid> _users = [];

    public void Observe(SyncTable table, IReadOnlyDictionary<string, object?> row, bool inserted, PropertyValues? before)
    {
        if (table.ClrType == typeof(TimeClockEntry) && row.GetValueOrDefault(nameof(TimeClockEntry.UserId)) is Guid user) _users.Add(user);
    }

    public async Task ApplyAsync(RetailPlusDbContext db, CancellationToken cancellationToken)
    {
        foreach (var chunk in _users.Chunk(500))
        {
            var ids = chunk.ToList();
            var open = await db.TimeClockEntries.AsNoTracking().Where(t => ids.Contains(t.UserId) && t.ClockOut == null)
                .GroupBy(t => t.UserId).Select(g => new { User = g.Key, From = g.Min(t => t.ClockIn) }).ToListAsync(cancellationToken);
            foreach (var user in open)
            {
                // Ordered here, not by the database: the same order on SQLite and SQL Server.
                var shifts = (await db.TimeClockEntries.Where(t => t.UserId == user.User && t.ClockIn >= user.From).ToListAsync(cancellationToken))
                    .OrderBy(t => t.ClockIn).ThenBy(t => t.Id).ToList();
                for (int i = 0; i < shifts.Count - 1; i++)
                    if (shifts[i].ClockOut is null) Close(db, shifts[i], shifts[i + 1]);
            }
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }

    private static void Close(RetailPlusDbContext db, TimeClockEntry shift, TimeClockEntry next)
    {
        var now = SyncColumns.Now();
        var before = Times(shift);
        shift.ClockOut = next.ClockIn;
        shift.EditReason = Cut(string.Create(CultureInfo.InvariantCulture,
            $"Closed by the sync: {shift.UserName} punched in again at {next.ClockIn:yyyy-MM-dd HH:mm} at station {next.StationNumber} before this shift was punched out."), 200);
        shift.EditedBy = Editor;
        shift.EditedById = null;
        shift.EditedAtUtc = now;
        // Rows saved while a package is applied keep the times they are given: this one is edited here, now, so it travels
        // on and wins over the copies that are still open.
        shift.UpdatedAtUtc = now;
        db.Entry(shift).Property(SyncColumns.EditedAtUtc).CurrentValue = now;

        var audit = new AuditEntry
        {
            StoreId = shift.StoreId, StationNumber = db.Station?.StationNumber ?? 0, AtUtc = now, UserName = Editor, Action = AuditActions.TimeClockEdit,
            EntityName = nameof(TimeClockEntry), EntityId = shift.Id, Before = RetailPlusDbContext.AuditValues(before), After = RetailPlusDbContext.AuditValues(Times(shift)),
            Details = Cut(string.Create(CultureInfo.InvariantCulture, $"{shift.UserName}, {shift.ClockIn:yyyy-MM-dd}: {shift.EditReason}"), 400),
            CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        db.AuditEntries.Add(audit);
        db.Entry(audit).Property(SyncColumns.EditedAtUtc).CurrentValue = now;
    }

    private static Dictionary<string, object?> Times(TimeClockEntry e) => new()
    {
        [nameof(TimeClockEntry.ClockIn)] = e.ClockIn,
        [nameof(TimeClockEntry.ClockOut)] = e.ClockOut,
    };

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}
