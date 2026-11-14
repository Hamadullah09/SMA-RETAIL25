using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RetailPlus.Data.Services;

namespace RetailPlus.Data;

public partial class RetailPlusDbContext
{
    /// <summary>The text properties of each kind of row that have a column size.</summary>
    private static readonly ConcurrentDictionary<IEntityType, IProperty[]> SizedText = new();

    /// <summary>
    /// Refuses text longer than its column before anything is saved, naming the field and its size. SQL Server would
    /// refuse it with a message that names no field, and SQLite (a till) would keep it, so the server would refuse it later
    /// when the till sends it. A changed row is checked on the columns that changed. Rows from a sync package are saved
    /// as they arrived (SQL Server checks them itself), so a till never refuses what the server sent it.
    /// </summary>
    private void CheckTextLengths()
    {
        if (ApplyingSync) return;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            foreach (var property in SizedText.GetOrAdd(entry.Metadata, SizedTextOf))
            {
                var value = entry.Property(property);
                if (entry.State == EntityState.Modified && !value.IsModified) continue;
                int max = property.GetMaxLength()!.Value;
                if (value.CurrentValue is string text && TextLimits.TooLong(text, max))
                    throw new BusinessRuleException(TextLimits.TooLongMessage(property.Name, max));
            }
        }
    }

    private static IProperty[] SizedTextOf(IEntityType type) =>
        [.. type.GetProperties().Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is not null)];
}

/// <summary>Text longer than its column: when it is, and how to tell people.</summary>
public static partial class TextLimits
{
    /// <summary>
    /// True when <paramref name="text"/> does not fit in <paramref name="max"/> characters. Spaces past the end do not
    /// count, as SQL Server drops them rather than refusing the text.
    /// </summary>
    public static bool TooLong(string text, int max) => text.Length > max && text.AsSpan(max).TrimEnd(' ').Length > 0;

    /// <summary>For example "Address 1 is too long: at most 30 characters."</summary>
    public static string TooLongMessage(string property, int max) => $"{FieldName(property)} is too long: at most {max} characters.";

    /// <summary>A property's name as people read it: Address1 is "Address 1", CustomerEmail "Customer email", Plu "Stock code".</summary>
    public static string FieldName(string property)
    {
        var text = string.Join(' ', Words().Matches(property).Select(w => Known.GetValueOrDefault(w.Value) ?? w.Value.ToLowerInvariant()));
        return text.Length == 0 ? property : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>Words of property names that are not written in lower case, or that people call something else.</summary>
    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
    {
        ["Plu"] = "stock code", ["WhatsApp"] = "WhatsApp", ["Cnic"] = "CNIC", ["Iban"] = "IBAN", ["Ntn"] = "NTN", ["Epc"] = "EPC", ["Eobi"] = "EOBI",
        ["Json"] = "JSON", ["Sha"] = "SHA", ["Rma"] = "RMA", ["Id"] = "ID", ["Pin"] = "PIN", ["Url"] = "URL",
    };

    [GeneratedRegex("WhatsApp|[A-Z][a-z]*|[a-z]+|[0-9]+")]
    private static partial Regex Words();

    /// <summary>
    /// The message for SQL Server's refusal of text longer than its column (error 2628, or 8152 before SQL Server 2019),
    /// anywhere in <paramref name="ex"/>: with the field, and its size when the model of <paramref name="data"/> knows
    /// the column. Null for any other error.
    /// </summary>
    public static string? FromSqlServer(Exception ex, DataContext? data = null)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is not SqlException { Number: 2628 or 8152 } sql) continue;
            var named = sql.Number == 2628 ? TruncatedColumn().Match(sql.Message) : Match.Empty;
            if (!named.Success) return "A text is longer than its field allows. Shorten it and save again.";
            string table = named.Groups["table"].Value, column = named.Groups["column"].Value;
            table = table[(table.LastIndexOf('.') + 1)..];
            IModel? model = null;
            if (data is not null)
                using (var db = data.Create()) model = db.Model;
            var mapped = model?.GetRelationalModel().Tables.FirstOrDefault(t => string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase))
                ?.Columns.FirstOrDefault(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase))?.PropertyMappings;
            return mapped is [{ Property: var property }, ..] && property.GetMaxLength() is int max
                ? TooLongMessage(property.Name, max)
                : $"{FieldName(column)} is too long for its field.";
        }
        return null;
    }

    /// <summary>SQL Server's 2628: "String or binary data would be truncated in table 'Db.dbo.Suppliers', column 'Address1'. …"</summary>
    [GeneratedRegex("table '(?<table>[^']*)', column '(?<column>[^']*)'")]
    private static partial Regex TruncatedColumn();
}
