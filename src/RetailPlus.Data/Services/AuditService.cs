using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>What the Audit screen asks for: a range of days (store local dates) and, optionally, a user, an action and text.</summary>
public sealed record AuditFilter(DateOnly From, DateOnly To, Guid? UserId = null, string? Action = null, string? Text = null);

/// <summary>One property of an audit line, before and after the change, as text.</summary>
public sealed record AuditChange(string Property, string? Before, string? After);

/// <summary>The audit log: written by the program for events, read by the Audit screen.</summary>
public sealed class AuditService(DataContext data)
{
    /// <summary>Writes one line now, for the signed-in user.</summary>
    public async Task WriteAsync(string action, string? details = null, string? entityName = null, Guid? entityId = null)
    {
        await using var db = data.Create();
        db.Audit(action, details, entityName, entityId);
        await db.SaveChangesAsync();
    }

    /// <summary>The lines that match, newest first (at most <paramref name="take"/>).</summary>
    public async Task<List<AuditEntry>> SearchAsync(AuditFilter filter, int take = 2000)
    {
        var start = filter.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var end = filter.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        await using var db = data.Create();
        var q = db.AuditEntries.AsNoTracking().Where(a => a.AtUtc >= start && a.AtUtc < end);
        if (data.Station is { } here) q = q.Where(a => a.StoreId == here.StoreId);
        if (filter.UserId is Guid user) q = q.Where(a => a.UserId == user);
        if (!string.IsNullOrWhiteSpace(filter.Action)) q = q.Where(a => a.Action == filter.Action);
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var like = "%" + filter.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            q = q.Where(a => EF.Functions.Like(a.Details ?? "", like, "\\") || EF.Functions.Like(a.UserName ?? "", like, "\\")
                             || EF.Functions.Like(a.EntityName ?? "", like, "\\") || EF.Functions.Like(a.Before ?? "", like, "\\")
                             || EF.Functions.Like(a.After ?? "", like, "\\"));
        }
        return await q.OrderByDescending(a => a.AtUtc).Take(take).ToListAsync();
    }

    /// <summary>The actions that occur in the log, for the filter list.</summary>
    public async Task<List<string>> GetActionsAsync()
    {
        await using var db = data.Create();
        return await db.AuditEntries.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
    }

    /// <summary>The audit lines of one row (an item, a user…), newest first.</summary>
    public async Task<List<AuditEntry>> HistoryAsync(string entityName, Guid entityId)
    {
        await using var db = data.Create();
        return await db.AuditEntries.AsNoTracking().Where(a => a.EntityName == entityName && a.EntityId == entityId).OrderByDescending(a => a.AtUtc).ToListAsync();
    }

    /// <summary>The properties of a line with their values before and after, in the order they were written.</summary>
    public static List<AuditChange> Changes(AuditEntry entry)
    {
        var before = Read(entry.Before);
        var after = Read(entry.After);
        return before.Keys.Concat(after.Keys).Distinct()
            .Select(k => new AuditChange(k, before.GetValueOrDefault(k), after.GetValueOrDefault(k))).ToList();
    }

    private static Dictionary<string, string?> Read(string? json)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return values;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return values;
            foreach (var p in doc.RootElement.EnumerateObject()) values[p.Name] = Text(p.Value);
        }
        catch (JsonException)
        {
            values["(value)"] = json;
        }
        return values;
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String when value.TryGetDateTime(out var at) && value.GetString()!.Length >= 19 => at.ToString("g", CultureInfo.CurrentCulture),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number.ToString("0.############", CultureInfo.CurrentCulture),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        _ => value.GetRawText(),
    };
}
