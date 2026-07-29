using Retail25.Domain.Configuration;
using Retail25.Domain.Terminals;
using Retail25.Domain.ValueObjects;

namespace Retail25.Application.Settings;

// The Setup screen of the legacy system was a set of tabs over one store's configuration
// (user guide p.76–84). These DTOs are those tabs. Each one is fetched and saved on its own so a
// half-finished edit on the Hardware tab cannot overwrite the Taxes tab.

/// <summary>Business ID tab (guide p.76) — what prints at the top of every receipt and invoice.</summary>
public sealed record BusinessSettingsDto(
    Guid LocationId,
    string BusinessName,
    Address Address,
    ContactDetails Contact,
    string? LicenceNumber,
    string? TaxRegistrationNumber,
    string LocationName,
    string LegacyCode,
    string TimeZoneId,
    TimeOnly BusinessDayStart,
    string BaseCurrencyCode);

/// <summary>
/// Taxes tab (guide p.76–77). Includes the row currently in force and the whole history, because a
/// rate change is a new row and an administrator needs to see what was in force when.
/// </summary>
public sealed record TaxSettingsDto(
    Guid? Id,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
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
    string? RegistrationNumber,
    bool IsCurrent);

/// <summary>POS tab (guide p.77–78) — every switch that changes how the till behaves.</summary>
public sealed record PosSettingsDto(
    bool ApplyTax1,
    bool ApplyTax2,
    bool AllowTaxOverride,
    bool ApplyAddOnCharge,
    bool FastScanMode,
    bool AutoSaveSales,
    bool ConfirmBeforeSavingSales,
    bool ScanRandomWeightBarcodes,
    bool StaffMayDiscount,
    bool AllowItemListEdit,
    bool TrackStaffSales,
    bool RequireSupervisorToVoid,
    bool UseEmployeeTimeClock,
    bool PrintCreditCardSignatureLine,
    bool PrintClientNameOnSalesSlip,
    bool CarryOverCityStateZip,
    Guid? DefaultTenderTypeId,
    int AbandonedCartTimeoutMinutes);

/// <summary>Stations, with the per-till overrides that make one counter behave unlike another.</summary>
public sealed record StationSettingsDto(
    Guid Id,
    string StationCode,
    string? Name,
    bool? FastScanMode,
    bool? AutoSaveSales,
    bool? ConfirmBeforeSaving,
    bool? ScanRandomWeightBarcodes,
    Guid? DefaultTenderTypeId,
    Guid? PrinterProfileId,
    Guid? ReaderProfileId,
    Guid? ScaleProfileId,
    Guid? PoleDisplayProfileId,
    ReaderMode ReaderMode,
    bool IsActive,
    string? AgentVersion,
    DateTimeOffset? LastHeartbeat,
    bool AgentOnline);

/// <summary>Printers tab (guide p.78–80). Every escape sequence is data, never a driver constant.</summary>
public sealed record PrinterSettingsDto(
    Guid Id,
    Guid? StationId,
    string Name,
    string? SetupCommand,
    string? CutterCommand,
    string? RedCommand,
    string? BlackCommand,
    string? Port,
    int DefaultCopies,
    bool PageEject,
    bool ExtraCopyOnCard,
    bool InitializeSerial,
    PrinterOutput Output,
    int Columns,
    string DrawerTrigger,
    int DrawerRepeat,
    bool OpenDrawerOnPrint,
    bool IsActive);

/// <summary>Hardware tab (guide p.80–81) — scale, pole display and RFID reader.</summary>
public sealed record ScaleSettingsDto(
    Guid Id,
    Guid? StationId,
    string Name,
    string Port,
    int BaudRate,
    int DataBits,
    string Parity,
    string StopBits,
    string GetWeightCommand,
    string ZeroCommand,
    string Unit,
    int TimeoutMs,
    bool IsActive);

public sealed record PoleDisplaySettingsDto(
    Guid Id,
    Guid? StationId,
    string Name,
    string Port,
    int BaudRate,
    int Line1Width,
    int Line2Width,
    string IdleLine1,
    string IdleLine2,
    string ClearCommand,
    string Line1Command,
    string Line2Command,
    bool IsActive);

public sealed record ReaderSettingsDto(
    Guid Id,
    Guid? StationId,
    string Name,
    string Host,
    int Port,
    ReaderProtocol Protocol,
    string AntennaZones,
    int RssiThresholdDbm,
    int MinimumReadCount,
    int DebounceMs,
    int CoalesceMs,
    int FlushIntervalMs,
    int MaxBatchSize,
    bool AutoAcceptBatches,
    bool ContinuousMode,
    bool IsActive);

/// <summary>Tender buttons, in the order they appear at the till (guide p.17).</summary>
public sealed record TenderSettingsDto(
    Guid Id,
    string Code,
    string DisplayName,
    TenderBehaviour Behaviour,
    int SortOrder,
    string? IconKey,
    bool OpensCashDrawer,
    bool AllowsOverTender,
    bool RoundsToMinimumTender,
    bool CountsTowardsDrawerCash,
    bool RequiresReference,
    bool PrintsSignatureCopy,
    bool AllowedForRefunds,
    string? CurrencyCode,
    string? ExternalAccountingKey,
    bool IsActive);

public sealed record CurrencySettingsDto(
    Guid Id,
    string Code,
    string Name,
    string Symbol,
    int Scale,
    RoundingMode Rounding,
    decimal MinimumTender,
    bool IsBaseCurrency,
    decimal ExchangeRate,
    DateTimeOffset? ExchangeRateUpdatedAt,
    bool IsActive);

/// <summary>Numbering tab — the legacy "next number" settings (guide p.76).</summary>
public sealed record NumberSequenceDto(
    Guid Id,
    SequenceKind Kind,
    string Prefix,
    int PadWidth,
    long NextNumber,
    long HighWaterMark,
    string Sample);

/// <summary>Options tab — the pricing precedence ladder, reorderable without a release (decision P1).</summary>
public sealed record PricingRuleDto(Guid Id, string RuleKey, int Order, bool Enabled, string? ParametersJson);

/// <summary>Users tab (guide p.82). PIN state is reported, never the PIN or its hash.</summary>
public sealed record StaffSettingsDto(
    Guid Id,
    Guid UserId,
    string StaffCode,
    string FirstName,
    string LastName,
    int AccessLevel,
    bool IsActive,
    bool HasPin,
    bool PinLocked,
    DateTimeOffset? PinLockedUntil);

/// <summary>Everything the settings screen needs in one round trip.</summary>
public sealed record SettingsSnapshotDto(
    BusinessSettingsDto Business,
    IReadOnlyList<TaxSettingsDto> Taxes,
    PosSettingsDto Pos,
    IReadOnlyList<StationSettingsDto> Stations,
    IReadOnlyList<PrinterSettingsDto> Printers,
    IReadOnlyList<ScaleSettingsDto> Scales,
    IReadOnlyList<PoleDisplaySettingsDto> PoleDisplays,
    IReadOnlyList<ReaderSettingsDto> Readers,
    IReadOnlyList<TenderSettingsDto> Tenders,
    IReadOnlyList<CurrencySettingsDto> Currencies,
    IReadOnlyList<NumberSequenceDto> Numbering,
    IReadOnlyList<PricingRuleDto> PricingRules,
    IReadOnlyList<StaffSettingsDto> Staff);
