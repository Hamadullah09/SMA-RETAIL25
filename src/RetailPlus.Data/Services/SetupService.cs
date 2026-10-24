using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;

namespace RetailPlus.Data.Services;

/// <summary>Setup screen data: store settings, locations, stations, users, tender types, lists and texts.</summary>
public sealed class SetupService(DataContext data)
{
    public static readonly (string Name, TenderKind Kind)[] DefaultTenders =
    [
        ("Cash", TenderKind.Cash), ("Visa", TenderKind.Card), ("MasterCard", TenderKind.Card), ("Amex", TenderKind.Card),
        ("Debit", TenderKind.Card), ("Cheque", TenderKind.Cheque), ("Gift Card", TenderKind.GiftCard), ("On Account", TenderKind.OnAccount),
    ];

    /// <summary>Creates the minimum a new database needs: the settings row, a location, tender types and the station.</summary>
    public async Task SeedAsync(int stationNumber, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        if (!await db.StoreSettings.AnyAsync(cancellationToken))
            db.StoreSettings.Add(new StoreSettings { Id = 1, CompanyName = "My Store", Tax1Name = "Tax 1", Tax2Name = "Tax 2", MinimumTender = 0.01m, GracePeriodDays = 30 });
        if (!await db.Locations.AnyAsync(cancellationToken))
            db.Locations.Add(new Location { Code = "POS", Name = "Main store" });
        if (!await db.TenderTypes.AnyAsync(cancellationToken))
        {
            int order = 0;
            foreach (var (name, kind) in DefaultTenders)
                db.TenderTypes.Add(new TenderType { Name = name, Kind = kind, SortOrder = ++order, OpensDrawer = kind is TenderKind.Cash or TenderKind.Cheque });
        }
        if (!await db.Stations.AnyAsync(s => s.Number == stationNumber, cancellationToken))
            db.Stations.Add(new Station { Number = stationNumber, Name = $"Station {stationNumber:000}" });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasDataAsync(CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        return await db.Items.AnyAsync(cancellationToken) || await db.Clients.AnyAsync(cancellationToken);
    }

    // Store settings ------------------------------------------------------------------------------------------

    public async Task<StoreSettings> GetSettingsAsync()
    {
        await using var db = data.Create();
        return await db.StoreSettings.AsNoTracking().SingleOrDefaultAsync() ?? new StoreSettings { Id = 1 };
    }

    public async Task SaveSettingsAsync(StoreSettings settings)
    {
        await using var db = data.Create();
        settings.Id = 1;
        if (await db.StoreSettings.AnyAsync()) db.StoreSettings.Update(settings);
        else db.StoreSettings.Add(settings);
        await db.SaveChangesAsync();
    }

    // Locations and stations ------------------------------------------------------------------------------------

    public async Task<List<Location>> GetLocationsAsync()
    {
        await using var db = data.Create();
        return await db.Locations.AsNoTracking().OrderBy(l => l.Code).ToListAsync();
    }

    public async Task<Location> AddLocationAsync(string code, string? name)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length is < 1 or > 3) throw new BusinessRuleException("A location code has 1 to 3 characters.");
        await using var db = data.Create();
        if (await db.Locations.AnyAsync(l => l.Code == code)) throw new BusinessRuleException($"Location {code} already exists.");
        var location = new Location { Code = code, Name = name };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location;
    }

    public async Task<Dictionary<string, string>> GetStationSettingsAsync(int stationNumber)
    {
        await using var db = data.Create();
        return await db.StationSettings.AsNoTracking()
            .Where(s => db.Stations.Any(st => st.Id == s.StationId && st.Number == stationNumber))
            .ToDictionaryAsync(s => s.Section + "/" + s.Key, s => s.Value ?? "", StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetStationSettingAsync(int stationNumber, string section, string key, string? value)
    {
        await using var db = data.Create();
        var station = await db.Stations.SingleOrDefaultAsync(s => s.Number == stationNumber)
                      ?? db.Stations.Add(new Station { Number = stationNumber, Name = $"Station {stationNumber:000}" }).Entity;
        await db.SaveChangesAsync();
        var row = await db.StationSettings.SingleOrDefaultAsync(s => s.StationId == station.Id && s.Section == section && s.Key == key);
        if (row is null) db.StationSettings.Add(new StationSetting { StationId = station.Id, Section = section, Key = key, Value = value });
        else row.Value = value;
        await db.SaveChangesAsync();
    }

    // Users ------------------------------------------------------------------------------------------------------

    public async Task<List<UserAccount>> GetUsersAsync()
    {
        await using var db = data.Create();
        return await db.UserAccounts.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();
    }

    public async Task SaveUserAsync(UserAccount user, string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(user.UserName)) throw new BusinessRuleException("Enter a user name.");
        if (user.AccessLevel is < 0 or > 4) throw new BusinessRuleException("Access levels are 0 to 4.");
        await using var db = data.Create();
        var existing = user.Id == 0 ? null : await db.UserAccounts.FindAsync(user.Id);
        if (existing is null)
        {
            existing = new UserAccount();
            db.UserAccounts.Add(existing);
        }
        existing.UserName = user.UserName.Trim();
        existing.AccessLevel = user.AccessLevel;
        existing.MustChangePassword = user.MustChangePassword;
        if (!string.IsNullOrEmpty(newPassword))
        {
            existing.PasswordHash = HashPassword(newPassword);
            existing.MustChangePassword = false;
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteUserAsync(int id)
    {
        await using var db = data.Create();
        await db.UserAccounts.Where(u => u.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Returns the user when the password matches; null otherwise.</summary>
    public async Task<UserAccount?> AuthenticateAsync(string userName, string password)
    {
        await using var db = data.Create();
        var user = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(u => u.UserName == userName.Trim());
        if (user is null) return null;
        if (user.PasswordHash is null) return user.MustChangePassword ? user : null;
        return VerifyPassword(password, user.PasswordHash) ? user : null;
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        var iterations = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // Lists ------------------------------------------------------------------------------------------------------

    public async Task<List<TenderType>> GetTenderTypesAsync(bool activeOnly = true)
    {
        await using var db = data.Create();
        return await db.TenderTypes.AsNoTracking().Where(t => !activeOnly || t.IsActive).OrderBy(t => t.SortOrder).ToListAsync();
    }

    public async Task SaveTenderTypesAsync(IEnumerable<TenderType> tenders)
    {
        await using var db = data.Create();
        foreach (var t in tenders)
        {
            if (string.IsNullOrWhiteSpace(t.Name)) continue;
            if (t.Id == 0) db.TenderTypes.Add(t);
            else db.TenderTypes.Update(t);
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<string>> GetDepartmentsAsync()
    {
        await using var db = data.Create();
        return await db.Departments.OrderBy(d => d.Name).Select(d => d.Name).ToListAsync();
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        await using var db = data.Create();
        return await db.Categories.OrderBy(d => d.Name).Select(d => d.Name).ToListAsync();
    }

    public async Task AddDepartmentAsync(string name)
    {
        name = name.Trim().ToUpperInvariant();
        if (name.Length == 0) return;
        await using var db = data.Create();
        if (!await db.Departments.AnyAsync(d => d.Name == name)) db.Departments.Add(new Department { Name = name });
        await db.SaveChangesAsync();
    }

    public async Task AddCategoryAsync(string name)
    {
        name = name.Trim().ToUpperInvariant();
        if (name.Length == 0) return;
        await using var db = data.Create();
        if (!await db.Categories.AnyAsync(d => d.Name == name)) db.Categories.Add(new Category { Name = name });
        await db.SaveChangesAsync();
    }

    public async Task DeleteDepartmentAsync(string name)
    {
        await using var db = data.Create();
        var dept = await db.Departments.SingleOrDefaultAsync(d => d.Name == name);
        if (dept is null) return;
        await db.Items.Where(i => i.DepartmentId == dept.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.DepartmentId, (int?)null));
        db.Departments.Remove(dept);
        await db.SaveChangesAsync();
    }

    public async Task DeleteCategoryAsync(string name)
    {
        await using var db = data.Create();
        var cat = await db.Categories.SingleOrDefaultAsync(d => d.Name == name);
        if (cat is null) return;
        await db.Items.Where(i => i.CategoryId == cat.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.CategoryId, (int?)null));
        db.Categories.Remove(cat);
        await db.SaveChangesAsync();
    }

    public async Task<List<TaxConfiguration>> GetTaxConfigurationsAsync()
    {
        await using var db = data.Create();
        return await db.TaxConfigurations.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
    }

    public async Task SaveTaxConfigurationsAsync(IEnumerable<TaxConfiguration> configurations)
    {
        var list = configurations.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        if (list.Count > 12) throw new BusinessRuleException("You can enter up to 12 tax configurations.");
        await using var db = data.Create();
        await db.TaxConfigurations.ExecuteDeleteAsync();
        foreach (var c in list) db.TaxConfigurations.Add(new TaxConfiguration { Name = c.Name, Tax1Name = c.Tax1Name, Tax1Rate = c.Tax1Rate, Tax2Name = c.Tax2Name, Tax2Rate = c.Tax2Rate });
        await db.SaveChangesAsync();
    }

    public async Task<List<FxRate>> GetFxRatesAsync()
    {
        await using var db = data.Create();
        return await db.FxRates.AsNoTracking().OrderBy(r => r.Currency).ToListAsync();
    }

    public async Task SaveFxRatesAsync(IEnumerable<FxRate> rates)
    {
        await using var db = data.Create();
        await db.FxRates.ExecuteDeleteAsync();
        foreach (var r in rates.Where(r => !string.IsNullOrWhiteSpace(r.Currency) && r.Rate > 0))
            db.FxRates.Add(new FxRate { Currency = r.Currency.Trim().ToUpperInvariant(), Rate = r.Rate });
        await db.SaveChangesAsync();
    }

    public async Task<string> GetTextAsync(DocumentTextKind kind, int? locationId = null)
    {
        await using var db = data.Create();
        return await db.DocumentTexts.Where(t => t.Kind == kind && t.LocationId == locationId).Select(t => t.Text).SingleOrDefaultAsync() ?? "";
    }

    public async Task SetTextAsync(DocumentTextKind kind, string text, int? locationId = null)
    {
        await using var db = data.Create();
        var row = await db.DocumentTexts.SingleOrDefaultAsync(t => t.Kind == kind && t.LocationId == locationId);
        if (row is null) db.DocumentTexts.Add(new DocumentText { Kind = kind, LocationId = locationId, Text = text });
        else row.Text = text;
        await db.SaveChangesAsync();
    }

    public async Task<List<FastKey>> GetFastKeysAsync(int panel)
    {
        await using var db = data.Create();
        return await db.FastKeys.AsNoTracking().Where(k => k.Panel == panel).OrderBy(k => k.KeyNumber).ToListAsync();
    }

    public async Task SaveFastKeyAsync(FastKey key)
    {
        await using var db = data.Create();
        var existing = await db.FastKeys.SingleOrDefaultAsync(k => k.Panel == key.Panel && k.KeyNumber == key.KeyNumber);
        if (existing is null) db.FastKeys.Add(key);
        else
        {
            existing.Name = key.Name; existing.Plu = key.Plu; existing.FixedPrice = key.FixedPrice; existing.IsCredit = key.IsCredit;
            existing.Taxable1 = key.Taxable1; existing.Taxable2 = key.Taxable2; existing.IsCategory = key.IsCategory; existing.Color = key.Color;
        }
        await db.SaveChangesAsync();
    }
}
