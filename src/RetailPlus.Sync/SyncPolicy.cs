using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data;
using RetailPlus.Data.Import;

namespace RetailPlus.Sync;

/// <summary>Which way a table's rows travel.</summary>
[Flags]
public enum SyncFlow
{
    None = 0,
    /// <summary>From the tills to the server.</summary>
    Up = 1,
    /// <summary>From the server to the tills.</summary>
    Down = 2,
    Both = Up | Down,
}

/// <summary>The till a package is for or from: its store and station.</summary>
public sealed record TillScope(Guid StoreId, int StationNumber);

/// <summary>
/// A row of a till's package that a table's upload rule looks at before the server takes it: the values as they
/// arrived (the rule may change them) and the server's copy, when it has one.
/// </summary>
public sealed class UploadCheck
{
    public required RetailPlusDbContext Db { get; init; }
    public required TillScope Till { get; init; }
    public required IDictionary<string, object?> Values { get; init; }
    public object? Existing { get; init; }
    /// <summary>Add a line here to tell the till's staff what was changed or not taken, and why.</summary>
    public required IList<string> Notes { get; init; }
}

/// <summary>
/// Every rule of the synchronisation in one place. Tables are found in the EF model, so a table added later
/// synchronises without being listed: rows of a store (<see cref="IStoreScoped"/>, which includes rows stamped with
/// their station) go up from the tills and down to the tills of that store; rows that belong to a row of a store (an
/// item's matrix lines, a sale's lines) follow that row; rows without a store (clients, departments) go everywhere.
/// Only the exceptions are listed here.
/// </summary>
public sealed class SyncPolicy
{
    private readonly Dictionary<Type, SyncFlow> _flows = [];
    private readonly HashSet<Type> _excluded = [];
    private readonly Dictionary<Type, Func<TillScope, LambdaExpression>> _receives = [];
    private readonly Dictionary<Type, List<MergeKey>> _merges = [];
    private readonly Dictionary<Type, Func<UploadCheck, Task<bool>>> _uploadRules = [];
    private readonly Dictionary<Type, HashSet<string>> _excludedColumns = [];
    private readonly List<Func<IDerivedRule>> _derived = [];
    private readonly HashSet<Type> _everywhere = [];
    private readonly HashSet<Type> _downEverywhere = [];

    /// <summary>The rules of SMA RETAIL.</summary>
    public static SyncPolicy Default { get; } = CreateDefault();

    private static SyncPolicy CreateDefault()
    {
        var policy = new SyncPolicy();

        // Never sent: the importer's trace of Retail Plus records (rows marked ILocalOnly are never sent either).
        policy.Exclude<ImportBatch>();
        policy.Exclude<ImportSourceRow>();

        // Made on the server only. A till receives its own store and the stations of its store; the store and station
        // it was seeded with are replaced by the server's when it joins.
        policy.Table<Store>(SyncFlow.Down, till => s => s.Id == till.StoreId);
        policy.Table<Station>(SyncFlow.Down);
        // Every till also has the other stores and their locations (not their items): transfers, gift cards and loyalty
        // transactions of other stores point at them. A till still only sends its own store's locations.
        policy.DownEverywhere<Store>();
        policy.DownEverywhere<Location>();
        // What each role may do is set for every store at once, on the server.
        policy.Table<Role>(SyncFlow.Down);
        policy.Table<RoleGrant>(SyncFlow.Down);
        // A till receives its store's users and the users of every store (super administrators), with their password
        // and PIN hashes, so staff can sign in without a connection.
        policy.Table<UserAccount>(SyncFlow.Both, till => u => u.StoreId == null || u.StoreId == till.StoreId);
        policy.UploadRule<UserAccount>(UserFromTillAsync);
        // A till's document number counters go up, and come back only to that till (after it is set up again).
        policy.Table<NumberCounter>(SyncFlow.Both, till => c => c.StationNumber == till.StationNumber);
        // Commission rules are set for every store on the server, like roles.
        policy.Table<CommissionRule>(SyncFlow.Down);
        // A transfer (and its lines, which follow it) reaches the tills of both the sending and the receiving store.
        policy.Table<StockTransfer>(SyncFlow.Both, till => StockTransfer.OfStore(till.StoreId));
        // A gift card can be spent in any store, and a client's points earned anywhere: cards, their transactions and
        // loyalty transactions go to every till. (Audit lines, time clock entries and stock counts keep the default: up to
        // the server, down to their own store's tills.)
        policy.Everywhere<GiftCard>(SyncFlow.Both);
        policy.Everywhere<GiftCardTransaction>(SyncFlow.Both);
        policy.Everywhere<LoyaltyTransaction>(SyncFlow.Both);
        // HR and payroll tables stay on the server (ILocalOnly); so do the audit lines about them, which would otherwise go
        // down to the tills of the store they were written for. So do the lines about ledger entries, which also stay on the
        // server and in which payroll names employees with their pay and loans. A till's own audit lines go up as before.
        List<string> hr = [.. HrTables.Names, nameof(JournalEntry)];
        policy.Table<AuditEntry>(SyncFlow.Both, till => a => a.StoreId == till.StoreId && (a.EntityName == null || !hr.Contains(a.EntityName)));

        // The same code made on two databases before they synchronised is one thing: the server's row stays, the other
        // is merged into it (rows pointing at it are moved over) and the merge is noted in the batch. Any other clash of
        // unique values refuses the package.
        policy.MergeOn<Store>(nameof(Store.Code));
        policy.MergeOn<Location>(nameof(Location.StoreId), nameof(Location.Code));
        policy.MergeOn<Station>(nameof(Station.Number));
        policy.MergeOn<StoreSettings>(nameof(StoreSettings.StoreId));
        policy.MergeOn<StoreSettingExtra>(nameof(StoreSettingExtra.StoreId), nameof(StoreSettingExtra.Name));
        policy.MergeOn<StationSetting>(nameof(StationSetting.StationId), nameof(StationSetting.Section), nameof(StationSetting.Key));
        policy.MergeOn<DocumentText>(nameof(DocumentText.StoreId), nameof(DocumentText.Kind));
        policy.MergeOn<DocumentText>(nameof(DocumentText.Kind), nameof(DocumentText.LocationId));
        policy.MergeOn<Department>(nameof(Department.Name));
        policy.MergeOn<Category>(nameof(Category.Name));
        policy.MergeOn<TenderType>(nameof(TenderType.Name));
        policy.MergeOn<FxRate>(nameof(FxRate.Currency));
        policy.MergeOn<FastKey>(nameof(FastKey.Panel), nameof(FastKey.KeyNumber));
        policy.MergeOn<Role>(nameof(Role.Name));
        policy.MergeOn<RoleGrant>(nameof(RoleGrant.RoleId), nameof(RoleGrant.Permission));
        policy.MergeOn<RfidReader>(nameof(RfidReader.StoreId), nameof(RfidReader.Name));
        policy.MergeOn<RfidAntenna>(nameof(RfidAntenna.ReaderId), nameof(RfidAntenna.Port));
        policy.MergeOn<Item>(nameof(Item.LocationId), nameof(Item.Plu));
        policy.MergeOn<ItemSupplier>(nameof(ItemSupplier.ItemId), nameof(ItemSupplier.Rank));
        policy.MergeOn<ItemMatrixColumn>(nameof(ItemMatrixColumn.ItemId), nameof(ItemMatrixColumn.Ordinal));
        policy.MergeOn<ItemVariant>(nameof(ItemVariant.ItemId), nameof(ItemVariant.Ordinal));
        policy.MergeOn<KitComponent>(nameof(KitComponent.KitItemId), nameof(KitComponent.Ordinal));
        policy.MergeOn<ItemTag>(nameof(ItemTag.Epc));
        // A till set up again under a number the server already counted for keeps counting from the server's numbers.
        policy.MergeOn<NumberCounter>(nameof(NumberCounter.Kind), nameof(NumberCounter.StationNumber));
        // One card number is one card: transactions made against two copies of it end up on one card.
        policy.MergeOn<GiftCard>(nameof(GiftCard.Number));
        policy.MergeOn<AccountMapping>(nameof(AccountMapping.StoreId), nameof(AccountMapping.Kind), nameof(AccountMapping.Key));
        policy.MergeOn<StoreImage>(nameof(StoreImage.StoreId), nameof(StoreImage.Kind));
        // One user name is one person, but only within a store: a name taken by another store's user refuses the package.
        policy.MergeOn<UserAccount>([nameof(UserAccount.UserName)],
            (_, existing, till) => till is null || ((UserAccount)existing).StoreId == till.StoreId);

        // Columns the system keeps (SyncColumns) are never taken from a package for a row already here; these are worked
        // out again from the rows they come from. The others (on order, flags, sign-in failures) stay each database's own.
        policy.Derive(() => new StockFromMovements());
        policy.Derive(() => new BalanceFromInvoices());
        policy.Derive(() => new PointsFromLoyalty());
        // Two tills offline can each punch the same user in; when the entries meet, the earlier shift is closed.
        policy.Derive(() => new OpenShiftsClosed());
        return policy;
    }

    // Declaring rules -----------------------------------------------------------------------------------------------

    /// <summary>Rows of <typeparamref name="T"/> never travel.</summary>
    public void Exclude<T>() => _excluded.Add(typeof(T));

    /// <summary>
    /// How rows of <typeparamref name="T"/> travel, and optionally which ones a till receives (instead of the rows of its
    /// store). For example a transfer between stores would reach both:
    /// <c>Table&lt;StockTransfer&gt;(SyncFlow.Both, till =&gt; t =&gt; t.FromStoreId == till.StoreId || t.ToStoreId == till.StoreId)</c>.
    /// </summary>
    public void Table<T>(SyncFlow flow, Func<TillScope, Expression<Func<T, bool>>>? receives = null)
    {
        _flows[typeof(T)] = flow;
        if (receives is not null) _receives[typeof(T)] = receives;
    }

    /// <summary>Rows of <typeparamref name="T"/> go to every till and may come from any till, whatever store they belong to.</summary>
    public void Everywhere<T>(SyncFlow flow)
    {
        _flows[typeof(T)] = flow;
        _everywhere.Add(typeof(T));
    }

    /// <summary>
    /// Every till receives all rows of <typeparamref name="T"/>; a till still sends only its own store's, and rows that
    /// belong to these rows still follow the store (a location's items).
    /// </summary>
    public void DownEverywhere<T>() => _downEverywhere.Add(typeof(T));

    /// <summary>
    /// Two rows of <typeparamref name="T"/> with the same values in these columns (a unique index) are merged instead of
    /// refusing the package; <paramref name="when"/> can limit it (incoming row, existing row, the till concerned).
    /// </summary>
    public void MergeOn<T>(string[] columns, Func<object, object, TillScope?, bool>? when = null) =>
        (_merges.TryGetValue(typeof(T), out var list) ? list : _merges[typeof(T)] = []).Add(new MergeKey(columns, when));

    /// <inheritdoc cref="MergeOn{T}(string[], Func{object, object, TillScope?, bool}?)"/>
    public void MergeOn<T>(params string[] columns) => MergeOn<T>(columns, null);

    /// <summary>
    /// A check on rows of <typeparamref name="T"/> that a till sends; it returns false to leave the row out (with a note).
    /// </summary>
    public void UploadRule<T>(Func<UploadCheck, Task<bool>> rule) => _uploadRules[typeof(T)] = rule;

    /// <summary>A column of <typeparamref name="T"/> that never travels (a secret, or something only one database means).</summary>
    public void ExcludeColumn<T>(string column) =>
        (_excludedColumns.TryGetValue(typeof(T), out var set) ? set : _excludedColumns[typeof(T)] = []).Add(column);

    /// <summary>A column the system keeps that is worked out again after every package (a new rule per package).</summary>
    public void Derive(Func<IDerivedRule> rule) => _derived.Add(rule);

    // Reading rules -------------------------------------------------------------------------------------------------

    /// <summary>True when rows of the type travel at all.</summary>
    public bool IsSynced(Type type) => !typeof(ILocalOnly).IsAssignableFrom(type) && !_excluded.Contains(type) && FlowOf(type) != SyncFlow.None;

    public SyncFlow FlowOf(Type type) => _flows.TryGetValue(type, out var flow) ? flow : SyncFlow.Both;

    /// <summary>True when every till receives every row of the type (see <see cref="Everywhere{T}"/>).</summary>
    public bool IsEverywhere(Type type) => _everywhere.Contains(type);

    /// <summary>True when every till receives every row of the type, everywhere or only downwards.</summary>
    public bool IsDownEverywhere(Type type) => _everywhere.Contains(type) || _downEverywhere.Contains(type);

    /// <summary>The rows of the type a till receives when they are not simply its store's; null for the default.</summary>
    public LambdaExpression? Receives(Type type, TillScope till) => _receives.TryGetValue(type, out var make) ? make(till) : null;

    /// <summary>True when a clash on these unique columns merges the rows instead of refusing the package.</summary>
    public bool MayMerge(Type type, IReadOnlyList<string> columns, object incoming, object existing, TillScope? till) =>
        _merges.TryGetValue(type, out var keys)
        && keys.Any(k => k.Columns.SequenceEqual(columns) && (k.When is null || k.When(incoming, existing, till)));

    public Func<UploadCheck, Task<bool>>? UploadRuleOf(Type type) => _uploadRules.GetValueOrDefault(type);

    /// <summary>Fresh derived-value rules for one package.</summary>
    public IReadOnlyList<IDerivedRule> NewDerivedRules() => [.. _derived.Select(make => make())];

    /// <summary>Columns that never travel: the server's change number, and any listed with <see cref="ExcludeColumn{T}"/>.</summary>
    public bool IsExcludedColumn(Type type, string column) =>
        column == SyncColumns.ChangeNumber || (_excludedColumns.TryGetValue(type, out var set) && set.Contains(column));

    private sealed record MergeKey(string[] Columns, Func<object, object, TillScope?, bool>? When);

    // The rules themselves ------------------------------------------------------------------------------------------

    /// <summary>
    /// Users sent by a till: a till looks after its own store's users only. A new user belongs to the till's store; a
    /// user of another store or of every store is not changed from a till; and a till cannot give anyone the super
    /// administrator's reach over every store.
    /// </summary>
    private static async Task<bool> UserFromTillAsync(UploadCheck check)
    {
        var name = check.Values[nameof(UserAccount.UserName)];
        if (check.Existing is UserAccount existing)
        {
            if (existing.StoreId != check.Till.StoreId)
            {
                check.Notes.Add($"User {name} belongs to {(existing.StoreId is null ? "every store" : "another store")}; changes from a till are not taken.");
                return false;
            }
        }
        check.Values[nameof(UserAccount.StoreId)] = check.Till.StoreId;
        if (check.Values[nameof(UserAccount.RoleId)] is Guid roleId)
        {
            var grants = await check.Db.RoleGrants.AsNoTracking().Where(g => g.RoleId == roleId).Select(g => g.Permission).ToListAsync();
            bool known = await check.Db.Roles.AnyAsync(r => r.Id == roleId);
            if (!known || grants.Contains(Permissions.StoresAll))
            {
                var level = Convert.ToInt32(check.Values[nameof(UserAccount.AccessLevel)] ?? 1, System.Globalization.CultureInfo.InvariantCulture);
                var fallback = check.Existing is UserAccount { RoleId: { } kept } ? kept
                    : await check.Db.Roles.Where(r => r.Name == Permissions.DefaultRoleForLevel(level)).Select(r => (Guid?)r.Id).FirstOrDefaultAsync();
                check.Values[nameof(UserAccount.RoleId)] = fallback;
                check.Notes.Add($"User {name}: {(known ? "a till cannot give a role for every store" : "the role is not on the server")}; the user keeps {(check.Existing is null ? "the standard role of the level" : "the role they had")}.");
            }
        }
        return true;
    }
}
