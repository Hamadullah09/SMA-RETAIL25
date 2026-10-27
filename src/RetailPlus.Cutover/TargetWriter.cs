using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data;

namespace RetailPlus.Cutover;

/// <summary>
/// Writes the mapped rows into the new database, all on one connection and in one transaction (so a failed or dry run
/// leaves the database as it was). Rows are matched by id: a row that is not there yet is added, a row that is there is
/// updated with the values that differ, and a row whose values are all the same is left alone, so a second run over
/// unchanged data changes nothing. Contexts hold the automatic audit lines back; the run writes one line of its own.
/// </summary>
internal sealed class TargetWriter(DbConnection connection, DbTransaction transaction, DatabaseProvider provider, CutoverReport report,
    IReadOnlyDictionary<object, string> where)
{
    private const int ChunkSize = 500;

    /// <summary>A context on the run's connection, enlisted in its transaction.</summary>
    public RetailPlusDbContext NewContext(StationIdentity? station = null)
    {
        var builder = RetailPlusDb.Configure(new DbContextOptionsBuilder<RetailPlusDbContext>(), connection, provider, 600);
        var db = new RetailPlusDbContext(builder.Options) { Station = station, SuppressAudit = true };
        db.Database.UseTransaction(transaction);
        return db;
    }

    /// <summary>
    /// Adds or updates <paramref name="rows"/> (each with its final id). For a row already in the database,
    /// <paramref name="merge"/> may first copy into the mapped row what the new database owns (a password set there), and
    /// the times the row was made and saved stay as they are unless a value really changed.
    /// </summary>
    public async Task UpsertAsync<T>(AreaResult area, IReadOnlyList<T> rows, CancellationToken ct, Action<T, T>? merge = null) where T : class
    {
        if (rows.Count == 0) return;
        await using (var model = NewContext()) Fit(model, rows);
        for (int start = 0; start < rows.Count; start += ChunkSize)
        {
            var part = rows.Skip(start).Take(ChunkSize).ToList();
            await using var db = NewContext();
            var key = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties.Single();
            var getter = key.PropertyInfo!;
            var ids = part.Select(r => (Guid)getter.GetValue(r)!).ToList();
            var existing = (await db.Set<T>().Where(e => ids.Contains(EF.Property<Guid>(e, key.Name))).ToListAsync(ct))
                .ToDictionary(e => (Guid)getter.GetValue(e)!);
            foreach (var row in part)
            {
                if (!existing.TryGetValue((Guid)getter.GetValue(row)!, out var stored))
                {
                    db.Add(row);
                    area.Added++;
                    continue;
                }
                merge?.Invoke(stored, row);
                if (row is ITracked mapped && stored is ITracked kept)
                {
                    mapped.UpdatedAtUtc = kept.UpdatedAtUtc;
                    if (mapped.CreatedAtUtc == default) mapped.CreatedAtUtc = kept.CreatedAtUtc;
                }
                if (row is IVersioned versioned && stored is IVersioned storedVersion) versioned.Version = storedVersion.Version;
                var entry = db.Entry(stored);
                entry.CurrentValues.SetValues(row);
                if (entry.State == EntityState.Modified) area.Changed++;
                else area.Unchanged++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Removes the rows <paramref name="scope"/> selects that are not in <paramref name="keep"/>: children of carried rows
    /// that Retail25 no longer has (a supplier taken off an item, a kit component removed). Only rows the cutover made
    /// (with a <see cref="StableIds"/> id, version 8) are ever removed; rows the program made itself are left alone.
    /// </summary>
    public async Task RemoveMissingAsync<T>(AreaResult area, Func<RetailPlusDbContext, IQueryable<T>> scope, IReadOnlySet<Guid> keep, CancellationToken ct)
        where T : Entity
    {
        List<Guid> stale;
        await using (var db = NewContext())
            stale = (await scope(db).Select(e => e.Id).ToListAsync(ct)).Where(id => id.Version == 8 && !keep.Contains(id)).ToList();
        for (int start = 0; start < stale.Count; start += ChunkSize)
        {
            var part = stale.Skip(start).Take(ChunkSize).ToList();
            await using var db = NewContext();
            var rows = await db.Set<T>().Where(e => part.Contains(e.Id)).ToListAsync(ct);
            db.RemoveRange(rows);
            await db.SaveChangesAsync(ct);
            area.Removed += rows.Count;
        }
    }

    /// <summary>Cuts text to the column sizes of the model before anything is compared or saved, noting each cut.</summary>
    private void Fit<T>(RetailPlusDbContext db, IEnumerable<T> rows) where T : class
    {
        var type = db.Model.FindEntityType(typeof(T))!;
        var limited = type.GetProperties()
            .Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is not null && p.PropertyInfo is not null)
            .Select(p => (Property: p.PropertyInfo!, Max: p.GetMaxLength()!.Value))
            .ToList();
        foreach (var row in rows)
            foreach (var (property, max) in limited)
            {
                if (property.GetValue(row) is not string text || text.Length <= max) continue;
                property.SetValue(row, text[..max].TrimEnd());
                string field = typeof(T).Name + "." + property.Name;
                report.Truncated[field] = report.Truncated.GetValueOrDefault(field) + 1;
                if (report.Truncated[field] <= 25)
                    report.Warnings.Add($"{where.GetValueOrDefault(row, typeof(T).Name)}: {property.Name} \"{text}\" cut to {max} characters");
            }
    }
}
