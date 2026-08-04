using MediatR;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Common;
using Retail25.Domain.Configuration;
using Retail25.Domain.Staff;

namespace Retail25.Application.Settings;

/// <summary>
/// Tender buttons (guide p.17). The legacy system let merchants edit this list and required the names
/// to match the accounting system exactly (p.110), so both the label and the external mapping key are
/// administered here.
/// </summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SaveTenderTypeCommand(long LocationId, TenderSettingsDto Tender) : IRequest<Result<TenderSettingsDto>>;

[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record DeleteTenderTypeCommand(long LocationId, long TenderTypeId) : IRequest<Result>;

/// <summary>Currencies and their rounding rules (guide p.9, p.84).</summary>
[RequiresPermission(PermissionKeys.Settings.Write)]
public sealed record SaveCurrencyCommand(long LocationId, CurrencySettingsDto Currency) : IRequest<Result<CurrencySettingsDto>>;

/// <summary>Users tab (guide p.82). Creates or edits the shop-floor identity, never the login.</summary>
[RequiresPermission(PermissionKeys.System.UsersManage)]
public sealed record SaveStaffCommand(
    long LocationId,
    long? Id,
    long UserId,
    string StaffCode,
    string FirstName,
    string LastName,
    int AccessLevel,
    bool IsActive) : IRequest<Result<StaffSettingsDto>>;

public sealed class CommerceSettingsHandlers
    : IRequestHandler<SaveTenderTypeCommand, Result<TenderSettingsDto>>,
      IRequestHandler<DeleteTenderTypeCommand, Result>,
      IRequestHandler<SaveCurrencyCommand, Result<CurrencySettingsDto>>,
      IRequestHandler<SaveStaffCommand, Result<StaffSettingsDto>>
{
    public static readonly Error TenderNotFound = new("tender_type.not_found", "No such tender type.");
    public static readonly Error TenderInUse = new("tender_type.in_use", "This tender has been used on a sale and cannot be removed. Deactivate it instead.");
    public static readonly Error LastCashTender = new("tender_type.last_cash", "At least one active cash tender is required — without one a drawer cannot be reconciled.");
    public static readonly Error CurrencyNotFound = new("currency.not_found", "No such currency.");
    public static readonly Error BaseCurrencyFixed = new("currency.base_fixed", "The base currency is set when the location is created and cannot be changed here.");
    public static readonly Error StaffNotFound = new("staff.not_found", "No such staff member.");
    public static readonly Error DuplicateStaffCode = new("staff.duplicate_code", "That staff code is already in use.");
    public static readonly Error AccessLevelInvalid = new("staff.access_level_invalid", "An access level must be between 0 and 4.");

    private readonly IApplicationDbContext _db;
    private readonly IPosNotifier _notifier;
    private readonly IDateTime _clock;

    public CommerceSettingsHandlers(IApplicationDbContext db, IPosNotifier notifier, IDateTime clock)
    {
        _db = db;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<Result<TenderSettingsDto>> Handle(SaveTenderTypeCommand request, CancellationToken ct)
    {
        var input = request.Tender;
        TenderType? tender = null;

        if (input.Id != 0L)
        {
            tender = await _db.TenderTypes.FirstOrDefaultAsync(t => t.Id == input.Id, ct);
            if (tender is null)
            {
                return Result.Failure<TenderSettingsDto>(TenderNotFound.With("tenderTypeId", input.Id));
            }
        }

        if (tender is null)
        {
            var created = TenderType.Create(input.Code, input.DisplayName, input.Behaviour, input.SortOrder, input.IconKey, input.CurrencyCode);
            if (created.IsFailure)
            {
                return Result.Failure<TenderSettingsDto>(created.Error);
            }

            tender = created.Value;
            _db.TenderTypes.Add(tender);
        }

        // Removing the last active cash tender would leave a till that cannot take cash and a drawer
        // that cannot be counted — a configuration no shop can trade in.
        if (tender.Behaviour == TenderBehaviour.Cash && !input.IsActive)
        {
            var otherCash = await _db.TenderTypes.AsNoTracking().AnyAsync(
                t => t.Id != tender.Id && !t.IsDeleted && t.IsActive && t.Behaviour == TenderBehaviour.Cash, ct);

            if (!otherCash)
            {
                return Result.Failure<TenderSettingsDto>(LastCashTender);
            }
        }

        tender.UpdatePresentation(input.DisplayName, input.SortOrder, input.IconKey);
        tender.UpdateCapabilities(
            input.OpensCashDrawer,
            input.AllowsOverTender,
            input.RoundsToMinimumTender,
            input.CountsTowardsDrawerCash,
            input.RequiresReference,
            input.PrintsSignatureCopy,
            input.AllowedForRefunds);
        tender.MapToAccounting(input.ExternalAccountingKey);
        tender.SetActive(input.IsActive);

        await _db.SaveChangesAsync(ct);

        var dto = SettingsQueryHandler.ToDto(tender);
        await _notifier.RowChangedAsync(request.LocationId, GridKeys.TenderType, tender.Id, dto, ct);
        await _notifier.SettingsChangedAsync(request.LocationId, SettingsSections.Tenders, ct);

        return Result.Success(dto);
    }

    public async Task<Result> Handle(DeleteTenderTypeCommand request, CancellationToken ct)
    {
        var tender = await _db.TenderTypes.FirstOrDefaultAsync(t => t.Id == request.TenderTypeId, ct);
        if (tender is null)
        {
            return Result.Failure(TenderNotFound.With("tenderTypeId", request.TenderTypeId));
        }

        // A tender named by a settled sale is part of that sale's record. Hiding it would leave the
        // sales log unable to say how the customer paid.
        if (await _db.SaleTenders.AsNoTracking().AnyAsync(s => s.TenderTypeId == tender.Id, ct))
        {
            return Result.Failure(TenderInUse.With("displayName", tender.DisplayName));
        }

        _db.TenderTypes.Remove(tender);
        await _db.SaveChangesAsync(ct);

        await _notifier.RowRemovedAsync(request.LocationId, GridKeys.TenderType, tender.Id, ct);
        await _notifier.SettingsChangedAsync(request.LocationId, SettingsSections.Tenders, ct);

        return Result.Success();
    }

    public async Task<Result<CurrencySettingsDto>> Handle(SaveCurrencyCommand request, CancellationToken ct)
    {
        var input = request.Currency;
        Currency? currency = null;

        if (input.Id != 0L)
        {
            currency = await _db.Currencies.FirstOrDefaultAsync(c => c.Id == input.Id, ct);
            if (currency is null)
            {
                return Result.Failure<CurrencySettingsDto>(CurrencyNotFound.With("currencyId", input.Id));
            }

            if (currency.IsBaseCurrency != input.IsBaseCurrency)
            {
                // Every ledger in the system is denominated in the base currency. Switching it after
                // a single sale exists would silently reinterpret every stored amount.
                return Result.Failure<CurrencySettingsDto>(BaseCurrencyFixed);
            }
        }

        if (currency is null)
        {
            var created = Currency.Create(
                input.Code,
                input.Name,
                input.Symbol,
                input.Scale,
                input.Rounding,
                input.MinimumTender,
                input.IsBaseCurrency);

            if (created.IsFailure)
            {
                return Result.Failure<CurrencySettingsDto>(created.Error);
            }

            currency = created.Value;
            _db.Currencies.Add(currency);
        }

        var rate = currency.SetExchangeRate(input.ExchangeRate, _clock.Now);
        if (rate.IsFailure)
        {
            return Result.Failure<CurrencySettingsDto>(rate.Error);
        }

        currency.SetActive(input.IsActive);

        await _db.SaveChangesAsync(ct);

        var dto = SettingsQueryHandler.ToDto(currency);
        await _notifier.RowChangedAsync(request.LocationId, GridKeys.Currency, currency.Id, dto, ct);
        await _notifier.SettingsChangedAsync(request.LocationId, SettingsSections.Currencies, ct);

        return Result.Success(dto);
    }

    public async Task<Result<StaffSettingsDto>> Handle(SaveStaffCommand request, CancellationToken ct)
    {
        if (request.AccessLevel is < 0 or > 4)
        {
            return Result.Failure<StaffSettingsDto>(AccessLevelInvalid.With("value", request.AccessLevel));
        }

        StaffProfile? staff = null;

        if (request.Id is { } id)
        {
            staff = await _db.StaffProfiles.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (staff is null)
            {
                return Result.Failure<StaffSettingsDto>(StaffNotFound.With("staffId", id));
            }
        }

        var code = request.StaffCode.Trim().ToUpperInvariant();

        if (await _db.StaffProfiles.AsNoTracking().AnyAsync(s => s.StaffCode == code && (staff == null || s.Id != staff.Id), ct))
        {
            // The staff code is what a cashier types at a PIN prompt. Two people sharing one would
            // make every attribution on a receipt and a commission report ambiguous.
            return Result.Failure<StaffSettingsDto>(DuplicateStaffCode.With("staffCode", code));
        }

        if (staff is null)
        {
            staff = StaffProfile.Create(request.UserId, code, request.FirstName, request.LastName, request.AccessLevel);
            _db.StaffProfiles.Add(staff);
        }
        else
        {
            staff.StaffCode = code;
            staff.FirstName = request.FirstName.Trim();
            staff.LastName = request.LastName.Trim();
            staff.AccessLevel = request.AccessLevel;
        }

        staff.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        await _notifier.SettingsChangedAsync(request.LocationId, "users", ct);

        return Result.Success(SettingsQueryHandler.ToDto(staff, _clock.Now));
    }
}
