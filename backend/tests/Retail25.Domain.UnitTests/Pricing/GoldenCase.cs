using System.Text.Json.Serialization;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// The on-disk shape of a golden case.
/// <para>
/// Each file is one cart and the money it must produce, and every file cites the guide page or the
/// architecture decision it encodes. They are written before the engine and are the parity contract:
/// if a change to pricing breaks one, that is the change asking to be justified, not the file.
/// </para>
/// </summary>
public sealed record GoldenCase(
    string Name,
    string Source,
    string Description,
    GoldenContext Context,
    List<GoldenLine> Lines,
    List<GoldenAdjustment>? Adjustments,
    GoldenExpectation Expected);

public sealed record GoldenContext(
    string BusinessDate,
    GoldenTax Tax,
    GoldenPolicy Policy,
    GoldenCustomer? Customer,
    GoldenLoyalty? Loyalty,
    GoldenSaleTaxOverride? SaleTaxOverride,
    GoldenRounding? Rounding);

public sealed record GoldenTax(
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
    bool Inclusive);

public sealed record GoldenPolicy(
    bool ApplyTax1 = true,
    bool ApplyTax2 = true,
    bool AllowTaxOverride = true,
    bool ApplyAddOnCharge = false,
    bool StaffMayDiscount = true);

public sealed record GoldenCustomer(
    int PriceLevel = 1,
    decimal UsualDiscountPct = 0m,
    bool ExemptTax1 = false,
    bool ExemptTax2 = false,
    int RewardPoints = 0);

public sealed record GoldenLoyalty(
    bool IsEnabled,
    decimal PointsPerDollar,
    int MinimumRequired,
    bool PercentEnabled,
    decimal RewardPercent,
    bool FixedEnabled,
    decimal RewardFixedAmount,
    bool SuppressIfSubtotalDiscountApplied = true);

public sealed record GoldenSaleTaxOverride(bool? Tax1, bool? Tax2, int AppliesFromSequence);

public sealed record GoldenRounding(int Scale = 2, decimal MinimumTender = 0.01m);

public sealed record GoldenLine(
    string StockCode,
    string Name,
    decimal RegularPrice,
    decimal Quantity,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Domain.Catalog.ProductType ProductType = Domain.Catalog.ProductType.Standard,
    bool Tax1Applies = true,
    bool Tax2Applies = true,
    decimal AvgCost = 0m,
    decimal? ManualUnitPrice = null,
    decimal? ManualDiscountPct = null,
    int? RequestedPriceLevel = null,
    bool? Tax1Override = null,
    bool? Tax2Override = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Domain.Sales.LineType LineType = Domain.Sales.LineType.Sale,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Domain.Sales.LineSource Source = Domain.Sales.LineSource.StockCode,
    decimal? EmbeddedPrice = null,
    Dictionary<int, decimal>? PriceLevels = null,
    Dictionary<int, decimal>? PriceBreaks = null,
    GoldenBonus? Bonus = null,
    GoldenSale? Sale = null);

public sealed record GoldenBonus(decimal BuyQty, decimal FreeQty);

public sealed record GoldenSale(decimal DiscountPct, string StartsOn, string EndsOn);

public sealed record GoldenAdjustment(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Domain.Sales.AdjustmentType Type,
    string Label,
    decimal Amount = 0m,
    decimal Percent = 0m);

public sealed record GoldenExpectation(
    decimal Subtotal,
    decimal AdjustmentTotal,
    decimal DiscountedSubtotal,
    decimal AddOnCharge,
    decimal Tax1Total,
    decimal Tax2Total,
    decimal GrandTotal,
    int LoyaltyPointsEarned = 0,
    int LoyaltyPointsRedeemed = 0,
    List<GoldenExpectedLine>? Lines = null);

public sealed record GoldenExpectedLine(
    decimal UnitPrice,
    decimal ChargeableQuantity,
    decimal LineNet,
    decimal Tax1Amount,
    decimal Tax2Amount,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] Domain.Sales.PriceOrigin PriceOrigin);
