using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Staff;
using Retail25.Domain.Terminals;

namespace Retail25.Application.Settings;

/// <summary>
/// The whole Setup screen in one call (guide p.76–84).
/// <para>
/// One round trip rather than a call per tab. The tabs are small, an administrator opening Setup
/// will visit several, and a settings screen that flickers into existence tab by tab is the kind of
/// thing that makes people distrust whether their last save took.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Read)]
public sealed record GetSettingsQuery(long LocationId) : IRequest<Result<SettingsSnapshotDto>>;

public sealed class SettingsQueryHandler : IRequestHandler<GetSettingsQuery, Result<SettingsSnapshotDto>>
{
    public static readonly Error LocationNotFound = new("location.not_found", "No such location.");

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public SettingsQueryHandler(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<SettingsSnapshotDto>> Handle(GetSettingsQuery request, CancellationToken ct)
    {
        var location = await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<SettingsSnapshotDto>(LocationNotFound.With("locationId", request.LocationId));
        }

        var profile = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.LocationId == location.Id, ct);
        var today = _clock.Today();

        var taxes = await _db.TaxConfigurations.AsNoTracking()
            .Where(t => t.LocationId == location.Id)
            .OrderByDescending(t => t.EffectiveFrom)
            .ToListAsync(ct);

        var policy = await _db.PosPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.LocationId == location.Id, ct)
            ?? PosPolicy.CreateDefault(location.Id);

        var stations = await _db.Stations.AsNoTracking()
            .Where(s => s.LocationId == location.Id).OrderBy(s => s.StationCode).ToListAsync(ct);

        var printers = await _db.PrinterProfiles.AsNoTracking()
            .Where(p => p.LocationId == location.Id).OrderBy(p => p.Name).ToListAsync(ct);

        var scales = await _db.ScaleProfiles.AsNoTracking()
            .Where(p => p.LocationId == location.Id).OrderBy(p => p.Name).ToListAsync(ct);

        var poles = await _db.PoleDisplayProfiles.AsNoTracking()
            .Where(p => p.LocationId == location.Id).OrderBy(p => p.Name).ToListAsync(ct);

        var readers = await _db.ReaderProfiles.AsNoTracking()
            .Where(p => p.LocationId == location.Id).OrderBy(p => p.Name).ToListAsync(ct);

        var tenders = await _db.TenderTypes.AsNoTracking()
            .Where(t => !t.IsDeleted).OrderBy(t => t.SortOrder).ThenBy(t => t.DisplayName).ToListAsync(ct);

        var currencies = await _db.Currencies.AsNoTracking()
            .OrderByDescending(c => c.IsBaseCurrency).ThenBy(c => c.Code).ToListAsync(ct);

        var sequences = await _db.NumberSequences.AsNoTracking()
            .Where(s => s.LocationId == location.Id).OrderBy(s => s.Kind).ToListAsync(ct);

        var rules = await _db.PricingRuleSettings.AsNoTracking()
            .Where(r => r.LocationId == location.Id).OrderBy(r => r.Order).ToListAsync(ct);

        var staff = await _db.StaffProfiles.AsNoTracking()
            .OrderBy(s => s.StaffCode).ToListAsync(ct);

        var now = _clock.Now;

        return Result.Success(new SettingsSnapshotDto(
            new BusinessSettingsDto(
                location.Id,
                profile?.BusinessName ?? location.Name,
                profile?.Address ?? location.Address,
                profile?.Contact ?? location.Contact,
                profile?.LicenceNumber,
                profile?.TaxRegistrationNumber,
                location.Name,
                location.LegacyCode,
                location.TimeZoneId,
                location.BusinessDayStart,
                location.BaseCurrencyCode),
            taxes.Select(t => ToDto(t, today)).ToList(),
            ToDto(policy),
            stations.Select(s => ToDto(s, now)).ToList(),
            printers.Select(ToDto).ToList(),
            scales.Select(ToDto).ToList(),
            poles.Select(ToDto).ToList(),
            readers.Select(ToDto).ToList(),
            tenders.Select(ToDto).ToList(),
            currencies.Select(ToDto).ToList(),
            sequences.Select(ToDto).ToList(),
            rules.Select(r => new PricingRuleDto(r.Id, r.RuleKey, r.Order, r.Enabled, r.ParametersJson)).ToList(),
            staff.Select(s => ToDto(s, now)).ToList()));
    }

    public static TaxSettingsDto ToDto(TaxConfiguration tax, DateOnly today)
        => new(
            tax.Id,
            tax.EffectiveFrom,
            tax.EffectiveTo,
            tax.Tax1Enabled,
            tax.Tax1Name,
            tax.Tax1Rate.Value,
            tax.Tax2Enabled,
            tax.Tax2Name,
            tax.Tax2Rate.Value,
            tax.Tax2Compound,
            tax.AddOnChargeEnabled,
            tax.AddOnChargeName,
            tax.AddOnChargeRate.Value,
            tax.AddOnChargeTaxable,
            tax.TaxationType,
            tax.RegistrationNumber,
            tax.IsCurrentOn(today));

    public static PosSettingsDto ToDto(PosPolicy policy)
        => new(
            policy.ApplyTax1,
            policy.ApplyTax2,
            policy.AllowTaxOverride,
            policy.ApplyAddOnCharge,
            policy.FastScanMode,
            policy.AutoSaveSales,
            policy.ConfirmBeforeSavingSales,
            policy.ScanRandomWeightBarcodes,
            policy.StaffMayDiscount,
            policy.AllowItemListEdit,
            policy.TrackStaffSales,
            policy.RequireSupervisorToVoid,
            policy.UseEmployeeTimeClock,
            policy.PrintCreditCardSignatureLine,
            policy.PrintClientNameOnSalesSlip,
            policy.CarryOverCityStateZip,
            policy.DefaultTenderTypeId,
            policy.AbandonedCartTimeoutMinutes);

    public static StationSettingsDto ToDto(Station station, DateTimeOffset now)
        => new(
            station.Id,
            station.StationCode,
            station.Name,
            station.FastScanMode,
            station.AutoSaveSales,
            station.ConfirmBeforeSaving,
            station.ScanRandomWeightBarcodes,
            station.DefaultTenderTypeId,
            station.PrinterProfileId,
            station.ReaderProfileId,
            station.ScaleProfileId,
            station.PoleDisplayProfileId,
            station.ReaderMode,
            station.IsActive,
            station.AgentVersion,
            station.LastHeartbeat,
            station.IsAgentOnline(now));

    public static PrinterSettingsDto ToDto(PrinterProfile printer)
        => new(
            printer.Id,
            printer.StationId,
            printer.Name,
            printer.SetupCommand,
            printer.CutterCommand,
            printer.RedCommand,
            printer.BlackCommand,
            printer.Port,
            printer.DefaultCopies,
            printer.PageEject,
            printer.ExtraCopyOnCard,
            printer.InitializeSerial,
            printer.Output,
            printer.Columns,
            printer.DrawerTrigger,
            printer.DrawerRepeat,
            printer.OpenDrawerOnPrint,
            printer.IsActive);

    public static ScaleSettingsDto ToDto(ScaleProfile scale)
        => new(
            scale.Id,
            scale.StationId,
            scale.Name,
            scale.Port,
            scale.BaudRate,
            scale.DataBits,
            scale.Parity,
            scale.StopBits,
            scale.GetWeightCommand,
            scale.ZeroCommand,
            scale.Unit,
            scale.TimeoutMs,
            scale.IsActive);

    public static PoleDisplaySettingsDto ToDto(PoleDisplayProfile pole)
        => new(
            pole.Id,
            pole.StationId,
            pole.Name,
            pole.Port,
            pole.BaudRate,
            pole.Line1Width,
            pole.Line2Width,
            pole.IdleLine1,
            pole.IdleLine2,
            pole.ClearCommand,
            pole.Line1Command,
            pole.Line2Command,
            pole.IsActive);

    public static ReaderSettingsDto ToDto(ReaderProfile reader)
        => new(
            reader.Id,
            reader.StationId,
            reader.Name,
            reader.Host,
            reader.Port,
            reader.Protocol,
            reader.AntennaZones,
            reader.RssiThresholdDbm,
            reader.MinimumReadCount,
            reader.DebounceMs,
            reader.CoalesceMs,
            reader.FlushIntervalMs,
            reader.MaxBatchSize,
            reader.AutoAcceptBatches,
            reader.ContinuousMode,
            reader.IsActive);

    public static TenderSettingsDto ToDto(TenderType tender)
        => new(
            tender.Id,
            tender.Code,
            tender.DisplayName,
            tender.Behaviour,
            tender.SortOrder,
            tender.IconKey,
            tender.OpensCashDrawer,
            tender.AllowsOverTender,
            tender.RoundsToMinimumTender,
            tender.CountsTowardsDrawerCash,
            tender.RequiresReference,
            tender.PrintsSignatureCopy,
            tender.AllowedForRefunds,
            tender.CurrencyCode,
            tender.ExternalAccountingKey,
            tender.IsActive);

    public static CurrencySettingsDto ToDto(Currency currency)
        => new(
            currency.Id,
            currency.Code,
            currency.Name,
            currency.Symbol,
            currency.Scale,
            currency.Rounding,
            currency.MinimumTender,
            currency.IsBaseCurrency,
            currency.ExchangeRate,
            currency.ExchangeRateUpdatedAt,
            currency.IsActive);

    public static NumberSequenceDto ToDto(NumberSequence sequence)
        => new(
            sequence.Id,
            sequence.Kind,
            sequence.Prefix,
            sequence.PadWidth,
            sequence.NextNumber,
            sequence.HighWaterMark,
            sequence.Format(sequence.NextNumber));

    /// <summary>
    /// Staff for the Users tab. The hash is never projected — not because a settings screen would
    /// display it, but because a DTO that carries it will eventually be logged or cached by
    /// something that does not know what it is holding.
    /// </summary>
    public static StaffSettingsDto ToDto(StaffProfile staff, DateTimeOffset now)
        => new(
            staff.Id,
            staff.UserId,
            staff.StaffCode,
            staff.FirstName,
            staff.LastName,
            staff.AccessLevel,
            staff.IsActive,
            staff.HasPin,
            staff.IsPinLocked(now),
            staff.PinLockedUntil);
}
