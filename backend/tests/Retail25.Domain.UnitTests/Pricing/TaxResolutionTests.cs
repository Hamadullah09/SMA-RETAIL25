using FluentAssertions;
using Retail25.Domain.Catalog;
using Retail25.Domain.Sales.Pricing;
using Xunit;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// Tax applicability per line (doc 04 §3). Precedence is: sale-level override, then line override,
/// then the combination of store policy, the item's flag and the customer's exemptions.
/// </summary>
public class TaxResolutionTests
{
    [Fact]
    public void Charges_both_taxes_on_an_ordinary_taxable_item()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .AddLine(regularPrice: 100m)
            .Calculate();

        result.Tax1Total.Should().Be(5m);
        result.Tax2Total.Should().Be(7m);
        result.GrandTotal.Should().Be(112m);
    }

    [Fact]
    public void Does_not_charge_a_tax_the_item_is_not_flagged_for()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .AddLine(regularPrice: 100m, tax2Applies: false)
            .Calculate();

        result.Tax1Total.Should().Be(5m);
        result.Tax2Total.Should().Be(0m);
        result.Lines[0].Tax2Source.Should().Be(TaxDecisionSource.ProductFlag);
    }

    [Fact]
    public void Does_not_charge_a_tax_the_store_has_switched_off()
    {
        // Guide p.77: a tax is charged only if it is selected in the policy AND the item is flagged.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithPolicy(p => p.UpdateTaxBehaviour(applyTax1: false, applyTax2: true, allowTaxOverride: true, applyAddOnCharge: false))
            .AddLine(regularPrice: 100m)
            .Calculate();

        result.Tax1Total.Should().Be(0m);
        result.Tax2Total.Should().Be(7m);
        result.Lines[0].Tax1Source.Should().Be(TaxDecisionSource.PolicyDefault);
    }

    [Fact]
    public void Honours_a_customers_tax_exemption()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithCustomer(exemptTax1: true)
            .AddLine(regularPrice: 100m)
            .Calculate();

        result.Tax1Total.Should().Be(0m);
        result.Tax2Total.Should().Be(7m);
        result.Lines[0].Tax1Source.Should().Be(TaxDecisionSource.CustomerExemption);
    }

    [Fact]
    public void A_line_override_can_apply_a_tax_the_item_is_not_flagged_for()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .AddLine(regularPrice: 100m, tax1Applies: false, tax1Override: true)
            .Calculate();

        result.Tax1Total.Should().Be(5m);
        result.Lines[0].Tax1Source.Should().Be(TaxDecisionSource.LineOverride);
    }

    [Fact]
    public void A_line_override_is_ignored_when_the_store_forbids_overrides()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithPolicy(p => p.UpdateTaxBehaviour(applyTax1: true, applyTax2: true, allowTaxOverride: false, applyAddOnCharge: false))
            .AddLine(regularPrice: 100m, tax1Override: false)
            .Calculate();

        result.Tax1Total.Should().Be(5m);
    }

    [Fact]
    public void A_line_override_is_ignored_without_the_permission()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithPermissions(PricingPermissions.None)
            .AddLine(regularPrice: 100m, tax1Override: false)
            .Calculate();

        result.Tax1Total.Should().Be(5m);
    }

    [Fact]
    public void A_per_sale_override_does_not_reach_lines_already_on_the_screen()
    {
        // Guide p.11, verbatim: the change applies "only for the items that are not already on the
        // POS screen". Line 0 keeps its tax; the override was raised before line 1 was rung up.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithSaleTaxOverride(fromSequence: 1, tax1: false)
            .AddLine(regularPrice: 100m)
            .AddLine(regularPrice: 100m)
            .Calculate();

        result.Lines[0].Tax1Applies.Should().BeTrue();
        result.Lines[0].Tax1Source.Should().Be(TaxDecisionSource.ProductFlag);

        result.Lines[1].Tax1Applies.Should().BeFalse();
        result.Lines[1].Tax1Source.Should().Be(TaxDecisionSource.SaleOverride);

        result.Tax1Total.Should().Be(5m);
    }

    [Fact]
    public void A_per_sale_override_outranks_a_line_override()
    {
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .WithSaleTaxOverride(fromSequence: 0, tax1: false)
            .AddLine(regularPrice: 100m, tax1Override: true)
            .Calculate();

        result.Tax1Total.Should().Be(0m);
        result.Lines[0].Tax1Source.Should().Be(TaxDecisionSource.SaleOverride);
    }

    [Fact]
    public void Gift_cards_are_never_taxed_at_issue()
    {
        // Guide p.106: tax is charged when the card is spent, not when it is bought. Not even an
        // override re-enables it.
        var result = new PricingScenarioBuilder()
            .WithStandardTaxes()
            .AddLine(regularPrice: 50m, type: ProductType.GiftCard, tax1Override: true, tax2Override: true)
            .Calculate();

        result.Tax1Total.Should().Be(0m);
        result.Tax2Total.Should().Be(0m);
        result.Lines[0].Tax1Source.Should().Be(TaxDecisionSource.GiftCardExempt);
        result.GrandTotal.Should().Be(50m);
    }

    [Fact]
    public void An_override_cannot_conjure_a_tax_the_location_has_not_configured()
    {
        var result = new PricingScenarioBuilder()
            .WithTaxes(tax1Rate: 5m, tax2Rate: 0m)
            .AddLine(regularPrice: 100m, tax2Override: true)
            .Calculate();

        result.Tax2Total.Should().Be(0m);
        result.Lines[0].Tax2Source.Should().Be(TaxDecisionSource.NotConfigured);
    }

    [Fact]
    public void Compounding_charges_tax_2_on_the_amount_including_tax_1()
    {
        // Guide p.77. 100 → tax1 5.00 → tax2 on 105.00 at 7% = 7.35.
        var result = new PricingScenarioBuilder()
            .WithTaxes(tax1Rate: 5m, tax2Rate: 7m, compound: true)
            .AddLine(regularPrice: 100m)
            .Calculate();

        result.Tax1Total.Should().Be(5m);
        result.Tax2Total.Should().Be(7.35m);
        result.GrandTotal.Should().Be(112.35m);
    }

    [Fact]
    public void Tax_inclusive_pricing_backs_the_tax_out_of_the_sticker_price()
    {
        // 112.00 inclusive of 5% + 7% is 100.00 net. The customer still pays exactly 112.00.
        var result = new PricingScenarioBuilder()
            .WithTaxes(tax1Rate: 5m, tax2Rate: 7m, taxationType: Domain.Configuration.TaxationType.Inclusive)
            .AddLine(regularPrice: 112m)
            .Calculate();

        result.GrandTotal.Should().Be(112m);
        result.Tax1Total.Should().Be(5m);
        result.Tax2Total.Should().Be(7m);
    }

    [Fact]
    public void The_add_on_charge_is_taxed_when_configured_to_be()
    {
        // Guide p.76–77: a 10% service charge on 100.00 is 10.00, and both taxes then apply to 110.
        var result = new PricingScenarioBuilder()
            .WithTaxes(tax1Rate: 5m, tax2Rate: 7m, addOnEnabled: true, addOnRate: 10m, addOnTaxable: true)
            .WithPolicy(p => p.UpdateTaxBehaviour(applyTax1: true, applyTax2: true, allowTaxOverride: true, applyAddOnCharge: true))
            .AddLine(regularPrice: 100m)
            .Calculate();

        result.AddOnCharge.Should().Be(10m);
        result.Tax1Total.Should().Be(5.50m);
        result.Tax2Total.Should().Be(7.70m);
        result.GrandTotal.Should().Be(123.20m);
    }

    [Fact]
    public void The_add_on_charge_is_untaxed_when_configured_that_way()
    {
        var result = new PricingScenarioBuilder()
            .WithTaxes(tax1Rate: 5m, tax2Rate: 7m, addOnEnabled: true, addOnRate: 10m, addOnTaxable: false)
            .WithPolicy(p => p.UpdateTaxBehaviour(applyTax1: true, applyTax2: true, allowTaxOverride: true, applyAddOnCharge: true))
            .AddLine(regularPrice: 100m)
            .Calculate();

        result.AddOnCharge.Should().Be(10m);
        result.Tax1Total.Should().Be(5m);
        result.Tax2Total.Should().Be(7m);
        result.GrandTotal.Should().Be(122m);
    }
}
