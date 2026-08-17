using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Retail25.Application.Abstractions;
using Retail25.Domain.Configuration;
using Retail25.Domain.Terminals;
using Retail25.Domain.ValueObjects;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Seeds the working defaults a store needs before it can ring anything up.
/// <para>
/// The standing constraint is that no rule is compiled in â€” but a database of empty configuration
/// tables is not usable either. This writes a defensible starting point (a location, a currency, the
/// documented pricing ladder, tax rows, the standard tenders, one station and its peripherals) that
/// an administrator then edits. It is idempotent, so it is safe to run on every start.
/// </para>
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(ApplicationDbContext db, IDateTime clock, ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var currency = await SeedCurrencyAsync(ct);
        var location = await SeedLocationAsync(currency.Code, ct);

        await SeedTaxAsync(location.Id, ct);
        await SeedPolicyAsync(location.Id, ct);
        await SeedPricingLadderAsync(location.Id, ct);
        await SeedNumberingAsync(location.Id, ct);
        await SeedLoyaltyAsync(location.Id, ct);
        await SeedTendersAsync(ct);
        await SeedStationAsync(location.Id, ct);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seed complete for location {LocationCode}", location.LegacyCode);
    }

    private async Task<Currency> SeedCurrencyAsync(CancellationToken ct)
    {
        var existing = await _db.Currencies.FirstOrDefaultAsync(c => c.IsBaseCurrency, ct);
        if (existing is not null)
        {
            return existing;
        }

        // Penny tendering, away from zero. A store that abolished the penny sets MinimumTender to
        // 0.05 in the settings UI and the engine follows without a code change (decision P4).
        var created = Currency.Create("CAD", "Canadian Dollar", "$", 2, RoundingMode.AwayFromZero, 0.01m, true).Value;
        _db.Currencies.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<Location> SeedLocationAsync(string currencyCode, CancellationToken ct)
    {
        var existing = await _db.Locations.FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = Location.Create("Main Store", "TST", currencyCode, TimeZoneInfo.Local.Id, TimeOnly.MinValue).Value;
        _db.Locations.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private async Task SeedTaxAsync(long locationId, CancellationToken ct)
    {
        if (await _db.TaxConfigurations.AnyAsync(t => t.LocationId == locationId, ct))
        {
            return;
        }

        // Two named taxes, non-compound, exclusive â€” the shape the legacy Setup screen models
        // (guide p.76â€“77). Names and rates are data; this is only a starting point.
        var created = TaxConfiguration.Create(
            locationId,
            DateOnly.FromDateTime(_clock.Now.Date).AddYears(-1),
            tax1Enabled: true,
            tax1Name: "GST",
            tax1Rate: new Percentage(5m),
            tax2Enabled: true,
            tax2Name: "PST",
            tax2Rate: new Percentage(7m),
            tax2Compound: false,
            addOnChargeEnabled: false,
            addOnChargeName: "Service",
            addOnChargeRate: Percentage.Zero,
            addOnChargeTaxable: false,
            taxationType: TaxationType.Exclusive,
            registrationNumber: null);

        _db.TaxConfigurations.Add(created.Value);
    }

    private async Task SeedPolicyAsync(long locationId, CancellationToken ct)
    {
        if (await _db.PosPolicies.AnyAsync(p => p.LocationId == locationId, ct))
        {
            return;
        }

        _db.PosPolicies.Add(PosPolicy.CreateDefault(locationId));
    }

    private async Task SeedPricingLadderAsync(long locationId, CancellationToken ct)
    {
        if (await _db.PricingRuleSettings.AnyAsync(r => r.LocationId == locationId, ct))
        {
            return;
        }

        // The documented order from doc 04 Â§2. Reordering these two rows is what implements the
        // alternative to decision P1 â€” no code change (README, standing build constraint).
        _db.PricingRuleSettings.AddRange(PricingRuleSetting.SeedDefaults(locationId));
    }

    /// <summary>
    /// The legacy "next number" settings (guide p.76), starting at 1 for a fresh store.
    /// <para>
    /// A migrated store overwrites these from its own counters before the first sale â€” which is the
    /// whole reason they are rows. Customer 4,182 has to be followed by 4,183, because staff and
    /// paper records refer to those numbers.
    /// </para>
    /// </summary>
    private async Task SeedNumberingAsync(long locationId, CancellationToken ct)
    {
        var existing = await _db.NumberSequences
            .Where(s => s.LocationId == locationId)
            .Select(s => s.Kind)
            .ToListAsync(ct);

        var missing = NumberSequence.SeedDefaults(locationId).Where(s => !existing.Contains(s.Kind)).ToList();

        if (missing.Count > 0)
        {
            _db.NumberSequences.AddRange(missing);
        }
    }

    private async Task SeedLoyaltyAsync(long locationId, CancellationToken ct)
    {
        if (await _db.LoyaltyPolicies.AnyAsync(l => l.LocationId == locationId, ct))
        {
            return;
        }

        _db.LoyaltyPolicies.Add(new LoyaltyPolicy
        {
            LocationId = locationId,
            IsEnabled = false,
            PointsPerDollar = 1m,
            MinimumRequired = 500,
            PercentEnabled = true,
            RewardPercent = 5m,
            FixedEnabled = false,
            RewardFixedAmount = 0m,
            SuppressIfSubtotalDiscountApplied = true,
        });
    }

    private async Task SeedTendersAsync(CancellationToken ct)
    {
        if (await _db.TenderTypes.AnyAsync(ct))
        {
            return;
        }

        var tenders = new[]
        {
            TenderType.Create("CASH", "Cash", TenderBehaviour.Cash, 10, "banknote").Value,
            TenderType.Create("CREDIT", "Credit", TenderBehaviour.Card, 20, "credit-card").Value,
            TenderType.Create("DEBIT", "Debit", TenderBehaviour.Card, 30, "credit-card").Value,
            TenderType.Create("GIFTCARD", "Gift card", TenderBehaviour.GiftCard, 40, "gift").Value,
            TenderType.Create("GIFTCERT", "Gift certificate", TenderBehaviour.GiftCertificate, 50, "ticket").Value,
            TenderType.Create("CHEQUE", "Cheque", TenderBehaviour.Manual, 60, "file-text").Value,
            TenderType.Create("ONACCT", "On account", TenderBehaviour.OnAccount, 70, "user").Value,
        };

        _db.TenderTypes.AddRange(tenders);
    }

    private async Task SeedStationAsync(long locationId, CancellationToken ct)
    {
        if (await _db.Stations.AnyAsync(s => s.LocationId == locationId, ct))
        {
            return;
        }

        var station = Station.Create(locationId, "001", "Front counter").Value;

        var printer = PrinterProfile.CreateDefault(locationId);
        var reader = ReaderProfile.CreateDefault(locationId);
        var scale = ScaleProfile.CreateDefault(locationId);
        var pole = PoleDisplayProfile.CreateDefault(locationId);

        _db.PrinterProfiles.Add(printer);
        _db.ReaderProfiles.Add(reader);
        _db.ScaleProfiles.Add(scale);
        _db.PoleDisplayProfiles.Add(pole);

        // Saved before the station is told about them.
        //
        // The profiles' ids are assigned by the database, so reading them here without saving first
        // wires the station to profile 0 four times over â€” and nothing objects, because a station's
        // peripheral columns are nullable references with no constraint behind them. The till would
        // simply come up with no printer, no reader, no scale and no pole display, and the reason
        // would be invisible in the data.
        await _db.SaveChangesAsync(ct);

        station.AssignPeripherals(printer.Id, reader.Id, scale.Id, pole.Id);
        _db.Stations.Add(station);
    }

}
