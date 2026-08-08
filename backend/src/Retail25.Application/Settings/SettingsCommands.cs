using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.ValueObjects;

namespace Retail25.Application.Settings;

/// <summary>Business ID tab (guide p.76).</summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SaveBusinessSettingsCommand(
    long LocationId,
    string BusinessName,
    Address Address,
    ContactDetails Contact,
    string? LicenceNumber,
    string? TaxRegistrationNumber,
    string LocationName,
    string TimeZoneId,
    TimeOnly BusinessDayStart) : IRequest<Result<BusinessSettingsDto>>;

/// <summary>
/// Taxes tab (guide p.76–77). A rate change writes a <b>new effective-dated row</b> and closes the
/// previous one; it never edits history.
/// <para>
/// That is what makes a reprint of last month's invoice show last month's tax, which the guide is
/// explicit about (p.56). Editing the row in place would silently rewrite every historical document
/// that recomputes from configuration.
/// </para>
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Taxes)]
public sealed record SaveTaxSettingsCommand(
    long LocationId,
    DateOnly EffectiveFrom,
    bool Tax1Enabled,
    string Tax1Name,
    decimal Tax1Rate,
    bool Tax2Enabled,
    string Tax2Name,
    decimal Tax2Rate,
    bool Tax2Compound,
    bool AddOnChargeEnabled,
    string AddOnChargeName,
    decimal AddOnChargeRate,
    bool AddOnChargeTaxable,
    TaxationType TaxationType,
    string? RegistrationNumber) : IRequest<Result<TaxSettingsDto>>;

/// <summary>POS tab (guide p.77–78).</summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SavePosSettingsCommand(long LocationId, PosSettingsDto Settings) : IRequest<Result<PosSettingsDto>>;

/// <summary>Numbering tab — repoints a counter and the live sequence behind it (guide p.76).</summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SaveNumberSequenceCommand(
    long LocationId,
    SequenceKind Kind,
    string Prefix,
    int PadWidth,
    long? NextNumber) : IRequest<Result<NumberSequenceDto>>;

/// <summary>Options tab — the pricing precedence ladder (decision P1).</summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SavePricingLadderCommand(long LocationId, IReadOnlyList<PricingRuleDto> Rules)
    : IRequest<Result<IReadOnlyList<PricingRuleDto>>>;

public sealed class SettingsCommandHandlers
    : IRequestHandler<SaveBusinessSettingsCommand, Result<BusinessSettingsDto>>,
      IRequestHandler<SaveTaxSettingsCommand, Result<TaxSettingsDto>>,
      IRequestHandler<SavePosSettingsCommand, Result<PosSettingsDto>>,
      IRequestHandler<SaveNumberSequenceCommand, Result<NumberSequenceDto>>,
      IRequestHandler<SavePricingLadderCommand, Result<IReadOnlyList<PricingRuleDto>>>
{
    public static readonly Error LocationNotFound = new("location.not_found", "No such location.");
    public static readonly Error EffectiveDateInPast = new("tax.effective_date_in_past", "A tax change cannot start before today — sales already rung would change retroactively.");
    public static readonly Error LadderIncomplete = new("pricing_rule.ladder_incomplete", "Every pricing rule must appear exactly once in the ladder.");

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;
    private readonly ISequenceGenerator _sequences;
    private readonly IDateTime _clock;

    public SettingsCommandHandlers(
        IApplicationDbContext db,
        IPosNotifier notifier,
        ISequenceGenerator sequences,
        IDateTime clock)
    {
        _db = db;
        _notifier = notifier;
        _sequences = sequences;
        _clock = clock;
    }

    public async Task<Result<BusinessSettingsDto>> Handle(SaveBusinessSettingsCommand request, CancellationToken ct)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == request.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<BusinessSettingsDto>(LocationNotFound.With("locationId", request.LocationId));
        }

        var profile = await _db.BusinessProfiles.FirstOrDefaultAsync(b => b.LocationId == location.Id, ct);
        if (profile is null)
        {
            profile = BusinessProfile.Create(location.Id, request.BusinessName);
            _db.BusinessProfiles.Add(profile);
        }

        profile.BusinessName = request.BusinessName.Trim();

        // Fresh records rather than the caller's instances: an owned value object shared between two
        // entities is claimed by two owners, which the persistence layer refuses.
        profile.Address = request.Address with { };
        profile.Contact = request.Contact with { };
        profile.LicenceNumber = Blank(request.LicenceNumber);
        profile.TaxRegistrationNumber = Blank(request.TaxRegistrationNumber);

        location.UpdateDetails(request.LocationName, request.Address with { }, request.Contact with { });

        if (!TimeZoneExists(request.TimeZoneId))
        {
            return Result.Failure<BusinessSettingsDto>(new Error(
                "location.time_zone_unknown",
                "That time zone is not known to this server.").With("value", request.TimeZoneId));
        }

        location.UpdateCalendar(request.TimeZoneId, request.BusinessDayStart);

        await _db.SaveChangesAsync(ct);
        await _notifier.SettingsChangedAsync(location.Id, SettingsSections.Business, ct);

        return Result.Success(new BusinessSettingsDto(
            location.Id,
            profile.BusinessName,
            profile.Address,
            profile.Contact,
            profile.LicenceNumber,
            profile.TaxRegistrationNumber,
            location.Name,
            location.LegacyCode,
            location.TimeZoneId,
            location.BusinessDayStart,
            location.BaseCurrencyCode));
    }

    public async Task<Result<TaxSettingsDto>> Handle(SaveTaxSettingsCommand request, CancellationToken ct)
    {
        var today = _clock.Today();

        if (request.EffectiveFrom < today)
        {
            return Result.Failure<TaxSettingsDto>(EffectiveDateInPast.With("effectiveFrom", request.EffectiveFrom));
        }

        var created = TaxConfiguration.Create(
            request.LocationId,
            request.EffectiveFrom,
            request.Tax1Enabled,
            request.Tax1Name,
            new Percentage(request.Tax1Rate),
            request.Tax2Enabled,
            request.Tax2Name,
            new Percentage(request.Tax2Rate),
            request.Tax2Compound,
            request.AddOnChargeEnabled,
            request.AddOnChargeName,
            new Percentage(request.AddOnChargeRate),
            request.AddOnChargeTaxable,
            request.TaxationType,
            request.RegistrationNumber);

        if (created.IsFailure)
        {
            return Result.Failure<TaxSettingsDto>(created.Error);
        }

        var existing = await _db.TaxConfigurations
            .Where(t => t.LocationId == request.LocationId)
            .OrderByDescending(t => t.EffectiveFrom)
            .ToListAsync(ct);

        // Same-day correction: an administrator who mistypes a rate and fixes it before the change
        // takes effect should not leave a one-day-long row behind.
        var sameDay = existing.FirstOrDefault(t => t.EffectiveFrom == request.EffectiveFrom);
        if (sameDay is not null)
        {
            _db.TaxConfigurations.Remove(sameDay);
            existing.Remove(sameDay);
        }

        foreach (var row in existing.Where(t => t.EffectiveTo is null && t.EffectiveFrom < request.EffectiveFrom))
        {
            row.Supersede(request.EffectiveFrom);
        }

        _db.TaxConfigurations.Add(created.Value);
        await _db.SaveChangesAsync(ct);
        await _notifier.SettingsChangedAsync(request.LocationId, SettingsSections.Taxes, ct);

        return Result.Success(SettingsQueryHandler.ToDto(created.Value, today));
    }

    public async Task<Result<PosSettingsDto>> Handle(SavePosSettingsCommand request, CancellationToken ct)
    {
        var policy = await _db.PosPolicies.FirstOrDefaultAsync(p => p.LocationId == request.LocationId, ct);
        if (policy is null)
        {
            policy = PosPolicy.CreateDefault(request.LocationId);
            _db.PosPolicies.Add(policy);
        }

        var settings = request.Settings;

        policy.UpdateTaxBehaviour(settings.ApplyTax1, settings.ApplyTax2, settings.AllowTaxOverride, settings.ApplyAddOnCharge);
        policy.UpdateSellingBehaviour(
            settings.FastScanMode,
            settings.AutoSaveSales,
            settings.ConfirmBeforeSavingSales,
            settings.ScanRandomWeightBarcodes,
            settings.StaffMayDiscount,
            settings.AllowItemListEdit);
        policy.UpdateStaffControls(settings.TrackStaffSales, settings.RequireSupervisorToVoid, settings.UseEmployeeTimeClock);
        policy.UpdatePrinting(settings.PrintCreditCardSignatureLine, settings.PrintClientNameOnSalesSlip, settings.CarryOverCityStateZip);
        policy.SetDefaultTender(settings.DefaultTenderTypeId);
        policy.SetAbandonedCartTimeout(settings.AbandonedCartTimeoutMinutes);

        await _db.SaveChangesAsync(ct);
        await _notifier.SettingsChangedAsync(request.LocationId, SettingsSections.Pos, ct);

        return Result.Success(SettingsQueryHandler.ToDto(policy));
    }

    public async Task<Result<NumberSequenceDto>> Handle(SaveNumberSequenceCommand request, CancellationToken ct)
    {
        var sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.LocationId == request.LocationId && s.Kind == request.Kind, ct);

        if (sequence is null)
        {
            sequence = NumberSequence.Create(request.LocationId, request.Kind);
            _db.NumberSequences.Add(sequence);
        }

        sequence.SetFormat(request.Prefix, request.PadWidth);

        if (request.NextNumber is { } next && next != sequence.NextNumber)
        {
            var repointed = sequence.SetNext(next);
            if (repointed.IsFailure)
            {
                return Result.Failure<NumberSequenceDto>(repointed.Error);
            }

            // Saving the row alone would change nothing that issues numbers: the Postgres sequence was
            // created from this row the first time it was used and never reads it again.
            await _sequences.RestartAsync(request.Kind, request.LocationId, next, ct);
        }

        await _db.SaveChangesAsync(ct);
        await _notifier.SettingsChangedAsync(request.LocationId, SettingsSections.Numbering, ct);

        return Result.Success(SettingsQueryHandler.ToDto(sequence));
    }

    public async Task<Result<IReadOnlyList<PricingRuleDto>>> Handle(SavePricingLadderCommand request, CancellationToken ct)
    {
        var submitted = (request.Rules ?? []).ToList();
        var keys = submitted.Select(r => r.RuleKey).ToHashSet(StringComparer.Ordinal);

        // A ladder missing a rung is not a partial save, it is a pricing engine with a hole in it:
        // the rule simply stops being consulted and prices change with no record of why.
        if (keys.Count != submitted.Count || !PricingRuleKeys.DefaultOrder.All(keys.Contains))
        {
            return Result.Failure<IReadOnlyList<PricingRuleDto>>(LadderIncomplete);
        }

        var existing = await _db.PricingRuleSettings
            .Where(r => r.LocationId == request.LocationId)
            .ToListAsync(ct);

        var byKey = existing.ToDictionary(r => r.RuleKey, StringComparer.Ordinal);

        foreach (var rule in submitted)
        {
            if (byKey.TryGetValue(rule.RuleKey, out var row))
            {
                row.Reorder(rule.Order);
                row.SetEnabled(rule.Enabled);
                row.SetParameters(rule.ParametersJson);
            }
            else
            {
                var created = PricingRuleSetting.Create(request.LocationId, rule.RuleKey, rule.Order, rule.Enabled, rule.ParametersJson);
                if (created.IsSuccess)
                {
                    _db.PricingRuleSettings.Add(created.Value);
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        await _notifier.SettingsChangedAsync(request.LocationId, SettingsSections.Pricing, ct);

        var saved = await _db.PricingRuleSettings.AsNoTracking()
            .Where(r => r.LocationId == request.LocationId)
            .OrderBy(r => r.Order)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<PricingRuleDto>>(
            saved.Select(r => new PricingRuleDto(r.Id, r.RuleKey, r.Order, r.Enabled, r.ParametersJson)).ToList());
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TimeZoneExists(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
