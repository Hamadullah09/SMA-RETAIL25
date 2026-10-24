using System.Text.Json;
using RetailPlus.Migration.Dbf;

namespace RetailPlus.Migration.Import;

/// <summary>
/// Lenient typed access to one source record for the importer. Unlike <see cref="DbfRecord"/>, a field that is
/// missing (older table structures) reads as blank, and a value that can't be read (for example a numeric overflow
/// stored as <c>***</c>) reads as blank with a warning naming the file, record and field, never the value.
/// </summary>
internal sealed class SourceRecord(SourceTable table, DbfRecord record)
{
    public SourceTable Table { get; } = table;
    public int Number => record.RecordNumber;
    public bool IsDeleted => record.IsDeleted;

    public string Where => $"{Table.RelativePath} record {Number}";

    /// <summary>Character field text, trimmed; null when blank or when the table has no such field.</summary>
    public string? Text(string field)
    {
        var f = Table.Dbf.FindField(field);
        if (f is null) return null;
        var text = record.GetString(f).Replace("\0", "", StringComparison.Ordinal).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Character field text without trimming or NUL removal (for packed lists such as WEEKLY).</summary>
    public string? RawText(string field)
    {
        var f = Table.Dbf.FindField(field);
        return f is null ? null : record.GetString(f);
    }

    public decimal Dec(string field)
    {
        var f = Table.Dbf.FindField(field);
        if (f is null) return 0;
        try
        {
            // Character fields hold numbers as text in some tables (FASTKEYS.UNITPRICE1).
            if (f.Type == 'C')
            {
                var text = Text(field);
                if (text is null) return 0;
                if (decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
                Table.Log.Warn(Where, $"{field} is not a number; 0 used");
                return 0;
            }
            return record.GetDecimal(f) ?? 0;
        }
        catch (DbfFormatException)
        {
            Table.Log.Warn(Where, $"{field} holds an unreadable number (overflow?); 0 used");
            return 0;
        }
    }

    public int Int(string field, int min = int.MinValue, int max = int.MaxValue)
    {
        var value = decimal.Truncate(Dec(field));
        if (value < min || value > max)
        {
            var clamped = Math.Clamp(value, min, max);
            Table.Log.Warn(Where, $"{field} = {value} is outside {min}..{max}; {clamped} used");
            value = clamped;
        }
        return (int)value;
    }

    public DateOnly? Date(string field)
    {
        var f = Table.Dbf.FindField(field);
        if (f is null) return null;
        try
        {
            return record.GetDate(f);
        }
        catch (DbfFormatException)
        {
            Table.Log.Warn(Where, $"{field} holds an invalid date; left empty");
            return null;
        }
    }

    public bool Bool(string field)
    {
        var f = Table.Dbf.FindField(field);
        if (f is null) return false;
        if (f.Type == 'C') return Codecs.PackedLists.ParseTextLogical(Text(field));
        try
        {
            return record.GetLogical(f) ?? false;
        }
        catch (DbfFormatException)
        {
            Table.Log.Warn(Where, $"{field} holds an invalid logical; false used");
            return false;
        }
    }

    /// <summary>Memo text with trailing blanks and NULs removed; null when empty or missing.</summary>
    public string? Memo(string field)
    {
        var f = Table.Dbf.FindField(field);
        if (f is null || f.Type != 'M') return null;
        try
        {
            var text = record.GetMemo(f)?.Text.TrimEnd(' ', '\0', '\r', '\n', '\x1a');
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (DbfFormatException ex)
        {
            Table.Log.Warn(Where, $"{field} memo could not be read ({ex.Detail})");
            return null;
        }
    }

    /// <summary>True when every field is blank: the empty template records some tables carry.</summary>
    public bool IsBlank
    {
        get
        {
            foreach (var f in Table.Dbf.Fields)
            {
                var bytes = record.GetBytes(f);
                foreach (var b in bytes)
                    if (b != (byte)' ' && b != 0) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The whole record as JSON (field name → text as stored, trimmed; memos as text) for the import trace.
    /// Fields named in <paramref name="exclude"/> are left out entirely, which is how passwords stay out.
    /// </summary>
    public string ToJson(params string[] exclude)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (IsDeleted) values["_deleted"] = true;
        foreach (var f in Table.Dbf.Fields)
        {
            if (exclude.Contains(f.Name, StringComparer.OrdinalIgnoreCase)) continue;
            string? value;
            if (f.Type == 'M')
            {
                value = Memo(f.Name);
            }
            else
            {
                value = record.GetString(f).Replace("\0", "", StringComparison.Ordinal).Trim();
                if (value.Length == 0) value = null;
            }
            if (value is not null) values[f.Name] = value;
        }
        return JsonSerializer.Serialize(values);
    }
}

/// <summary>An open source table with the path it is reported under.</summary>
internal sealed class SourceTable(DbfTable dbf, string relativePath, ImportLog log) : IDisposable
{
    public DbfTable Dbf { get; } = dbf;
    public string RelativePath { get; } = relativePath;
    public ImportLog Log { get; } = log;

    public IEnumerable<SourceRecord> Records()
    {
        foreach (var r in Dbf.ReadRecords())
            yield return new SourceRecord(this, r);
    }

    /// <summary>The header's last-update date; Retail Plus writes a two-digit year.</summary>
    public DateOnly? LastUpdated
    {
        get
        {
            int yy = Dbf.Header.LastUpdateYear;
            int year = yy < 100 ? (yy < 80 ? 2000 + yy : 1900 + yy) : 1900 + yy;
            try
            {
                return new DateOnly(year, Dbf.Header.LastUpdateMonth, Dbf.Header.LastUpdateDay);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }

    public void Dispose() => Dbf.Dispose();
}
