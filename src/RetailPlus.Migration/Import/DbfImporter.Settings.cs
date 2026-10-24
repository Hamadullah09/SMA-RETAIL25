using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data;
using RetailPlus.Migration.Codecs;

namespace RetailPlus.Migration.Import;

public sealed partial class DbfImporter
{
    /// <summary>SETUP.DBF fields mapped to typed <see cref="StoreSettings"/> properties; the rest become extras.</summary>
    private static readonly HashSet<string> MappedSetupFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPANY", "ADDR1", "ADDR2", "CITY", "STATE", "ZIP", "COUNTRY", "PHONE", "FAX",
        "VAT", "VATREG", "TAX1NAME", "TAX1", "TAX2NAME", "TAX2", "TAXCOMP",
        "STAFFDISC", "VOID_ID", "STAFF_ID", "CARRYCSZ", "UPCWEIGHT", "FORCERMA", "DISC4CASH", "YNTHCLIMIT", "THCLIMIT",
        "GIFTCARDMAX", "DP_ON", "DP_PRINT", "DP_FACTOR", "DP_THRESHO", "DP_PERCENT", "DP_RATE", "DP_FIXED", "DP_AMT",
        "PENRATE", "GRACE", "MINIMUM_COI", "UK_DATE",
    };

    private sealed record Numbers(long LastInvoice, long LastCustomer, long LastPurchaseOrder, long LastRma);

    private bool _ukDates;
    private readonly HashSet<int> _stationNumbers = [];
    private long _maxInvoiceNumber, _maxCustomerNumber, _maxPurchaseOrderNumber, _maxRmaNumber;

    private async Task ImportSetupAsync(CancellationToken ct)
    {
        var path = FindFile(_root, "SETUP.DBF");
        if (path is null)
        {
            _log.Warn("SETUP.DBF not found; default store settings created");
            await SaveAsync([new Pending(new StoreSettings { Id = 1, CompanyName = "My Store", MinimumTender = 0.01m })], ct);
            return;
        }
        using var table = Open(path);
        var result = Result(table, "StoreSettings");
        var record = table.Records().FirstOrDefault(r => !r.IsDeleted);
        if (record is null)
        {
            _log.Warn(table.RelativePath, "no live record; default store settings created");
            await SaveAsync([new Pending(new StoreSettings { Id = 1, CompanyName = "My Store", MinimumTender = 0.01m })], ct);
            return;
        }
        var s = new StoreSettings
        {
            Id = 1,
            CompanyName = record.Text("COMPANY"),
            Address1 = record.Text("ADDR1"),
            Address2 = record.Text("ADDR2"),
            City = record.Text("CITY"),
            State = record.Text("STATE"),
            Zip = record.Text("ZIP"),
            Country = record.Text("COUNTRY"),
            Phone = record.Text("PHONE"),
            Fax = record.Text("FAX"),
            TaxInclusive = record.Bool("VAT"),
            TaxRegistrationNumber = record.Text("VATREG"),
            Tax1Name = record.Text("TAX1NAME"),
            Tax1Rate = record.Dec("TAX1"),
            Tax2Name = record.Text("TAX2NAME"),
            Tax2Rate = record.Dec("TAX2"),
            Tax2Compound = record.Bool("TAXCOMP"),
            StaffMayDiscount = record.Bool("STAFFDISC"),
            SupervisorForVoids = record.Bool("VOID_ID"),
            TrackStaffSales = record.Bool("STAFF_ID"),
            CarryOverCityStateZip = record.Bool("CARRYCSZ"),
            ScanRandomWeightBarcodes = record.Bool("UPCWEIGHT"),
            UseRmaOnReturns = record.Bool("FORCERMA"),
            CashDiscountPercent = record.Dec("DISC4CASH"),
            LimitThc = record.Bool("YNTHCLIMIT"),
            ThcLimit = record.Dec("THCLIMIT"),
            GiftCardMaximum = record.Dec("GIFTCARDMAX"),
            BonusPointsEnabled = record.Bool("DP_ON"),
            PrintPointsOnSlips = record.Bool("DP_PRINT"),
            PointsPerDollar = record.Dec("DP_FACTOR"),
            PointsRequired = record.Dec("DP_THRESHO"),
            PercentDiscountReward = record.Bool("DP_PERCENT"),
            RewardPercent = record.Dec("DP_RATE"),
            FixedDiscountReward = record.Bool("DP_FIXED"),
            RewardAmount = record.Dec("DP_AMT"),
            InterestRatePerMonth = record.Dec("PENRATE"),
            GracePeriodDays = record.Int("GRACE", 0, 999),
            MinimumTender = record.Dec("MINIMUM_COI"),
            UseUkDateFormat = record.Bool("UK_DATE"),
        };
        _ukDates = s.UseUkDateFormat;
        var rows = new List<Pending> { One(s, nameof(StoreSettings), record) };
        foreach (var field in table.Dbf.Fields.Where(f => !MappedSetupFields.Contains(f.Name)))
        {
            // Raw text as stored (an overflowing numeric such as *** is kept as is, which is the point of extras).
            var raw = record.RawText(field.Name)?.Replace("\0", "", StringComparison.Ordinal).Trim();
            rows.Add(new Pending(Remember(new StoreSettingExtra { Name = field.Name, Value = string.IsNullOrEmpty(raw) ? null : raw }, record)));
        }
        result.Imported = await SaveAsync(rows, ct);
    }

    private Numbers ReadNumbers()
    {
        var path = FindFile(_root, "NUMBERS.DBF");
        if (path is null)
        {
            _log.Warn("NUMBERS.DBF not found; numbering continues after the highest imported numbers");
            return new Numbers(0, 0, 0, 0);
        }
        using var table = Open(path);
        var result = Result(table, "sequences");
        var record = table.Records().FirstOrDefault(r => !r.IsDeleted);
        if (record is null) return new Numbers(0, 0, 0, 0);
        result.Imported = 1;
        return new Numbers((long)record.Dec("LASTINVNO"), (long)record.Dec("LASTCUSTNO"), (long)record.Dec("LAST_PO"), (long)record.Dec("LAST_RMA"));
    }

    /// <summary>Restarts each sequence after the larger of the NUMBERS.DBF value and the highest number imported.</summary>
    private async Task RestartSequencesAsync(Numbers numbers, CancellationToken ct)
    {
        var restarts = new (string Sequence, long Next)[]
        {
            (RetailPlusDbContext.Sequences.InvoiceNumbers, Math.Max(numbers.LastInvoice, _maxInvoiceNumber) + 1),
            (RetailPlusDbContext.Sequences.CustomerNumbers, Math.Max(numbers.LastCustomer, _maxCustomerNumber) + 1),
            (RetailPlusDbContext.Sequences.PurchaseOrderNumbers, Math.Max(numbers.LastPurchaseOrder, _maxPurchaseOrderNumber) + 1),
            (RetailPlusDbContext.Sequences.RmaNumbers, Math.Max(numbers.LastRma, _maxRmaNumber) + 1),
        };
        foreach (var (sequence, next) in restarts)
        {
            // Names come from the fixed list in RetailPlusDbContext.Sequences; the value is a number.
            await ExecAsync($"ALTER SEQUENCE [dbo].[{sequence}] RESTART WITH {next.ToString(CultureInfo.InvariantCulture)}", ct);
            _log.Info($"Sequence {sequence} continues at {next}");
            var expected = next;
            var name = sequence;
            Check($"Sequence {sequence}: next number", expected, (db, token) => NextSequenceValueAsync(db, name, token));
        }
    }

    /// <summary>The value NEXT VALUE FOR would return, read without taking it.</summary>
    private static async Task<decimal> NextSequenceValueAsync(RetailPlusDbContext db, string sequence, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = conn.CreateCommand();
            command.CommandText = "SELECT CASE WHEN last_used_value IS NULL THEN CONVERT(bigint, current_value) " +
                "ELSE CONVERT(bigint, last_used_value) + CONVERT(bigint, increment) END FROM sys.sequences WHERE name = @name";
            var p = command.CreateParameter();
            p.ParameterName = "@name";
            p.Value = sequence;
            command.Parameters.Add(p);
            var value = await command.ExecuteScalarAsync(ct);
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task ImportDepartmentsAndCategoriesAsync(InventoryNames names, CancellationToken ct)
    {
        foreach (var (file, isDepartment) in new[] { ("DEPT.DBF", true), ("CATEGORY.DBF", false) })
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<Pending>();
            string field = isDepartment ? "DEPT" : "CATEGORY";
            var path = FindFile(_root, file);
            int sourceCount = 0;
            if (path is not null)
            {
                using var table = Open(path);
                var result = Result(table, isDepartment ? nameof(Department) : nameof(Category));
                foreach (var record in table.Records())
                {
                    if (record.IsDeleted) { result.Deleted++; continue; }
                    var name = record.Text(field);
                    if (name is null) { result.Blank++; continue; }
                    if (!known.Add(name)) { _log.Warn(record.Where, $"duplicate {field} skipped"); continue; }
                    sourceCount++;
                    rows.Add(isDepartment
                        ? One(new Department { Name = name }, nameof(Department), record)
                        : One(new Category { Name = name }, nameof(Category), record));
                }
                result.Imported = rows.Count;
            }
            int created = 0;
            foreach (var name in isDepartment ? names.Departments : names.Categories)
            {
                if (!known.Add(name)) continue;
                created++;
                rows.Add(new Pending(isDepartment ? new Department { Name = name } : new Category { Name = name }));
            }
            if (created > 0)
                _log.Warn($"{created} {(isDepartment ? "departments" : "categories")} used on items but missing from {file} were added to the list");
            await SaveAsync(rows, ct);

            await using var db = NewContext();
            var lookup = isDepartment ? _departments : _categories;
            var saved = isDepartment
                ? await db.Departments.Select(d => new { d.Id, d.Name }).ToListAsync(ct)
                : await db.Categories.Select(d => new { d.Id, d.Name }).ToListAsync(ct);
            foreach (var d in saved) lookup[d.Name] = d.Id;
            int expected = sourceCount + created;
            if (isDepartment) Check("Departments: list entries", expected, async (c, t) => await c.Departments.CountAsync(t));
            else Check("Categories: list entries", expected, async (c, t) => await c.Categories.CountAsync(t));
        }
    }

    private async Task ImportUsersAsync(CancellationToken ct)
    {
        var path = FindFile(_root, "USERS.DBF");
        if (path is null) return;
        using var table = Open(path);
        var result = Result(table, nameof(UserAccount));
        var rows = new List<Pending>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in table.Records())
        {
            if (record.IsBlank) { result.Blank++; continue; }
            if (record.IsDeleted) { result.Deleted++; rows.Add(TraceOnlyWithout(nameof(UserAccount), record, "P_WORD")); continue; }
            var userName = record.Text("USERNAM");
            if (userName is null) { result.Blank++; continue; }
            if (!names.Add(userName)) { _log.Warn(record.Where, "duplicate user name skipped"); continue; }
            // The password is never read: every imported user sets a new one at first login.
            var user = new UserAccount { UserName = userName, AccessLevel = record.Int("PRV", 0, 4), PasswordHash = null, MustChangePassword = true };
            rows.Add(One(user, nameof(UserAccount), record, "P_WORD"));
        }
        result.Imported = await SaveAsync(rows, ct);
        int expected = result.Imported;
        Check("Users: accounts", expected, async (db, t) => await db.UserAccounts.CountAsync(t));
    }

    private static Pending TraceOnlyWithout(string entityName, SourceRecord source, params string[] exclude)
    {
        var pending = new Pending(null);
        pending.Traces.Add((null, entityName, source, source.ToJson(exclude)));
        return pending;
    }

    private async Task ImportStaffAsync(CancellationToken ct)
    {
        var path = FindFile(_root, "STAFF.DBF");
        if (path is null) return;
        using var table = Open(path);
        var result = Result(table, nameof(StaffPeriod));
        var rows = new List<Pending>();
        foreach (var record in table.Records())
        {
            if (record.IsBlank) { result.Blank++; continue; }
            if (record.IsDeleted) { result.Deleted++; rows.Add(TraceOnly(nameof(StaffPeriod), record)); continue; }
            rows.Add(One(new StaffPeriod
            {
                UserName = record.Text("USERNAM") ?? "",
                FromDate = record.Date("FROM_DATE"),
                ToDate = record.Date("TO_DATE"),
                FromTime = record.Text("FROM_TIME"),
                ToTime = record.Text("TO_TIME"),
                Sales = record.Dec("SALES"),
                Commission = record.Dec("COMMISSION"),
                Hours = record.Dec("HOURS"),
            }, nameof(StaffPeriod), record));
        }
        result.Imported = await SaveAsync(rows, ct);
    }

    private async Task ImportTextsAsync(CancellationToken ct)
    {
        var rows = new List<Pending>();
        foreach (var (file, kind) in new[]
                 {
                     ("HEADER.TXT", DocumentTextKind.InvoiceHeader),
                     ("FOOTER.TXT", DocumentTextKind.InvoiceFooter),
                     ("POHEADER.TXT", DocumentTextKind.PurchaseOrderHeader),
                 })
        {
            var path = FindFile(_root, file);
            if (path is null || ReadText(path) is not { } text) continue;
            rows.Add(new Pending(new DocumentText { Kind = kind, Text = text }));
            _report.Tables.Add(new TableResult(file, nameof(DocumentText)) { Records = 1, Imported = 1 });
        }
        await SaveAsync(rows, ct);
    }

    private async Task ImportFastKeysAsync(CancellationToken ct)
    {
        var rows = new List<Pending>();
        var used = new HashSet<(int, int)>();
        var path = FindFile(_root, "FASTKEYS.DBF");
        if (path is not null)
        {
            using var table = Open(path);
            var result = Result(table, nameof(FastKey));
            foreach (var record in table.Records())
            {
                if (record.IsBlank) { result.Blank++; continue; }
                if (record.IsDeleted) { result.Deleted++; continue; }
                int number = record.Int("KEYNUM", 0, 99_999);
                var name = record.Text("KEYNAME");
                var plu = record.Text("PLU");
                bool isCategory = record.Bool("CATKEY");
                // An unprogrammed key carries its own number as its name. Those are not stored: the app shows a
                // blank key for every number without a row.
                if (name == number.ToString(CultureInfo.InvariantCulture)) name = null;
                if (name is null && plu is null && !isCategory && record.Dec("UNITPRICE1") == 0) { result.Blank++; continue; }
                if (number < 1) { _log.Warn(record.Where, "KEYNUM missing; key skipped"); continue; }
                // Keys 1–25 are the main panel; each further block of 25 holds the sub-keys of category key n (to confirm).
                int panel = (number - 1) / 25, key = (number - 1) % 25 + 1;
                if (!used.Add((panel, key))) { _log.Warn(record.Where, $"duplicate key number {number} skipped"); continue; }
                rows.Add(One(new FastKey
                {
                    Panel = panel,
                    KeyNumber = key,
                    Name = name,
                    Plu = plu,
                    FixedPrice = record.Dec("UNITPRICE1"),
                    IsCredit = record.Bool("CREDIT"),
                    Taxable1 = record.Bool("TAX1"),
                    Taxable2 = record.Bool("TAX2"),
                    IsCategory = isCategory,
                    Color = record.Text("COLOR"),
                }, nameof(FastKey), record));
            }
            result.Imported = await SaveAsync(rows, ct);
            int expected = result.Imported;
            Check("Fast keys: programmed keys", expected, async (db, t) => await db.FastKeys.CountAsync(t));
            return;
        }

        // No FASTKEYS.DBF: fall back to the default key set in Fastkey.cfg (num,name,plu,credit,tax1,tax2,price|...).
        var cfg = FindFile(_root, "Fastkey.cfg");
        if (cfg is null || ReadText(cfg) is not { } text) return;
        int imported = 0;
        foreach (var entry in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = entry.Split(',');
            if (t.Length < 7 || !int.TryParse(t[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number is < 1 or > 25) continue;
            var name = t[1].Trim();
            var plu = t[2].Trim();
            if (name.Length == 0 && plu.Length == 0) continue;
            decimal.TryParse(t[6].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var price);
            rows.Add(new Pending(new FastKey
            {
                Panel = 0,
                KeyNumber = number,
                Name = name.Length == 0 ? null : name,
                Plu = plu.Length == 0 ? null : plu,
                IsCredit = PackedLists.ParseTextLogical(t[3]),
                Taxable1 = PackedLists.ParseTextLogical(t[4]),
                Taxable2 = PackedLists.ParseTextLogical(t[5]),
                FixedPrice = price,
            }));
            imported++;
        }
        await SaveAsync(rows, ct);
        _report.Tables.Add(new TableResult("Fastkey.cfg", nameof(FastKey)) { Records = imported, Imported = imported });
    }

    /// <summary>Stations seen in SALESnnn/TOTALnnn files and the INI, always including station 1, plus the INI settings.</summary>
    private async Task ImportStationsAsync(CancellationToken ct)
    {
        _stationNumbers.Add(1);
        Dictionary<(string Section, string Key), string>? ini = null;
        int iniStation = 1;
        if (options.StationIniPath is { } iniPath)
        {
            if (!File.Exists(iniPath)) throw new ImportRefusedException($"Station INI {iniPath} does not exist.");
            ini = ParseIni(ReadText(iniPath) ?? "");
            if (ini.TryGetValue(("STARTUP", "POS Station"), out var station) &&
                int.TryParse(station, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 999)
                iniStation = n;
            _stationNumbers.Add(iniStation);
        }
        var rows = _stationNumbers.Order().Select(n => new Pending(new Station
        {
            Number = n,
            Name = $"Station {n:000}",
            Settings = n == iniStation && ini is not null
                ? ini.Select(kv => new StationSetting { Section = kv.Key.Section, Key = kv.Key.Key, Value = kv.Value }).ToList()
                : [],
        })).ToList();
        await SaveAsync(rows, ct);
        if (ini is not null)
            _report.Tables.Add(new TableResult(Path.GetFileName(options.StationIniPath!), nameof(StationSetting)) { Records = ini.Count, Imported = ini.Count });
    }

    private static Dictionary<(string Section, string Key), string> ParseIni(string text)
    {
        var values = new Dictionary<(string, string), string>();
        string section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line[^1] == ']') { section = line[1..^1].Trim(); continue; }
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            values[(section, line[..eq].Trim())] = line[(eq + 1)..].Trim();
        }
        return values;
    }

    private async Task SeedTenderTypesAsync(CancellationToken ct)
    {
        await using (var db = NewContext())
            if (await db.TenderTypes.AnyAsync(ct)) return;
        (string Name, TenderKind Kind)[] defaults =
        [
            ("Cash", TenderKind.Cash), ("Visa", TenderKind.Card), ("MasterCard", TenderKind.Card), ("Amex", TenderKind.Card),
            ("Debit", TenderKind.Card), ("Cheque", TenderKind.Cheque), ("Gift Card", TenderKind.GiftCard), ("On Account", TenderKind.OnAccount),
        ];
        int order = 0;
        await SaveAsync(defaults.Select(d => new Pending(new TenderType
        {
            Name = d.Name, Kind = d.Kind, SortOrder = ++order, OpensDrawer = d.Kind is TenderKind.Cash or TenderKind.Cheque, IsActive = true,
        })).ToList(), ct);
    }

    /// <summary>Lists data files that exist but are not imported, so the report says so instead of staying silent.</summary>
    private void NoteUnimportedFiles()
    {
        foreach (var file in new[] { "PRNT.DBF", "PO.DBF", "V2.INI", "PLUSFTP.INI", "cnt.cfg" })
            if (FindFile(_root, file) is not null)
                _report.NotImported.Add(file + ": " + file.ToUpperInvariant() switch
                {
                    "PRNT.DBF" => "print line buffer, scratch data",
                    "PO.DBF" => "empty purchase order template",
                    "V2.INI" => "demo session counter",
                    "PLUSFTP.INI" => "FTP settings, not used by the rewrite",
                    _ => "program state, not business data",
                });
        foreach (var file in Directory.EnumerateFiles(_root, "Columns_*.cfg"))
            _report.NotImported.Add(Path.GetFileName(file) + ": window column layout of the original");
    }

    [GeneratedRegex(@"^\d{1,18}$")]
    private static partial Regex DigitsOnly();

    private static long? AsNumber(string? text) =>
        text is not null && DigitsOnly().IsMatch(text) ? long.Parse(text, CultureInfo.InvariantCulture) : null;
}
