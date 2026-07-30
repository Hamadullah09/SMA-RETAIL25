using FluentAssertions;
using Retail25.Application.Loyalty;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Customers;
using Xunit;

namespace Retail25.Application.UnitTests.Purchasing;

/// <summary>
/// Stage 5: the admin surface over loyalty (guide p.83–84). Earn/redeem on a sale already worked
/// before this stage (<see cref="Carts.CompleteSaleTests"/>); this covers policy CRUD, balance lookup
/// and the manual adjustment a supervisor makes outside the sale flow.
/// </summary>
public sealed class LoyaltyTests
{
    [Fact]
    public async Task An_unconfigured_location_reads_as_a_disabled_default_policy()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var policy = await harness.Loyalty.Handle(new GetLoyaltyPolicyQuery(harness.Location.Id), default);

        policy.IsEnabled.Should().BeFalse();
        policy.PointsPerDollar.Should().Be(0m);
    }

    [Fact]
    public async Task Saving_the_policy_creates_it_on_first_save_and_updates_it_after()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var command = new SaveLoyaltyPolicyCommand(harness.Location.Id, true, 1m, 100, true, 5m, false, 0m, true);
        var first = await harness.Loyalty.Handle(command, default);
        first.IsSuccess.Should().BeTrue();

        harness.Db.LoyaltyPolicies.Should().ContainSingle(p => p.LocationId == harness.Location.Id);

        var second = await harness.Loyalty.Handle(command with { PointsPerDollar = 2m }, default);

        second.Value.PointsPerDollar.Should().Be(2m);
        harness.Db.LoyaltyPolicies.Should().ContainSingle(); // still one row, updated in place
    }

    [Fact]
    public async Task Balance_for_a_customer_with_no_pricing_profile_yet_reads_as_zero()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");

        var result = await harness.Loyalty.Handle(new GetLoyaltyBalanceQuery(customer.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.RewardPoints.Should().Be(0);
    }

    [Fact]
    public async Task A_manual_grant_raises_the_balance_and_is_recorded_on_the_ledger()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");

        var result = await harness.Loyalty.Handle(new AdjustLoyaltyPointsCommand(customer.Id, 250, "Goodwill gesture"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.RewardPoints.Should().Be(250);

        harness.Db.LoyaltyLedgerEntries.Should().ContainSingle(e => e.EntryType == LoyaltyEntryType.Manual && e.Points == 250);
    }

    [Fact]
    public async Task A_manual_adjustment_cannot_take_the_balance_below_zero()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");
        await harness.Loyalty.Handle(new AdjustLoyaltyPointsCommand(customer.Id, 50, "Initial grant"), default);

        var result = await harness.Loyalty.Handle(new AdjustLoyaltyPointsCommand(customer.Id, -100, "Correction"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("loyalty.insufficient_balance");

        (await harness.Loyalty.Handle(new GetLoyaltyBalanceQuery(customer.Id), default)).Value.RewardPoints.Should().Be(50);
    }

    [Fact]
    public async Task A_manual_adjustment_requires_a_reason()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (customer, _) = await harness.AddCustomerWithAccountAsync("Ada", "Lovelace");

        var result = await harness.Loyalty.Handle(new AdjustLoyaltyPointsCommand(customer.Id, 10, ""), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("loyalty.reason_required");
    }
}
