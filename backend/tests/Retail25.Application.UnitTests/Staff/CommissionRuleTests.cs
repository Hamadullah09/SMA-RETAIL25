using FluentAssertions;
using Retail25.Application.Staff;
using Retail25.Application.UnitTests.Masters;
using Retail25.Domain.Staff;
using Xunit;

namespace Retail25.Application.UnitTests.Staff;

/// <summary>Managing the rules themselves — separate from what they pay, which the calculator owns.</summary>
public sealed class CommissionRuleTests
{
    [Fact]
    public async Task A_rule_is_saved_and_read_back()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("SK", "Sam", "Kerr");

        var saved = await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 5m),
            CancellationToken.None);

        saved.Value.Value.Should().Be(5m);

        var rules = await harness.StaffCommands.Handle(
            new ListCommissionRulesQuery(staff.Id), CancellationToken.None);

        rules.Should().ContainSingle().Which.CommissionType.Should().Be(CommissionType.Percentage);
    }

    /// <summary>
    /// The staff-wide case is the one that breaks a naive check: both scope columns are null, and
    /// `column == null` is NULL in SQL rather than true, so a predicate comparison lets a second one
    /// straight through to a constraint violation.
    /// </summary>
    [Fact]
    public async Task A_second_staff_wide_rule_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("SK", "Sam", "Kerr");

        await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 5m), CancellationToken.None);

        var again = await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 9m), CancellationToken.None);

        again.Error.Should().Be(StaffHandlers.DuplicateRule);
    }

    [Fact]
    public async Task A_second_rule_for_the_same_department_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("SK", "Sam", "Kerr");
        var hardware = await harness.AddDepartmentAsync("Hardware");

        await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 5m, DepartmentId: hardware.Id),
            CancellationToken.None);

        var again = await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 9m, DepartmentId: hardware.Id),
            CancellationToken.None);

        again.Error.Should().Be(StaffHandlers.DuplicateRule);
    }

    /// <summary>Different scopes are the whole point — one of each is exactly what precedence needs.</summary>
    [Fact]
    public async Task Rules_at_different_scopes_all_coexist()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("SK", "Sam", "Kerr");
        var hardware = await harness.AddDepartmentAsync("Hardware");
        var product = await harness.AddProductAsync("A-1", "Widget");

        await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 2m), CancellationToken.None);

        await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 5m, DepartmentId: hardware.Id),
            CancellationToken.None);

        await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Fixed, 1m, ProductId: product.Id),
            CancellationToken.None);

        var rules = await harness.StaffCommands.Handle(
            new ListCommissionRulesQuery(staff.Id), CancellationToken.None);

        rules.Should().HaveCount(3);

        // Listed most specific first, so the list reads the way the calculator resolves it.
        rules[0].ProductId.Should().NotBeNull();
        rules[1].DepartmentId.Should().NotBeNull();
        rules[2].ProductId.Should().BeNull();
        rules[2].DepartmentId.Should().BeNull();
    }

    [Fact]
    public async Task The_list_names_what_each_rule_applies_to()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("SK", "Sam", "Kerr");
        var hardware = await harness.AddDepartmentAsync("Hardware");
        var product = await harness.AddProductAsync("A-1", "Widget");

        await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 5m, DepartmentId: hardware.Id),
            CancellationToken.None);

        await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Fixed, 1m, ProductId: product.Id),
            CancellationToken.None);

        var rules = await harness.StaffCommands.Handle(
            new ListCommissionRulesQuery(staff.Id), CancellationToken.None);

        rules.Single(r => r.DepartmentId is not null).DepartmentName.Should().Be("Hardware");
        rules.Single(r => r.ProductId is not null).ProductName.Should().Contain("A-1");
    }

    [Fact]
    public async Task Editing_a_rule_changes_its_rate()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("SK", "Sam", "Kerr");

        var saved = await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 5m), CancellationToken.None);

        var edited = await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(saved.Value.Id, staff.Id, CommissionType.Percentage, 8m),
            CancellationToken.None);

        edited.Value.Value.Should().Be(8m);
        harness.Db.CommissionRules.Should().ContainSingle();
    }

    [Fact]
    public async Task An_invalid_rate_is_refused_on_edit_too()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("SK", "Sam", "Kerr");

        var saved = await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 5m), CancellationToken.None);

        var edited = await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(saved.Value.Id, staff.Id, CommissionType.Percentage, 500m),
            CancellationToken.None);

        edited.Error.Code.Should().Be(CommissionRule.PercentageOutOfRange.Code);
        harness.Db.CommissionRules.Single().Value.Should().Be(5m);
    }

    [Fact]
    public async Task Editing_a_rule_that_does_not_exist_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("SK", "Sam", "Kerr");

        var result = await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(Guid.NewGuid(), staff.Id, CommissionType.Percentage, 5m),
            CancellationToken.None);

        result.Error.Should().Be(StaffHandlers.RuleNotFound);
    }

    /// <summary>
    /// Deleting a rule removes it outright. What was already earned lives on the commission ledger
    /// with the rate frozen into it, so this cannot restate anyone's pay.
    /// </summary>
    [Fact]
    public async Task Deleting_a_rule_removes_it()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var staff = await harness.AddStaffAsync("SK", "Sam", "Kerr");

        var saved = await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, staff.Id, CommissionType.Percentage, 5m), CancellationToken.None);

        var deleted = await harness.StaffCommands.Handle(
            new DeleteCommissionRuleCommand(saved.Value.Id), CancellationToken.None);

        deleted.IsSuccess.Should().BeTrue();
        harness.Db.CommissionRules.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rule_for_someone_else_is_not_listed()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var mine = await harness.AddStaffAsync("SK", "Sam", "Kerr");
        var theirs = await harness.AddStaffAsync("JB", "Jo", "Blake");

        await harness.StaffCommands.Handle(
            new SaveCommissionRuleCommand(null, theirs.Id, CommissionType.Percentage, 5m), CancellationToken.None);

        var rules = await harness.StaffCommands.Handle(
            new ListCommissionRulesQuery(mine.Id), CancellationToken.None);

        rules.Should().BeEmpty();
    }
}
