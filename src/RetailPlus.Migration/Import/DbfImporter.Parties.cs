using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Migration.Import;

public sealed partial class DbfImporter
{
    private const string CreatedSupplierNote = "Created by the import: named on inventory items but missing from SUPPLIER.DBF.";

    private readonly Dictionary<string, int> _departments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _categories = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Suppliers by company name and by "first last", for the name references in inventory and POs.</summary>
    private readonly Dictionary<string, int> _suppliers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _clientsByNumber = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _clientsByAccount = new(StringComparer.OrdinalIgnoreCase);

    private async Task ImportPartiesAsync(IReadOnlyCollection<string> supplierNamesOnItems, CancellationToken ct)
    {
        // Suppliers first: items link to them by name.
        int supplierSource = await ImportPartyTableAsync("SUPPLIER.DBF", () => new Supplier(), ct);
        int created = 0;
        var createdRows = new List<Pending>();
        await LoadSupplierLookupAsync(ct);
        foreach (var name in supplierNamesOnItems)
        {
            if (_suppliers.ContainsKey(name)) continue;
            createdRows.Add(new Pending(new Supplier { Company = name, Notes = CreatedSupplierNote }));
            _suppliers[name] = 0;
            created++;
        }
        if (created > 0)
        {
            await SaveAsync(createdRows, ct);
            _log.Warn($"{created} suppliers named on inventory items but missing from SUPPLIER.DBF were created (see their notes)");
            await LoadSupplierLookupAsync(ct);
        }
        int expectedSuppliers = supplierSource + created;
        Check($"Suppliers: records (incl. {created} created from inventory)", expectedSuppliers, async (db, t) => await db.Suppliers.CountAsync(t));

        await ImportPartyTableAsync("CLIENTS.DBF", () => new Client(), ct);
        await using var db = NewContext();
        foreach (var c in await db.Clients.Select(c => new { c.Id, c.Number, c.AccountNumber, c.IsDeleted }).ToListAsync(ct))
        {
            // Numbers of deleted clients are not handed out again: the client may be undeleted.
            if (AsNumber(c.Number) is { } n) _maxCustomerNumber = Math.Max(_maxCustomerNumber, n);
            if (c.IsDeleted) continue;
            if (c.Number is not null) _clientsByNumber[c.Number] = c.Id;
            if (c.AccountNumber is not null) _clientsByAccount[c.AccountNumber] = c.Id;
        }
    }

    private async Task LoadSupplierLookupAsync(CancellationToken ct)
    {
        await using var db = NewContext();
        var all = await db.Suppliers.Where(s => !s.IsDeleted).OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Company, s.FirstName, s.LastName }).ToListAsync(ct);
        foreach (var s in all)
        {
            var person = $"{s.FirstName} {s.LastName}".Trim();
            if (person.Length > 0) _suppliers.TryAdd(person, s.Id);
        }
        // Company names win over person names.
        foreach (var s in all)
            if (!string.IsNullOrWhiteSpace(s.Company)) _suppliers[s.Company.Trim()] = s.Id;
    }

    private int? SupplierId(string? name) =>
        name is not null && _suppliers.TryGetValue(name, out var id) && id > 0 ? id : null;

    private int? ClientId(string? accountNumber, string? customerNumber)
    {
        if (accountNumber is not null && _clientsByAccount.TryGetValue(accountNumber, out var a)) return a;
        if (customerNumber is not null && _clientsByNumber.TryGetValue(customerNumber, out var c)) return c;
        return null;
    }

    /// <summary>CLIENTS.DBF and SUPPLIER.DBF share one structure. Returns the number of non-blank source records.</summary>
    private async Task<int> ImportPartyTableAsync<T>(string file, Func<T> create, CancellationToken ct) where T : Party
    {
        var path = FindFile(_root, file);
        if (path is null) return 0;
        using var table = Open(path);
        string entity = typeof(T).Name;
        var result = Result(table, entity);
        var rows = new List<Pending>();
        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        decimal liveBalance = 0;
        foreach (var record in table.Records())
        {
            if (record.IsBlank) { result.Blank++; continue; }
            if (record.IsDeleted) result.Deleted++;
            var party = create();
            party.FirstName = record.Text("FNAME");
            party.LastName = record.Text("LNAME");
            party.Title = record.Text("TITLE");
            party.Company = record.Text("COMPANY");
            party.Address1 = record.Text("ADDR1");
            party.Address2 = record.Text("ADDR2");
            party.City = record.Text("CITY");
            party.State = record.Text("STATE");
            party.Zip = record.Text("ZIP");
            party.Country = record.Text("COUNTRY");
            party.WorkPhone = record.Text("WORKPHONE");
            party.Extension = record.Text("EXT");
            party.Fax = record.Text("FAX");
            party.HomePhone = record.Text("HOMEPHONE");
            party.Email = record.Text("EMAIL");
            party.Website = record.Text("WEBSITE");
            party.Number = record.Text("CUSTNO");
            party.AccountNumber = record.Text("ACCTNO");
            party.LastMailed = record.Date("LMAIL");
            party.LastPurchase = record.Date("LPURCHASE");
            party.Type = record.Text("CLIENTTYPE");
            party.ShipTo = record.Memo("SHIPTO");
            party.Notes = record.Memo("NOTES");
            party.PhotoPath = PhotoPath(record.Text("PICPATH"));
            party.IsFlagged = record.Bool("PR");
            party.IsDeleted = record.IsDeleted;
            party.DeletedAt = record.IsDeleted ? _now : null;

            // Numbers must be unique among live rows; a duplicate keeps its number in the notes instead.
            if (!party.IsDeleted)
            {
                if (party.Number is not null && !numbers.Add(party.Number))
                {
                    _log.Warn(record.Where, "duplicate CUSTNO; number cleared and kept in the notes");
                    party.Notes = AppendNote(party.Notes, $"Imported customer number {party.Number} was also used by another record.");
                    party.Number = null;
                }
                if (party.AccountNumber is not null && !accounts.Add(party.AccountNumber))
                {
                    _log.Warn(record.Where, "duplicate ACCTNO; account number cleared and kept in the notes");
                    party.Notes = AppendNote(party.Notes, $"Imported account number {party.AccountNumber} was also used by another record.");
                    party.AccountNumber = null;
                }
            }

            if (party is Client client)
            {
                client.Birthday = record.Date("BIRTHDAY");
                client.Points = record.Dec("POINTS");
                client.UsualDiscountPercent = record.Dec("DISCOUNT");
                client.ExemptTax1 = record.Bool("EXEMPTTAX1");
                client.ExemptTax2 = record.Bool("EXEMPTTAX2");
                client.PriceLevel = record.Int("PRICELEVEL", 0, 4);
                client.CreditLimit = record.Dec("CREDIT");
                client.Balance = record.Dec("BALANCE");
                client.RecurringItemCode = record.Text("BILLITEM");
                client.RecurringEvery = record.Int("BILLEVERY", 0, 9999);
                client.RecurringUnit = record.Text("DMY");
                client.RecurringNextBilling = record.Date("NEXTBILL");
                client.RecurringLastBilled = record.Date("LBILLED");
                client.RecurringMaxBillings = record.Int("BILLQTY", 0, 9999);
                client.RecurringTimesBilled = record.Int("BILLSSENT", 0, 9999);
                client.RecurringActive = record.Bool("NOWACTIVE");
                if (!client.IsDeleted) liveBalance += client.Balance;
            }
            rows.Add(One(party, entity, record));
        }
        result.Imported = await SaveAsync(rows, ct);

        int expected = result.Records - result.Blank;
        if (typeof(T) == typeof(Client))
        {
            Check("Clients: records", expected, async (db, t) => await db.Clients.CountAsync(t));
            Check("Clients: balance owing (live)", liveBalance, async (db, t) => await db.Clients.Where(c => !c.IsDeleted).SumAsync(c => c.Balance, t));
        }
        return expected;
    }

    /// <summary>
    /// PICPATH holds absolute paths into the original's PICS folder (often of an older install). The rewrite keeps the
    /// path relative to the photo folder when it points into a PICS folder, and the full path otherwise.
    /// </summary>
    internal static string? PhotoPath(string? path)
    {
        if (path is null) return null;
        int at = path.LastIndexOf("\\PICS\\", StringComparison.OrdinalIgnoreCase);
        return at >= 0 ? path[(at + 6)..] : path;
    }
}
