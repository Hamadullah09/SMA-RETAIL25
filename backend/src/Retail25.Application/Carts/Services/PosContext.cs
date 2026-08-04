using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Sales.Pricing;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Carts.Services;

/// <summary>
/// Every configuration row a till needs for one request, resolved once. Loading these individually
/// inside a handler is how a 120 ms quote budget gets spent on round trips.
/// </summary>
public sealed record PosContext(
    Location Location,
    Station Station,
    PosPolicy Policy,
    TaxConfiguration Tax,
    LoyaltyPolicy? Loyalty,
    Currency Currency,
    IReadOnlyList<PricingRuleSetting> Rules,
    DateOnly BusinessDate)
{
    public MoneyRounding Rounding => MoneyRounding.FromCurrency(Currency);

    /// <summary>Station overrides win over the store policy; a null override defers (guide p.77–78).</summary>
    public bool FastScanMode => Station.FastScanMode ?? Policy.FastScanMode;

    public bool AutoSaveSales => Station.AutoSaveSales ?? Policy.AutoSaveSales;

    public bool ConfirmBeforeSaving => Station.ConfirmBeforeSaving ?? Policy.ConfirmBeforeSavingSales;

    public bool ScanRandomWeightBarcodes => Station.ScanRandomWeightBarcodes ?? Policy.ScanRandomWeightBarcodes;

    public long? DefaultTenderTypeId => Station.DefaultTenderTypeId ?? Policy.DefaultTenderTypeId;
}

/// <summary>
/// Loads a <see cref="PosContext"/> for a station. Everything it reads is configuration, so it is a
/// natural caching seam later; for now correctness beats a cache nobody has measured the need for.
/// </summary>
public sealed class PosContextLoader
{
    public static readonly Error StationNotFound = new("station.not_found", "That station is not registered.");
    public static readonly Error LocationNotFound = new("location.not_found", "The station's location no longer exists.");
    public static readonly Error TaxNotConfigured = new("tax.not_configured", "No tax configuration is effective for this business date.");
    public static readonly Error CurrencyNotConfigured = new("currency.not_configured", "The location has no base currency configured.");

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public PosContextLoader(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<PosContext>> LoadAsync(long stationId, CancellationToken ct)
    {
        var station = await _db.Stations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == stationId, ct);
        if (station is null)
        {
            return Result.Failure<PosContext>(StationNotFound.With("stationId", stationId));
        }

        return await LoadForStationAsync(station, ct);
    }

    public async Task<Result<PosContext>> LoadForStationAsync(Station station, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(station);

        var location = await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == station.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<PosContext>(LocationNotFound.With("locationId", station.LocationId));
        }

        // The trading day, not the server day: a sale rung at 00:30 belongs to the day the store says.
        var businessDate = location.BusinessDateFor(_clock.Now);

        var tax = await _db.TaxConfigurations
            .AsNoTracking()
            .Where(t => t.LocationId == location.Id && t.EffectiveFrom <= businessDate)
            .Where(t => t.EffectiveTo == null || t.EffectiveTo >= businessDate)
            .OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (tax is null)
        {
            return Result.Failure<PosContext>(TaxNotConfigured.With("businessDate", businessDate));
        }

        var currency = await _db.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == location.BaseCurrencyCode, ct);

        if (currency is null)
        {
            return Result.Failure<PosContext>(CurrencyNotConfigured.With("code", location.BaseCurrencyCode));
        }

        var policy = await _db.PosPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.LocationId == location.Id, ct)
            ?? PosPolicy.CreateDefault(location.Id);

        var loyalty = await _db.LoyaltyPolicies.AsNoTracking().FirstOrDefaultAsync(l => l.LocationId == location.Id, ct);

        var rules = await _db.PricingRuleSettings
            .AsNoTracking()
            .Where(r => r.LocationId == location.Id)
            .OrderBy(r => r.Order)
            .ToListAsync(ct);

        // A location with no ladder rows configured still has to be able to sell; the documented
        // default order is the fallback, not an error.
        if (rules.Count == 0)
        {
            rules = PricingRuleSetting.SeedDefaults(location.Id).ToList();
        }

        return Result.Success(new PosContext(location, station, policy, tax, loyalty, currency, rules, businessDate));
    }
}
