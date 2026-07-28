using Retail25.Domain.Catalog;
using Retail25.Domain.Configuration;
using Retail25.Domain.Customers;
using Retail25.Domain.Sales;
using Retail25.Domain.Sales.Pricing;
using Retail25.Domain.ValueObjects;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// Builds pricing scenarios for the tests.
/// <para>
/// The point of this builder is that every test states only what it is about. A test for compound
/// tax says nothing about loyalty; a test for bonus pricing says nothing about tax rates. Anything
/// unstated takes a neutral default, so when a test fails the reason is in the test.
/// </para>
/// </summary>
internal sealed class PricingScenarioBuilder
{
    private readonly Guid _locationId = Guid.NewGuid();
    private readonly List<PricingLineRequest> _lines = [];

    private DateOnly _businessDate = new(2026, 7, 27);
    private TaxConfiguration? _tax;
    private PosPolicy? _policy;
    private RoundingPolicy _rounding = new(2, MidpointRounding.AwayFromZero, 0.01m);
    private CustomerPricingProfile? _customer;
    private CartTaxOverride? _saleOverride;
    private LoyaltyPolicy? _loyalty;
    private PricingPermissions _permissions = PricingPermissions.All;
    private SaleAdjustments _adjustments = SaleAdjustments.None;

    /// <summary>Two taxes at 5% and 7%, both switched on, exclusive, no compounding.</summary>
    public PricingScenarioBuilder WithStandardTaxes()
        => WithTaxes(tax1Rate: 5m, tax2Rate: 7m);

    public PricingScenarioBuilder WithTaxes(
        decimal tax1Rate,
        decimal tax2Rate,
        bool compound = false,
        TaxationType taxationType = TaxationType.Exclusive,
        bool addOnEnabled = false,
        decimal addOnRate = 0m,
        bool addOnTaxable = false)
    {
        _tax = TaxConfiguration.Create(
            locationId: _locationId,
            effectiveFrom: new DateOnly(2000, 1, 1),
            tax1Enabled: tax1Rate > 0m,
            tax1Name: "GST",
            tax1Rate: new Percentage(tax1Rate),
            tax2Enabled: tax2Rate > 0m,
            tax2Name: "PST",
            tax2Rate: new Percentage(tax2Rate),
            tax2Compound: compound,
            addOnChargeEnabled: addOnEnabled,
            addOnChargeName: "Service charge",
            addOnChargeRate: new Percentage(addOnRate),
            addOnChargeTaxable: addOnTaxable,
            taxationType: taxationType,
            registrationNumber: null).Value;

        return this;
    }

    public PricingScenarioBuilder WithPolicy(Action<PosPolicy> configure)
    {
        _policy ??= PosPolicy.CreateDefault(_locationId);
        configure(_policy);
        return this;
    }

    public PricingScenarioBuilder WithCurrencyScale(int scale, decimal minimumTender)
    {
        _rounding = new RoundingPolicy(scale, MidpointRounding.AwayFromZero, minimumTender);
        return this;
    }

    public PricingScenarioBuilder OnDate(DateOnly date)
    {
        _businessDate = date;
        return this;
    }

    public PricingScenarioBuilder WithCustomer(
        decimal usualDiscountPct = 0m,
        int priceLevel = 1,
        bool exemptTax1 = false,
        bool exemptTax2 = false,
        int rewardPoints = 0)
    {
        _customer = CustomerPricingProfile.Create(Guid.NewGuid());
        _customer.UsualDiscountPct = usualDiscountPct;
        _customer.PriceLevel = priceLevel;
        _customer.ExemptTax1 = exemptTax1;
        _customer.ExemptTax2 = exemptTax2;
        _customer.RewardPoints = rewardPoints;
        return this;
    }

    public PricingScenarioBuilder WithLoyalty(
        decimal pointsPerDollar = 1m,
        int minimumRequired = 500,
        bool percentEnabled = false,
        decimal rewardPercent = 0m,
        bool fixedEnabled = false,
        decimal rewardFixedAmount = 0m)
    {
        var policy = LoyaltyPolicy.CreateDisabled(_locationId);
        policy.IsEnabled = true;
        policy.PointsPerDollar = pointsPerDollar;
        policy.MinimumRequired = minimumRequired;
        policy.PercentEnabled = percentEnabled;
        policy.RewardPercent = rewardPercent;
        policy.FixedEnabled = fixedEnabled;
        policy.RewardFixedAmount = rewardFixedAmount;
        _loyalty = policy;
        return this;
    }

    /// <summary>Suspends or applies a tax for the rest of the sale, from a given line onward.</summary>
    public PricingScenarioBuilder WithSaleTaxOverride(int fromSequence, bool? tax1 = null, bool? tax2 = null)
    {
        _saleOverride = CartTaxOverride.Create(Guid.NewGuid(), fromSequence, tax1, tax2);
        return this;
    }

    public PricingScenarioBuilder WithPermissions(PricingPermissions permissions)
    {
        _permissions = permissions;
        return this;
    }

    public PricingScenarioBuilder WithAdjustments(SaleAdjustments adjustments)
    {
        _adjustments = adjustments;
        return this;
    }

    /// <summary>Adds a line, with only the product price and quantity as required detail.</summary>
    public PricingScenarioBuilder AddLine(
        decimal regularPrice,
        decimal quantity = 1m,
        ProductType type = ProductType.Standard,
        bool tax1Applies = true,
        bool tax2Applies = true,
        decimal? manualUnitPrice = null,
        decimal? embeddedUnitPrice = null,
        decimal? manualDiscountPct = null,
        int? requestedPriceLevel = null,
        bool? tax1Override = null,
        bool? tax2Override = null,
        LineType lineType = LineType.Sale,
        PriceSource source = PriceSource.StockCode,
        decimal unitCost = 0m,
        IReadOnlyList<(int Level, decimal Price)>? priceLevels = null,
        IReadOnlyList<(int Level, decimal MinQuantity)>? priceBreaks = null,
        (decimal BuyQty, decimal FreeQty)? bonus = null,
        (decimal DiscountPct, DateOnly Start, DateOnly End)? salePricing = null)
    {
        var product = Product.Create(
            locationId: _locationId,
            stockCode: $"SKU{_lines.Count + 1:D4}",
            name: $"Item {_lines.Count + 1}",
            type: type,
            regularPrice: regularPrice,
            tax1Applies: tax1Applies,
            tax2Applies: tax2Applies).Value;

        var prices = (priceLevels ?? [])
            .Select(p => ProductPrice.Create(product.Id, p.Level, p.Price).Value)
            .ToList();

        var breaks = (priceBreaks ?? [])
            .Select(b => PriceBreak.Create(product.Id, b.Level, b.MinQuantity).Value)
            .ToList();

        var bonusRow = bonus is { } b2 ? BonusPricing.Create(product.Id, b2.BuyQty, b2.FreeQty).Value : null;

        var sales = salePricing is { } s
            ? new List<SalePricing> { SalePricing.Create(product.Id, s.DiscountPct, s.Start, s.End).Value }
            : [];

        var input = new LineInput(
            Sequence: _lines.Count,
            Product: product,
            Variant: null,
            Quantity: quantity,
            ManualUnitPrice: manualUnitPrice,
            EmbeddedUnitPrice: embeddedUnitPrice,
            ManualDiscountPct: manualDiscountPct,
            RequestedPriceLevel: requestedPriceLevel,
            Tax1Override: tax1Override,
            Tax2Override: tax2Override,
            Type: lineType,
            Source: source);

        _lines.Add(new PricingLineRequest(
            input,
            new ProductPricingData(prices, breaks, bonusRow, sales),
            unitCost));

        return this;
    }

    public PricingContext BuildContext()
    {
        _tax ??= TaxConfiguration.Create(
            _locationId, new DateOnly(2000, 1, 1),
            false, string.Empty, Percentage.Zero,
            false, string.Empty, Percentage.Zero,
            false, false, string.Empty, Percentage.Zero, false,
            TaxationType.Exclusive, null).Value;

        _policy ??= PosPolicy.CreateDefault(_locationId);
        _loyalty ??= LoyaltyPolicy.CreateDisabled(_locationId);

        return new PricingContext(
            _businessDate,
            _tax,
            _policy,
            _rounding,
            _customer,
            _saleOverride,
            _loyalty,
            _permissions);
    }

    public SalePricingResult Calculate()
        => SalePricingEngine.Calculate(_lines, BuildContext(), _adjustments);
}
