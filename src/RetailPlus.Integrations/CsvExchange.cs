using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Data.Services;

namespace RetailPlus.Integrations;

/// <summary>Result of a CSV import; <paramref name="Tags"/> counts the RFID tags (pieces) registered or updated.</summary>
public sealed record CsvImportResult(int Added, int Updated, int Skipped, IReadOnlyList<string> Problems, int Tags = 0);

/// <summary>
/// Tools / Import and Export: inventory (see CsvExchange.Items) and clients as CSV files, e.g. for a supplier's price
/// file, a web shop or a spreadsheet. Items are matched on the stock code, clients on the customer number.
/// </summary>
public sealed partial class CsvExchange(DataContext data)
{
    public static readonly string[] ClientColumns =
    [
        "Number", "FirstName", "LastName", "Company", "Address1", "Address2", "City", "State", "Zip", "Country", "WorkPhone", "HomePhone", "Email",
        "AccountNumber", "Type", "Discount", "PriceLevel", "CreditLimit", "Birthday",
    ];

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<string> ExportClientsAsync()
    {
        await using var db = data.Create();
        var clients = await db.Clients.AsNoTracking().Where(c => !c.IsDeleted).OrderBy(c => c.LastName).ToListAsync();
        var sb = new StringBuilder().AppendLine(Csv.Line(ClientColumns));
        foreach (var c in clients)
            sb.AppendLine(Csv.Line([
                c.Number, c.FirstName, c.LastName, c.Company, c.Address1, c.Address2, c.City, c.State, c.Zip, c.Country, c.WorkPhone, c.HomePhone, c.Email,
                c.AccountNumber, c.Type, N(c.UsualDiscountPercent), c.PriceLevel.ToString(Inv), N(c.CreditLimit), c.Birthday?.ToString("yyyy-MM-dd", Inv),
            ]));
        return sb.ToString();
    }

    public async Task<CsvImportResult> ImportClientsAsync(string csv)
    {
        await using var db = data.Create();
        var byNumber = (await db.Clients.Where(c => !c.IsDeleted && c.Number != null).ToListAsync())
            .GroupBy(c => c.Number!).ToDictionary(g => g.Key, g => g.First());
        int added = 0, updated = 0, skipped = 0, rowNo = 1;
        var problems = new List<string>();
        foreach (var r in CsvRecord.Read(csv))
        {
            rowNo++;
            if (string.IsNullOrWhiteSpace(r["LastName"]) && string.IsNullOrWhiteSpace(r["Company"]) && string.IsNullOrWhiteSpace(r["FirstName"]))
            { skipped++; problems.Add($"Row {rowNo}: no name or company."); continue; }
            var number = r["Number"];
            Client client;
            bool isNew = string.IsNullOrWhiteSpace(number) || !byNumber.TryGetValue(number, out client!);
            if (isNew)
            {
                client = new Client
                {
                    Number = string.IsNullOrWhiteSpace(number)
                        ? (await db.NextNumberAsync(Data.RetailPlusDbContext.Sequences.CustomerNumbers)).ToString(Inv) : number,
                };
                db.Clients.Add(client);
                byNumber[client.Number!] = client;
            }
            else client = byNumber[number!];
            string? S(string name) => r.Has(name) ? r[name] : null;
            if (r.Has("FirstName")) client.FirstName = S("FirstName");
            if (r.Has("LastName")) client.LastName = S("LastName");
            if (r.Has("Company")) client.Company = S("Company");
            if (r.Has("Address1")) client.Address1 = S("Address1");
            if (r.Has("Address2")) client.Address2 = S("Address2");
            if (r.Has("City")) client.City = S("City");
            if (r.Has("State")) client.State = S("State");
            if (r.Has("Zip")) client.Zip = S("Zip");
            if (r.Has("Country")) client.Country = S("Country");
            if (r.Has("WorkPhone")) client.WorkPhone = S("WorkPhone");
            if (r.Has("HomePhone")) client.HomePhone = S("HomePhone");
            if (r.Has("Email")) client.Email = S("Email");
            if (r.Has("Type")) client.Type = S("Type");
            if (decimal.TryParse(r["Discount"], NumberStyles.Number, Inv, out var disc)) client.UsualDiscountPercent = disc;
            if (int.TryParse(r["PriceLevel"], NumberStyles.Integer, Inv, out var level)) client.PriceLevel = level;
            if (decimal.TryParse(r["CreditLimit"], NumberStyles.Number, Inv, out var limit)) client.CreditLimit = limit;
            if (DateOnly.TryParse(r["Birthday"], Inv, out var bd)) client.Birthday = bd;
            if (isNew) added++; else updated++;
        }
        await db.SaveChangesAsync();
        return new CsvImportResult(added, updated, skipped, problems);
    }
}
