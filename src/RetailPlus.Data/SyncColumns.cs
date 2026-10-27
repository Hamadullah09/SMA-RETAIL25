using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using RetailPlus.Core.Model;

namespace RetailPlus.Data;

/// <summary>
/// What the DbContext keeps for the synchronisation between tills and the server: the change columns every row has, and
/// the columns the system keeps (stock counts, balances, flags) as opposed to the ones people edit.
/// </summary>
public static class SyncColumns
{
    /// <summary>
    /// Shadow column on every row: when a column that people edit last changed (UTC). <see cref="ITracked.UpdatedAtUtc"/>
    /// also moves when only a column the system keeps changes (a sale lowers the stock), so the two together decide which
    /// copy of a row is newer: the later edit wins, and between equal edits the later change.
    /// </summary>
    public const string EditedAtUtc = "EditedAtUtc";

    /// <summary>
    /// Shadow column on every row of the server's database (SQL Server only): a <c>rowversion</c>, which SQL Server raises
    /// on every insert and update whatever made it, so the server can hand a till everything changed since its last
    /// download.
    /// </summary>
    public const string ChangeNumber = "ChangeNumber";

    /// <summary>
    /// Columns only the system changes once the row exists: counts derived from other rows, per-database state, and the
    /// browse flags (each till keeps its own). A copy of a row arriving from another database never overwrites them, and
    /// changing only them is not an edit. New tables add their own here.
    /// </summary>
    private static readonly Dictionary<Type, string[]> Kept = new()
    {
        [typeof(Item)] =
        [
            nameof(Item.OnHand), nameof(Item.OnOrder), nameof(Item.CustomerOrdered), nameof(Item.OnHandBeforeYearEnd),
            nameof(Item.LastOrderDate), nameof(Item.LastReceivedDate), nameof(Item.LastReceivedQuantity), nameof(Item.LastUpdated),
        ],
        [typeof(ItemVariant)] = [nameof(ItemVariant.OnHand)],
        [typeof(Client)] = [nameof(Client.Balance), nameof(Client.Points), nameof(Client.LastPurchase)],
        [typeof(UserAccount)] = [nameof(UserAccount.FailedSignIns), nameof(UserAccount.LockedUntilUtc)],
    };

    /// <summary>True when <paramref name="property"/> of <paramref name="entityType"/> is kept by the system.</summary>
    public static bool IsKept(Type entityType, string property)
    {
        if (property == nameof(IFlaggable.IsFlagged) && typeof(IFlaggable).IsAssignableFrom(entityType)) return true;
        for (var type = entityType; type is not null && type != typeof(object); type = type.BaseType)
            if (Kept.TryGetValue(type, out var names) && Array.IndexOf(names, property) >= 0) return true;
        return false;
    }

    /// <summary>The change columns themselves, which are never compared or copied as data.</summary>
    public static bool IsBookkeeping(string property) =>
        property is nameof(ITracked.CreatedAtUtc) or nameof(ITracked.UpdatedAtUtc) or EditedAtUtc or ChangeNumber or nameof(IVersioned.Version);

    /// <summary>The time the DbContext stamps rows with: UTC, to the millisecond, so both engines store the same value.</summary>
    public static DateTime Now()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    /// <summary>
    /// For an <c>ExecuteUpdate</c> of columns the system keeps (stock, balances, flags): the row counts as changed, so it
    /// travels with the next package, without beating an edit made elsewhere.
    /// </summary>
    public static UpdateSettersBuilder<T> Touched<T>(this UpdateSettersBuilder<T> setters) where T : class, ITracked
    {
        ArgumentNullException.ThrowIfNull(setters);
        return setters.SetProperty(e => e.UpdatedAtUtc, Now());
    }

    /// <summary>For an <c>ExecuteUpdate</c> of columns people edit (deleting, a barcode, a department): the row is edited now.</summary>
    public static UpdateSettersBuilder<T> Edited<T>(this UpdateSettersBuilder<T> setters) where T : class, ITracked
    {
        ArgumentNullException.ThrowIfNull(setters);
        var now = Now();
        return setters.SetProperty(e => e.UpdatedAtUtc, now).SetProperty(e => EF.Property<DateTime>(e, EditedAtUtc), now);
    }
}
