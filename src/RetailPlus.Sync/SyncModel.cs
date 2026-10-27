using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RetailPlus.Core.Model;
using RetailPlus.Data;

namespace RetailPlus.Sync;

/// <summary>One table that synchronises, as the EF model and the policy describe it.</summary>
public sealed class SyncTable
{
    internal SyncTable(IEntityType entityType, IProperty key, SyncFlow flow, IReadOnlyList<IProperty> columns)
    {
        EntityType = entityType;
        Key = key;
        Flow = flow;
        Columns = columns;
    }

    public IEntityType EntityType { get; }
    public Type ClrType => EntityType.ClrType;
    /// <summary>The CLR name, used for the file in the package and in tombstones.</summary>
    public string Name => EntityType.ClrType.Name;
    public IProperty Key { get; }
    public SyncFlow Flow { get; }
    /// <summary>The columns that travel, the key first.</summary>
    public IReadOnlyList<IProperty> Columns { get; }
    /// <summary>The row has its own store column.</summary>
    public bool HasStore => typeof(IStoreScoped).IsAssignableFrom(ClrType);
    /// <summary>For rows without a store column: the required reference that leads to their store (a line's sale).</summary>
    public IForeignKey? StoreVia { get; internal set; }
    /// <summary>References to other tables that synchronise (their ids may need mapping when rows were merged).</summary>
    public IReadOnlyList<IForeignKey> References { get; internal set; } = [];
    /// <summary>References from other tables to this one.</summary>
    public IReadOnlyList<IForeignKey> ReferencedBy { get; internal set; } = [];
    public IReadOnlyList<IIndex> UniqueIndexes => [.. EntityType.GetIndexes().Where(i => i.IsUnique)];

    public override string ToString() => Name;
}

/// <summary>
/// The tables that synchronise, parents before children (by their foreign keys), and how to find the rows of one store.
/// Built from the EF model, so new tables need no code here.
/// </summary>
public sealed class SyncModel
{
    private static readonly ConcurrentDictionary<(IModel, SyncPolicy), SyncModel> Cache = new();
    private readonly Dictionary<string, SyncTable> _byName;
    private readonly Dictionary<Type, SyncTable> _byType;

    private SyncModel(IModel model, SyncPolicy policy)
    {
        Policy = policy;
        var tables = new List<SyncTable>();
        foreach (var type in model.GetEntityTypes().Where(t => !t.IsOwned() && t.GetTableName() is not null && !t.ClrType.IsAbstract))
        {
            if (!policy.IsSynced(type.ClrType) || !typeof(ITracked).IsAssignableFrom(type.ClrType)) continue;
            if (type.FindPrimaryKey() is not { Properties: [var key] } || key.ClrType != typeof(Guid)) continue;
            var columns = type.GetProperties().Where(p => !policy.IsExcludedColumn(type.ClrType, p.Name))
                .OrderBy(p => p.IsPrimaryKey() ? 0 : 1).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
            tables.Add(new SyncTable(type, key, policy.FlowOf(type.ClrType), columns));
        }
        _byType = tables.ToDictionary(t => t.ClrType);
        _byName = tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        foreach (var t in tables)
        {
            t.References = [.. t.EntityType.GetForeignKeys().Where(fk => _byType.ContainsKey(fk.PrincipalEntityType.ClrType) && fk.Properties.Count == 1)];
            t.ReferencedBy = [.. tables.SelectMany(o => o.EntityType.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType == t.ClrType && fk.Properties.Count == 1))];
        }
        var paths = new Dictionary<Type, (IForeignKey? Via, int Depth)?>();
        foreach (var t in tables) t.StoreVia = StorePath(t, paths, [])?.Via;
        Tables = Sort(tables);
    }

    public SyncPolicy Policy { get; }

    /// <summary>Every table that synchronises, parents before children.</summary>
    public IReadOnlyList<SyncTable> Tables { get; }

    public static SyncModel For(IModel model, SyncPolicy? policy = null) =>
        Cache.GetOrAdd((model, policy ?? SyncPolicy.Default), key => new SyncModel(key.Item1, key.Item2));

    public SyncTable? Find(string name) => _byName.GetValueOrDefault(name);

    public SyncTable? Find(Type type) => _byType.GetValueOrDefault(type);

    /// <summary>
    /// How a row reaches its store: depth 0 for a row with a store column, else the required reference to follow (the
    /// shortest way); null for rows of every store.
    /// </summary>
    private (IForeignKey? Via, int Depth)? StorePath(SyncTable table, Dictionary<Type, (IForeignKey? Via, int Depth)?> known, HashSet<Type> visiting)
    {
        if (table.HasStore) return (null, 0);
        if (known.TryGetValue(table.ClrType, out var done)) return done;
        if (table.ClrType == typeof(Store) || !visiting.Add(table.ClrType)) return null;
        (IForeignKey? Via, int Depth)? best = null;
        foreach (var fk in table.EntityType.GetForeignKeys().Where(fk => fk.Properties is [{ ClrType: var c }] && c == typeof(Guid)))
        {
            if (!_byType.TryGetValue(fk.PrincipalEntityType.ClrType, out var parent) || parent == table) continue;
            if (StorePath(parent, known, visiting) is { } up && (best is null || up.Depth + 1 < best.Value.Depth)) best = (fk, up.Depth + 1);
        }
        visiting.Remove(table.ClrType);
        known[table.ClrType] = best;
        return best;
    }

    /// <summary>Parents before children; tables in a cycle (none today) keep their model order.</summary>
    private static List<SyncTable> Sort(List<SyncTable> tables)
    {
        var sorted = new List<SyncTable>();
        var left = tables.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        while (left.Count > 0)
        {
            var ready = left.Where(t => t.References.All(fk => fk.PrincipalEntityType.ClrType == t.ClrType || !left.Any(o => o.ClrType == fk.PrincipalEntityType.ClrType))).ToList();
            if (ready.Count == 0) ready = [left[0]];
            sorted.AddRange(ready);
            left.RemoveAll(ready.Contains);
        }
        return sorted;
    }

    // Which rows a till receives -----------------------------------------------------------------------------------

    /// <summary>
    /// The condition on rows of <paramref name="table"/> a till receives: <see cref="StoreFilter"/>, except for tables every
    /// till receives whole (stores and locations, which transfers and gift cards of other stores point at).
    /// </summary>
    public LambdaExpression? DownloadFilter(SyncTable table, RetailPlusDbContext db, TillScope till) =>
        Policy.IsDownEverywhere(table.ClrType) ? null : StoreFilter(table, db, till);

    /// <summary>
    /// The condition on rows of <paramref name="table"/> that belong to the till's store (or that the policy gives it):
    /// what a till may send, and what rows that belong to these rows follow. Null when they belong to every store.
    /// </summary>
    public LambdaExpression? StoreFilter(SyncTable table, RetailPlusDbContext db, TillScope till)
    {
        if (Policy.IsEverywhere(table.ClrType)) return null;
        if (Policy.Receives(table.ClrType, till) is { } custom) return custom;
        var e = Expression.Parameter(table.ClrType, "e");
        if (table.HasStore)
            return Expression.Lambda(Expression.Equal(Column<Guid>(e, nameof(IStoreScoped.StoreId)), Value(till.StoreId)), e);
        if (table.StoreVia is not { } via) return null;
        var parent = _byType[via.PrincipalEntityType.ClrType];
        var parentFilter = StoreFilter(parent, db, till);
        if (parentFilter is null) return null;
        // e => db.Set<Parent>().Where(parentFilter).Select(p => p.Key).Contains(e.Reference)
        var rows = Rows(db, parent.ClrType);
        var p = Expression.Parameter(parent.ClrType, "p");
        var ids = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [parent.ClrType, typeof(Guid)],
            Expression.Call(typeof(Queryable), nameof(Queryable.Where), [parent.ClrType], rows.Expression, Expression.Quote(parentFilter)),
            Expression.Quote(Expression.Lambda(Column<Guid>(p, parent.Key.Name), p)));
        var contains = Expression.Call(typeof(Queryable), nameof(Queryable.Contains), [typeof(Guid)], ids, Column<Guid>(e, via.Properties[0].Name));
        return Expression.Lambda(contains, e);
    }

    /// <summary><c>EF.Property&lt;T&gt;(row, name)</c>.</summary>
    internal static Expression Column<T>(Expression row, string name) => Column(row, name, typeof(T));

    internal static Expression Column(Expression row, string name, Type type) =>
        Expression.Call(typeof(EF), nameof(EF.Property), [type], row, Expression.Constant(name));

    /// <summary>A value EF sends as a parameter (not written into the SQL).</summary>
    internal static Expression Value<T>(T value) => Expression.Property(Expression.Constant(new Box<T>(value)), nameof(Box<T>.Value));

    /// <summary><c>db.Set&lt;T&gt;()</c> for a type known at run time.</summary>
    internal static IQueryable Rows(DbContext db, Type type) =>
        (IQueryable)SetMethod.MakeGenericMethod(type).Invoke(db, null)!;

    private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;

    /// <summary><c>rows.Where(predicate)</c> for a type known at run time.</summary>
    internal static IQueryable Where(IQueryable rows, LambdaExpression predicate) =>
        rows.Provider.CreateQuery(Expression.Call(typeof(Queryable), nameof(Queryable.Where), [rows.ElementType], rows.Expression, Expression.Quote(predicate)));

    /// <summary>Holds a value so EF turns it into a query parameter.</summary>
    internal sealed class Box<T>(T value)
    {
        public T Value { get; } = value;
    }
}
