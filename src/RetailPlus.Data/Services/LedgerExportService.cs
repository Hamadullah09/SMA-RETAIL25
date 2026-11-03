using System.Globalization;
using System.Text;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>A posted entry with its lines, as the exports read it.</summary>
public sealed record ExportEntry(long Number, DateOnly Date, JournalKind Kind, string Memo, string? Reference, string? StoreCode,
    IReadOnlyList<(string Code, string Name, AccountType Type, decimal Debit, decimal Credit, string? Memo)> Lines);

/// <summary>
/// The posted ledger in an accountant's program's format, for a date range and a store (or the company): Tally's XML
/// vouchers (one Journal voucher per entry) and QuickBooks' IIF (one general journal transaction per entry, with the
/// account list first). The daily journal's CSV export (<see cref="AccountingService"/>) stays as it was.
/// </summary>
public sealed class LedgerExportService(DataContext data)
{
    public async Task<List<ExportEntry>> EntriesAsync(DateOnly from, DateOnly to, Guid? store = null)
    {
        await using var db = data.Create();
        var q = db.JournalEntries.AsNoTracking().Where(e => e.Status != JournalStatus.Draft && e.Date >= from && e.Date <= to);
        if (store is Guid s) q = q.Where(e => e.StoreId == s || e.Lines.Any(l => l.StoreId == s));
        var entries = await q.Include(e => e.Lines).ThenInclude(l => l.Account).AsSplitQuery().OrderBy(e => e.Date).ThenBy(e => e.Number).ToListAsync();
        var codes = await db.Stores.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code);
        return entries.Select(e => new ExportEntry(e.Number, e.Date, e.Kind, e.Memo, e.Reference, e.StoreId is Guid id ? codes.GetValueOrDefault(id) : null,
                e.Lines.Where(l => store is null || l.StoreId == store).OrderBy(l => l.LineNumber)
                    .Select(l => (l.Account!.Code, l.Account.Name, l.Account.Type, l.Debit, l.Credit, l.Memo)).ToList()))
            .Where(e => e.Lines.Count > 0).ToList();
    }

    /// <summary>
    /// Tally XML: an import envelope of Journal vouchers. Ledgers are named "code name" as in the chart (create them in
    /// Tally first, or let Tally make them); a debit is a negative amount marked deemed positive, as Tally writes them.
    /// </summary>
    public static string ToTallyXml(IEnumerable<ExportEntry> entries, string company)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false, Encoding = new UTF8Encoding(false) };
        using (var w = XmlWriter.Create(new StringWriter(sb, CultureInfo.InvariantCulture), settings))
        {
            w.WriteStartElement("ENVELOPE");
            w.WriteStartElement("HEADER");
            w.WriteElementString("TALLYREQUEST", "Import Data");
            w.WriteEndElement();
            w.WriteStartElement("BODY");
            w.WriteStartElement("IMPORTDATA");
            w.WriteStartElement("REQUESTDESC");
            w.WriteElementString("REPORTNAME", "Vouchers");
            w.WriteStartElement("STATICVARIABLES");
            w.WriteElementString("SVCURRENTCOMPANY", company);
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("REQUESTDATA");
            foreach (var e in entries)
            {
                w.WriteStartElement("TALLYMESSAGE");
                w.WriteAttributeString("xmlns", "UDF", null, "TallyUDF");
                w.WriteStartElement("VOUCHER");
                w.WriteAttributeString("VCHTYPE", "Journal");
                w.WriteAttributeString("ACTION", "Create");
                w.WriteElementString("DATE", e.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                w.WriteElementString("VOUCHERTYPENAME", "Journal");
                w.WriteElementString("VOUCHERNUMBER", e.Number.ToString(CultureInfo.InvariantCulture));
                if (e.Reference is { Length: > 0 } reference) w.WriteElementString("REFERENCE", reference);
                w.WriteElementString("NARRATION", e.StoreCode is null ? e.Memo : $"{e.Memo} [{e.StoreCode}]");
                foreach (var l in e.Lines)
                {
                    w.WriteStartElement("ALLLEDGERENTRIES.LIST");
                    w.WriteElementString("LEDGERNAME", $"{l.Code} {l.Name}");
                    bool debit = l.Debit != 0;
                    w.WriteElementString("ISDEEMEDPOSITIVE", debit ? "Yes" : "No");
                    w.WriteElementString("AMOUNT", (debit ? -l.Debit : l.Credit).ToString("0.00", CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }
        return sb.ToString();
    }

    /// <summary>
    /// QuickBooks IIF: the accounts (!ACCNT) and one GENERAL JOURNAL transaction per entry (!TRNS, !SPL, !ENDTRNS),
    /// tab-separated, debits positive and credits negative, dates as MM/DD/YYYY.
    /// </summary>
    public static string ToIif(IReadOnlyList<ExportEntry> entries)
    {
        var sb = new StringBuilder();
        static string Clean(string? s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Replace("\"", "'", StringComparison.Ordinal);
        sb.Append("!ACCNT\tNAME\tACCNTTYPE\tACCNUM\r\n");
        foreach (var a in entries.SelectMany(e => e.Lines).Select(l => (l.Code, l.Name, l.Type)).Distinct().OrderBy(a => a.Code, StringComparer.Ordinal))
            sb.Append("ACCNT\t").Append(Clean(a.Name)).Append('\t').Append(IifType(a.Type)).Append('\t').Append(Clean(a.Code)).Append("\r\n");
        sb.Append("!TRNS\tTRNSTYPE\tDATE\tACCNT\tAMOUNT\tDOCNUM\tMEMO\r\n");
        sb.Append("!SPL\tTRNSTYPE\tDATE\tACCNT\tAMOUNT\tDOCNUM\tMEMO\r\n");
        sb.Append("!ENDTRNS\r\n");
        foreach (var e in entries)
        {
            var date = e.Date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            for (int i = 0; i < e.Lines.Count; i++)
            {
                var l = e.Lines[i];
                sb.Append(i == 0 ? "TRNS" : "SPL").Append("\tGENERAL JOURNAL\t").Append(date).Append('\t').Append(Clean(l.Name)).Append('\t')
                  .Append((l.Debit - l.Credit).ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(e.Number.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(Clean(l.Memo ?? e.Memo)).Append("\r\n");
            }
            sb.Append("ENDTRNS\r\n");
        }
        return sb.ToString();
    }

    /// <summary>The posted entries as CSV: Date, Number, Kind, Store, Account, Account name, Debit, Credit, Memo, Reference.</summary>
    public static string ToCsv(IEnumerable<ExportEntry> entries)
    {
        var sb = new StringBuilder("Date,Number,Kind,Store,Account,Account name,Debit,Credit,Memo,Reference\r\n");
        static string E(string? s) => s is null ? "" : s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
        foreach (var e in entries)
            foreach (var l in e.Lines)
                sb.Append(e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(e.Number.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.Kind).Append(',').Append(E(e.StoreCode)).Append(',').Append(E(l.Code)).Append(',').Append(E(l.Name)).Append(',')
                  .Append(l.Debit == 0 ? "" : l.Debit.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(l.Credit == 0 ? "" : l.Credit.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(E(l.Memo ?? e.Memo)).Append(',')
                  .Append(E(e.Reference)).Append("\r\n");
        return sb.ToString();
    }

    private static string IifType(AccountType type) => type switch
    {
        AccountType.Asset => "OCASSET",
        AccountType.Liability => "OCLIAB",
        AccountType.Equity => "EQUITY",
        AccountType.Income => "INC",
        _ => "EXP",
    };
}
