using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Retail25.Domain.Configuration;
using Retail25.Domain.Terminals;
using Retail25.Domain.ValueObjects;

namespace Retail25.Infrastructure.Persistence.Seeding;

/// <summary>
/// Puts the minimum configuration in place for a store to trade.
/// <para>
/// Nothing in the engine has a fallback for missing configuration: <c>CartPricingService</c> refuses
/// to price a sale when no tax configuration is in effect, rather than guessing a rate. That is the
/// right behaviour, and it makes this seeder a hard requirement for a usable database rather than a
/// convenience.
/// </para>
/// <para>
/// Every value written here is data an administrator can change afterwards. The seeder only fills
/// gaps — it never overwrites something that already exists, so it is safe to run on every start.
/// </para>
/// </summary>
public sealed class ReferenceDataSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ReferenceDataSeeder> _logger;

    public ReferenceDataSeeder(ApplicationDbContext db, ILogger<ReferenceDataSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <param name="options">Which store the defaults describe.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SeedAsync(SeedOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var currency = await SeedCurrencyAsync(options, ct);
        var location = await SeedLocationAsync(options, currency, ct);

        await SeedPriceLevelsAsync(ct);
        await SeedTaxConfigurationAsync(options, location, ct);
        await SeedPosPolicyAsync(location, ct);
        await SeedTenderTypesAsync(ct);
        await SeedLoyaltyPolicyAsync(location, ct);
        await SeedStationAsync(options, location, ct);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Reference data seeding complete for location {LocationCode}.", options.LocationCode);
    }

    private async Task<Currency> SeedCurrencyAsync(SeedOptions options, CancellationToken ct)
    {
        var existing = await _db.Currencies.FirstOrDefaultAsync(c => c.Code == options.CurrencyCode, ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = Currency.Create(
            code: options.CurrencyCode,
            name: options.CurrencyName,
            symbol: options.CurrencySymbol,
            scale: options.CurrencyScale,
            rounding: RoundingMode.AwayFromZero,
            minimumTender: options.MinimumTender,
            isBaseCurrency: true).Value;

        _db.Currencies.Add(created);
        _logger.LogInformation("Seeded base currency {Code}.", created.Code);
        return created;
    }

    private async Task<Location> SeedLocationAsync(SeedOptions options, Currency currency, CancellationToken ct)
    {
        var existing = await _db.Locations.FirstOrDefaultAsync(l => l.LegacyCode == options.LocationCode, ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = Location.Create(
            name: options.LocationName,
            legacyCode: options.LocationCode,
            baseCurrencyCode: currency.Code,
            timeZoneId: options.TimeZoneId,
            businessDayStart: options.BusinessDayStart).Value;

        _db.Locations.Add(created);
        _logger.LogInformation("Seeded location {Code} — {Name}.", created.LegacyCode, created.Name);
        return created;
    }

    /// <summary>
    /// Names the four price columns. The legacy system left the meaning of "price 3" to the
    /// shopkeeper's memory; naming them makes the customer record and the price grid legible.
    /// </summary>
    private async Task SeedPriceLevelsAsync(CancellationToken ct)
    {
        if (await _db.PriceLevelDefinitions.AnyAsync(ct))
        {
            return;
        }

        var levels = new (int Level, string Name, string Description)[]
        {
            (1, "Daily Customer", "Walk-in retail price. The default for anyone without an assigned level."),
            (2, "Retailer", "Trade price for retail buyers."),
            (3, "Wholesaler", "Wholesale price for volume buyers."),
            (4, "Distributor", "Lowest tier, for distribution partners."),
        };

        foreach (var (level, name, description) in levels)
        {
            _db.PriceLevelDefinitions.Add(PriceLevelDefinition.Create(level, name, description).Value);
        }

        _logger.LogInformation("Seeded {Count} price level definitions.", levels.Length);
    }

    private async Task SeedTaxConfigurationAsync(SeedOptions options, Location location, CancellationToken ct)
    {
        if (await _db.TaxConfigurations.AnyAsync(t => t.LocationId == location.Id, ct))
        {
            return;
        }

        var configuration = TaxConfiguration.Create(
            locationId: location.Id,
            effectiveFrom: options.TaxEffectiveFrom,
            tax1Enabled: options.Tax1Rate > 0m,
            tax1Name: options.Tax1Name,
            tax1Rate: new Percentage(options.Tax1Rate),
            tax2Enabled: options.Tax2Rate > 0m,
            tax2Name: options.Tax2Name,
            tax2Rate: new Percentage(options.Tax2Rate),
            tax2Compound: options.Tax2Compound,
            addOnChargeEnabled: false,
            addOnChargeName: "Service charge",
            addOnChargeRate: Percentage.Zero,
            addOnChargeTaxable: false,
            taxationType: options.TaxationType,
            registrationNumber: null).Value;

        _db.TaxConfigurations.Add(configuration);
        _logger.LogInformation(
            "Seeded tax configuration effective {Date}: {Tax1} {Rate1}%, {Tax2} {Rate2}%.",
            options.TaxEffectiveFrom, options.Tax1Name, options.Tax1Rate, options.Tax2Name, options.Tax2Rate);
    }

    private async Task SeedPosPolicyAsync(Location location, CancellationToken ct)
    {
        if (await _db.PosPolicies.AnyAsync(p => p.LocationId == location.Id, ct))
        {
            return;
        }

        _db.PosPolicies.Add(PosPolicy.CreateDefault(location.Id));
        _logger.LogInformation("Seeded default POS policy.");
    }

    /// <summary>
    /// The ways of paying a store starts with. Each is a row: capabilities, ordering and the
    /// accounting key are all editable, and new tenders need no code.
    /// </summary>
    private async Task SeedTenderTypesAsync(CancellationToken ct)
    {
        if (await _db.TenderTypes.AnyAsync(ct))
        {
            return;
        }

        var tenders = new (string Code, string Name, TenderBehaviour Behaviour, string Icon)[]
        {
            ("CASH", "Cash", TenderBehaviour.Cash, "banknote"),
            ("CREDIT", "Credit Card", TenderBehaviour.Card, "credit-card"),
            ("DEBIT", "Debit Card", TenderBehaviour.Card, "credit-card"),
            ("GIFTCARD", "Gift Card", TenderBehaviour.GiftCard, "gift"),
            ("GIFTCERT", "Gift Certificate", TenderBehaviour.GiftCertificate, "ticket"),
            ("CHEQUE", "Cheque", TenderBehaviour.Manual, "file-text"),
            ("ACCOUNT", "On Account", TenderBehaviour.OnAccount, "user"),
        };

        var order = 0;
        foreach (var (code, name, behaviour, icon) in tenders)
        {
            var tender = TenderType.Create(code, name, behaviour, order += 10, icon).Value;
            tender.MapToAccounting(name);
            _db.TenderTypes.Add(tender);
        }

        _logger.LogInformation("Seeded {Count} tender types.", tenders.Length);
    }

    private async Task SeedLoyaltyPolicyAsync(Location location, CancellationToken ct)
    {
        if (await _db.LoyaltyPolicies.AnyAsync(l => l.LocationId == location.Id, ct))
        {
            return;
        }

        // Off by default: a store that has not asked for a points scheme should not silently
        // start accruing liabilities.
        _db.LoyaltyPolicies.Add(LoyaltyPolicy.CreateDisabled(location.Id));
        _logger.LogInformation("Seeded loyalty policy (disabled).");
    }

    private async Task SeedStationAsync(SeedOptions options, Location location, CancellationToken ct)
    {
        if (await _db.Stations.AnyAsync(s => s.LocationId == location.Id, ct))
        {
            return;
        }

        _db.Stations.Add(Station.Create(location.Id, options.StationCode, options.StationName).Value);
        _logger.LogInformation("Seeded station {Code}.", options.StationCode);
    }
}
