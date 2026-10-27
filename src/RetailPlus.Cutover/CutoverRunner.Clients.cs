using System.Globalization;
using RetailPlus.Core.Model;
using RetailPlus.Cutover.Source;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>A carried-over customer: the client it became, with what documents copy from it.</summary>
    private sealed record CarriedClient(Guid Id, Client Client, SourceRow Row);

    private readonly Dictionary<long, CarriedClient> _clients = [];

    private CarriedClient? ClientOf(long? customerId) => customerId is long id ? _clients.GetValueOrDefault(id) : null;

    private static string? DisplayName(Client? client) => client is null ? null
        : !string.IsNullOrWhiteSpace(client.Company) ? client.Company : $"{client.FirstName} {client.LastName}".Trim();

    /// <summary>The store an invoice belongs to: its sale's, or else its customer's location's.</summary>
    private CarriedStore? InvoiceStore(SourceRow invoice) =>
        _src["sales_transactions"].Find(invoice.LongOrNull("transaction_id")) is { } sale
            ? StoreOf(sale.Long("location_id"))
            : StoreOf(_src["customers"].Find(invoice.Long("customer_id"))?.Long("location_id"));

    /// <summary>Retail25 invoices carried over (every one of a carried store; void ones as voided).</summary>
    private IEnumerable<SourceRow> CarriedInvoices() => _src["invoices"].Rows.Where(i => InvoiceStore(i) is not null);

    /// <summary>Open layaways of carried stores; paid-off and cancelled ones are history.</summary>
    private IEnumerable<SourceRow> OpenLayaways() => _src["layaways"].Rows.Where(l => l.Int("status") == 0 && _stores.ContainsKey(l.Long("location_id")));

    private static bool IsVoid(SourceRow invoice) => string.Equals(invoice.Text("status"), "Void", StringComparison.OrdinalIgnoreCase) || invoice.Text("status") == "2";

    /// <summary>What a client owes on invoices: the balances of their invoices that are not void.</summary>
    private Dictionary<long, decimal> InvoiceOwing() => CarriedInvoices().Where(i => !IsVoid(i))
        .GroupBy(i => i.Long("customer_id")).ToDictionary(g => g.Key, g => g.Sum(i => R2(i.Dec("balance_due"))));

    /// <summary>What a client owes on layaways: the unpaid rest of their open layaways.</summary>
    private Dictionary<long, decimal> LayawayOwing() => OpenLayaways()
        .GroupBy(l => l.Long("customer_id")).ToDictionary(g => g.Key, g => g.Sum(l => R2(l.Dec("total")) - R2(l.Dec("amount_paid"))));

    /// <summary>Rounded to cents, without counting it in the report (for totals worked out from Retail25).</summary>
    private static decimal R2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal R3(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    /// <summary>Address lines as one text (the ship-to and bill-to of the new model).</summary>
    private static string? Address(SourceRow row, string prefix)
    {
        var lines = new[]
        {
            row.Text(prefix + "line1"), row.Text(prefix + "line2"),
            string.Join(" ", new[] { row.Text(prefix + "city"), row.Text(prefix + "state_or_province"), row.Text(prefix + "postal_code") }.OfType<string>()),
            row.Text(prefix + "country"),
        }.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Customers become clients, with their account (number, credit limit; the balance is what their invoices and layaways
    /// owe), their pricing profile (usual discount, price level, tax exemptions) and their reward points.
    /// </summary>
    private async Task ClientsAsync(CancellationToken ct)
    {
        Log("Clients");
        var area = _report.Area("Clients", "customers, customer_accounts, customer_pricing_profiles");
        var accounts = _src["customer_accounts"].Rows.GroupBy(a => a.Long("customer_id")).ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).First());
        var profiles = _src["customer_pricing_profiles"].Rows.GroupBy(a => a.Long("customer_id")).ToDictionary(g => g.Key, g => g.OrderBy(a => a.Id).First());
        var ledger = _src["loyalty_ledger_entries"].Rows.GroupBy(e => e.Long("customer_id")).ToDictionary(g => g.Key, g => g.Sum(e => (decimal)e.Long("points")));
        var invoiceOwing = InvoiceOwing();
        var layawayOwing = LayawayOwing();
        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accountNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clients = new List<Client>();
        foreach (var row in _src["customers"].Rows.OrderBy(r => r.Long("location_id")).ThenBy(r => r.Id))
        {
            area.Read++;
            var store = StoreOf(row.Long("location_id"));
            if (store is null && _report.StoreCode is not null && !invoiceOwing.ContainsKey(row.Id) && !layawayOwing.ContainsKey(row.Id))
            {
                area.Skip("store not carried over (--store-code)");
                continue;
            }
            string code = store?.Code ?? "R25";
            bool deleted = row.Bool("is_deleted");
            var account = accounts.GetValueOrDefault(row.Id);
            var profile = profiles.GetValueOrDefault(row.Id);

            string number = Number(row.Long("customer_number"));
            string? unique = deleted ? number : UniqueNumber(number, code, numbers, 11);
            if (unique != number) Note($"Retail25 customer number {number} ({row.Text("company") ?? row.Text("last_name")}) is {unique} here: another location's customer has the same number.");
            string? accountNumber = account is null ? null : Number(account.Long("account_number"));
            string? uniqueAccount = deleted ? accountNumber : UniqueNumber(accountNumber, code, accountNumbers, 10);
            if (uniqueAccount != accountNumber) Note($"Retail25 account number {accountNumber} is {uniqueAccount} here: another location's account has the same number.");

            decimal points = profile is not null ? profile.Long("reward_points") : ledger.GetValueOrDefault(row.Id);
            if (profile is not null && ledger.TryGetValue(row.Id, out var ledgerPoints) && ledgerPoints != points)
                Warn($"customers {row.Id}: reward points {points} on the profile, {ledgerPoints} in the loyalty ledger; the profile's balance is carried over");
            int level = profile?.Int("price_level") ?? 1;
            if (R2(account?.Dec("balance_due") ?? 0) != invoiceOwing.GetValueOrDefault(row.Id))
                Warn($"customers {row.Id}: the account balance ({account?.Dec("balance_due") ?? 0:0.00}) is not the sum of the invoice balances ({invoiceOwing.GetValueOrDefault(row.Id):0.00}); the client owes what the invoices say");
            var client = From(new Client
            {
                Id = StableIds.For("customer", row.Id),
                FirstName = row.Text("first_name"), LastName = row.Text("last_name"), Company = row.Text("company"), Title = row.Text("title"),
                Address1 = row.Text("billing_address_line1"), Address2 = row.Text("billing_address_line2"), City = row.Text("billing_address_city"),
                State = row.Text("billing_address_state_or_province"), Zip = row.Text("billing_address_postal_code"), Country = row.Text("billing_address_country"),
                ShipTo = Address(row, "ship_to_address_"),
                WorkPhone = row.Text("contact_phone"), Extension = row.Text("contact_extension"), Fax = row.Text("contact_fax"),
                HomePhone = row.Text("contact_mobile"), Email = row.Text("contact_email"), Website = row.Text("contact_website"),
                Number = unique, AccountNumber = uniqueAccount, Type = row.Text("client_type"), Notes = row.Text("notes"),
                Birthday = row.Date("birthday"), LastPurchase = row.Date("last_purchase_on"), LastMailed = row.Date("last_mailing_on"),
                Points = Three(points, "Client.Points"),
                UsualDiscountPercent = Three(profile?.Dec("usual_discount_pct") ?? 0, "Client.UsualDiscountPercent"),
                ExemptTax1 = profile?.Bool("exempt_tax1") ?? false, ExemptTax2 = profile?.Bool("exempt_tax2") ?? false,
                // Retail25's level 1 is the regular price, which is level 0 here.
                PriceLevel = level is >= 2 and <= 4 ? level : 0,
                CreditLimit = Money(account?.Dec("credit_limit") ?? 0, "Client.CreditLimit"),
                Balance = invoiceOwing.GetValueOrDefault(row.Id) + layawayOwing.GetValueOrDefault(row.Id),
                IsDeleted = deleted,
                DeletedAt = deleted && row.Instant("deleted_at") is { } at ? (store is null ? Seconds(at.LocalDateTime) : Local(store, at)) : null,
                CreatedAtUtc = Utc(row.Instant("created_at")),
            }, row);
            clients.Add(client);
            _clients[row.Id] = new CarriedClient(client.Id, client, row);
        }
        await _w.UpsertAsync(area, clients, ct, merge: (stored, mapped) => mapped.IsFlagged |= stored.IsFlagged);
    }
}
