using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>A row of the Clients or Suppliers browse list.</summary>
public sealed record PartyRow(int Id, string? Number, string? Company, string? LastName, string? FirstName, string? City, string? WorkPhone,
    string? Email, decimal Balance, decimal Points, DateOnly? LastPurchase, bool IsFlagged, bool IsDeleted)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(Company) ? Company! : $"{LastName}, {FirstName}".Trim(' ', ',');
}

/// <summary>Clients and Suppliers windows.</summary>
public sealed class PartyService(DataContext data)
{
    public async Task<List<PartyRow>> SearchClientsAsync(string? text, bool includeDeleted = false, bool flaggedOnly = false, int take = 2000)
    {
        await using var db = data.Create();
        var q = db.Clients.AsNoTracking().AsQueryable();
        if (!includeDeleted) q = q.Where(c => !c.IsDeleted);
        if (flaggedOnly) q = q.Where(c => c.IsFlagged);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim();
            q = q.Where(c => (c.LastName ?? "").StartsWith(t) || (c.FirstName ?? "").StartsWith(t) || (c.Company ?? "").StartsWith(t)
                             || (c.Number ?? "").StartsWith(t) || (c.AccountNumber ?? "").StartsWith(t) || (c.WorkPhone ?? "").Contains(t)
                             || (c.HomePhone ?? "").Contains(t) || (c.Email ?? "").StartsWith(t));
        }
        return await q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Company).Take(take)
            .Select(c => new PartyRow(c.Id, c.Number, c.Company, c.LastName, c.FirstName, c.City, c.WorkPhone ?? c.HomePhone, c.Email, c.Balance, c.Points, c.LastPurchase, c.IsFlagged, c.IsDeleted))
            .ToListAsync();
    }

    public async Task<List<PartyRow>> SearchSuppliersAsync(string? text, bool includeDeleted = false, int take = 2000)
    {
        await using var db = data.Create();
        var q = db.Suppliers.AsNoTracking().AsQueryable();
        if (!includeDeleted) q = q.Where(c => !c.IsDeleted);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim();
            q = q.Where(c => (c.Company ?? "").StartsWith(t) || (c.LastName ?? "").StartsWith(t) || (c.Number ?? "").StartsWith(t) || (c.AccountNumber ?? "").StartsWith(t));
        }
        return await q.OrderBy(c => c.Company).ThenBy(c => c.LastName).Take(take)
            .Select(c => new PartyRow(c.Id, c.Number, c.Company, c.LastName, c.FirstName, c.City, c.WorkPhone, c.Email, 0, 0, c.LastPurchase, c.IsFlagged, c.IsDeleted))
            .ToListAsync();
    }

    public async Task<List<string>> GetSupplierNamesAsync()
    {
        await using var db = data.Create();
        return await db.Suppliers.Where(s => !s.IsDeleted && s.Company != null).OrderBy(s => s.Company).Select(s => s.Company!).Distinct().ToListAsync();
    }

    public async Task<Client?> GetClientAsync(int id)
    {
        await using var db = data.Create();
        return await db.Clients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Client?> FindClientByNumberAsync(string number)
    {
        await using var db = data.Create();
        number = number.Trim();
        return await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => !c.IsDeleted && (c.Number == number || c.AccountNumber == number));
    }

    public async Task<Supplier?> GetSupplierAsync(int id)
    {
        await using var db = data.Create();
        return await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>Saves a client; a new client without a customer number gets the next one.</summary>
    public async Task<Client> SaveClientAsync(Client client)
    {
        Validate(client);
        await using var db = data.Create();
        if (string.IsNullOrWhiteSpace(client.Number))
            client.Number = (await db.NextNumberAsync(RetailPlusDbContext.Sequences.CustomerNumbers)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (await db.Clients.AnyAsync(c => c.Number == client.Number && c.Id != client.Id && !c.IsDeleted))
            throw new BusinessRuleException($"Customer number {client.Number} is already used.");
        if (!string.IsNullOrWhiteSpace(client.AccountNumber) && await db.Clients.AnyAsync(c => c.AccountNumber == client.AccountNumber && c.Id != client.Id && !c.IsDeleted))
            throw new BusinessRuleException($"Account number {client.AccountNumber} is already used.");
        if (string.IsNullOrWhiteSpace(client.AccountNumber)) client.AccountNumber = null;
        if (client.Id == 0) db.Clients.Add(client); else db.Clients.Update(client);
        await SaveAsync(db, "client");
        return client;
    }

    public async Task<Supplier> SaveSupplierAsync(Supplier supplier)
    {
        Validate(supplier);
        await using var db = data.Create();
        if (string.IsNullOrWhiteSpace(supplier.Number)) supplier.Number = null;
        if (string.IsNullOrWhiteSpace(supplier.AccountNumber)) supplier.AccountNumber = null;
        if (supplier.Id == 0) db.Suppliers.Add(supplier); else db.Suppliers.Update(supplier);
        await SaveAsync(db, "supplier");
        return supplier;
    }

    private static void Validate(Party p)
    {
        if (string.IsNullOrWhiteSpace(p.Company) && string.IsNullOrWhiteSpace(p.LastName) && string.IsNullOrWhiteSpace(p.FirstName))
            throw new BusinessRuleException("Enter a name or a company.");
    }

    private static async Task SaveAsync(RetailPlusDbContext db, string what)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException($"This {what} was changed at another station. Reload it and make your changes again.");
        }
    }

    public async Task DeleteClientAsync(int id)
    {
        await using var db = data.Create();
        if (await db.Clients.AnyAsync(c => c.Id == id && c.Balance != 0))
            throw new BusinessRuleException("This client still has a balance owing.");
        await db.Clients.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true).SetProperty(c => c.DeletedAt, DateTime.Now));
    }

    public async Task DeleteSupplierAsync(int id)
    {
        await using var db = data.Create();
        await db.Suppliers.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDeleted, true).SetProperty(c => c.DeletedAt, DateTime.Now));
    }

    public async Task SetClientFlagAsync(IEnumerable<int> ids, bool flagged)
    {
        var list = ids.ToList();
        await using var db = data.Create();
        await db.Clients.Where(c => list.Contains(c.Id)).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsFlagged, flagged));
    }

    /// <summary>The client's purchases, newest first (the History tab).</summary>
    public async Task<List<SaleLine>> GetPurchaseHistoryAsync(int clientId, int take = 500)
    {
        await using var db = data.Create();
        return await db.SaleLines.AsNoTracking()
            .Where(l => db.Sales.Any(s => s.Id == l.SaleId && s.ClientId == clientId && s.Status == SaleStatus.Completed))
            .OrderByDescending(l => l.SaleId).ThenBy(l => l.LineNumber).Take(take).ToListAsync();
    }

    /// <summary>Clients whose birthday falls in a month (for mailing lists).</summary>
    public async Task<List<PartyRow>> BirthdaysAsync(int month)
    {
        await using var db = data.Create();
        return await db.Clients.AsNoTracking().Where(c => !c.IsDeleted && c.Birthday != null && c.Birthday.Value.Month == month)
            .Select(c => new PartyRow(c.Id, c.Number, c.Company, c.LastName, c.FirstName, c.City, c.WorkPhone, c.Email, c.Balance, c.Points, c.LastPurchase, c.IsFlagged, c.IsDeleted))
            .ToListAsync();
    }

    /// <summary>
    /// Recurring billing: creates an on-account invoice for each active client whose next billing date has come,
    /// then moves the next date on. Returns the number of invoices created.
    /// </summary>
    public async Task<int> RunRecurringBillingAsync(int locationId, DateOnly today, InvoiceService invoices)
    {
        await using var db = data.Create();
        var due = await db.Clients.Where(c => !c.IsDeleted && c.RecurringActive && c.RecurringNextBilling != null && c.RecurringNextBilling <= today && c.RecurringItemCode != null).ToListAsync();
        int created = 0;
        foreach (var client in due)
        {
            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.LocationId == locationId && i.Plu == client.RecurringItemCode && !i.IsDeleted);
            if (item is null) continue;
            await invoices.CreateAsync(client.Id, InvoiceKind.Invoice, today, [new InvoiceLine { Plu = item.Plu, Description = item.Name, Quantity = 1, UnitPrice = item.Price1, LineTotal = item.Price1 }], "Recurring billing");
            client.RecurringLastBilled = today;
            client.RecurringTimesBilled++;
            var next = client.RecurringNextBilling!.Value;
            int every = Math.Max(1, client.RecurringEvery);
            client.RecurringNextBilling = (client.RecurringUnit ?? "Months").StartsWith('D') ? next.AddDays(every)
                : (client.RecurringUnit ?? "").StartsWith('W') ? next.AddDays(7 * every) : next.AddMonths(every);
            if (client.RecurringMaxBillings > 0 && client.RecurringTimesBilled >= client.RecurringMaxBillings) client.RecurringActive = false;
            created++;
        }
        await db.SaveChangesAsync();
        return created;
    }
}
