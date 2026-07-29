using FluentAssertions;
using Xunit;
using Retail25.Application.Catalog;
using Retail25.Application.Common;
using Retail25.Application.Customers;
using Retail25.Application.Purchasing;
using Retail25.Domain.Catalog;
using Retail25.Domain.Customers;

namespace Retail25.Application.UnitTests.Masters;

/// <summary>Customers and suppliers — the two master files a store edits daily.</summary>
public sealed class PartyTests
{
    [Fact]
    public async Task Customer_numbers_are_allocated_by_the_sequence_not_by_the_caller()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var first = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace")), default);

        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Grace", "Hopper")), default);

        first.Value.CustomerNumber.Should().Be(1);
        second.Value.CustomerNumber.Should().Be(2);
    }

    [Fact]
    public async Task A_new_customer_gets_an_account_and_a_pricing_profile_immediately()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var created = await harness.Customers.Handle(
            new CreateCustomerCommand(
                harness.Location.Id,
                MastersTestHarness.Person("Ada", "Lovelace"),
                Account: new CustomerAccountSection(500m, 10m, 3, true, false)),
            default);

        created.Value.CreditLimit.Should().Be(500m);
        created.Value.PriceLevel.Should().Be(3);
        created.Value.ExemptTax1.Should().BeTrue();

        // Creating these lazily would mean the first on-account sale writes configuration rows inside
        // a payment transaction — the worst moment to discover a constraint.
        harness.Db.CustomerAccounts.Should().ContainSingle();
        harness.Db.CustomerPricingProfiles.Should().ContainSingle();
    }

    [Fact]
    public async Task A_price_level_outside_one_to_four_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var created = await harness.Customers.Handle(
            new CreateCustomerCommand(
                harness.Location.Id,
                MastersTestHarness.Person("Ada", "Lovelace"),
                Account: new CustomerAccountSection(0m, 0m, 9, false, false)),
            default);

        created.Error.Code.Should().Be("customer.price_level_invalid");
    }

    [Fact]
    public async Task A_customer_who_still_owes_money_cannot_be_deleted()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var created = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace")), default);

        var account = harness.Db.CustomerAccounts.Single(a => a.CustomerId == created.Value.Id);
        account.BalanceDue = 42.50m;
        await harness.Db.SaveChangesAsync();

        var deleted = await harness.Customers.Handle(new DeleteCustomerCommand(created.Value.Id), default);

        // The debt would stay on the ledger while the customer disappeared from every statement run.
        deleted.Error.Code.Should().Be("customer.has_balance");
    }

    [Fact]
    public async Task A_deleted_customer_can_be_restored()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var created = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace")), default);

        (await harness.Customers.Handle(new DeleteCustomerCommand(created.Value.Id), default)).IsSuccess.Should().BeTrue();
        (await harness.Customers.Handle(new RestoreCustomerCommand(created.Value.Id), default)).IsSuccess.Should().BeTrue();

        var page = await harness.CustomerBrowse.Handle(new BrowseCustomersQuery(harness.Location.Id), default);
        page.Items.Should().ContainSingle().Which.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task The_customer_browse_pages_without_losing_a_row()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        for (var i = 0; i < 25; i++)
        {
            await harness.Customers.Handle(
                new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person($"First{i}", "Same")), default);
        }

        var seen = new List<long>();
        string? cursor = null;

        do
        {
            var page = await harness.CustomerBrowse.Handle(
                new BrowseCustomersQuery(harness.Location.Id, Sort: CustomerSort.Name, Cursor: cursor, PageSize: 7),
                default);

            seen.AddRange(page.Items.Select(c => c.CustomerNumber));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        // Every surname is "Same", so this only works because the customer number tie-breaks.
        seen.Should().HaveCount(25);
        seen.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_company_customer_browses_under_its_company_name()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace", "Analytical Engines Ltd")),
            default);

        var page = await harness.CustomerBrowse.Handle(new BrowseCustomersQuery(harness.Location.Id), default);

        page.Items.Should().ContainSingle().Which.DisplayName.Should().Be("Analytical Engines Ltd");
    }

    [Fact]
    public async Task A_supplier_that_still_sources_items_cannot_be_deleted()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var supplier = await harness.AddSupplierAsync("Acme Supply", "S001");
        var product = await harness.AddProductAsync("SKU1", "Widget");

        harness.Db.ProductSuppliers.Add(ProductSupplier.Create(product.Id, supplier.Id, 1, 4.50m).Value);
        await harness.Db.SaveChangesAsync();

        var deleted = await harness.Suppliers.Handle(new DeleteSupplierCommand(supplier.Id), default);

        deleted.Error.Code.Should().Be("supplier.still_supplies_items");
    }

    [Fact]
    public async Task A_supplier_number_is_allocated_when_none_is_given()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var created = await harness.Suppliers.Handle(
            new CreateSupplierCommand(harness.Location.Id, MastersTestHarness.SupplierDetails("Acme Supply")), default);

        created.IsSuccess.Should().BeTrue();
        created.Value.SupplierNumber.Should().Be("1");
    }

    [Fact]
    public async Task A_duplicate_supplier_number_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.AddSupplierAsync("First", "S001");

        var created = await harness.Suppliers.Handle(
            new CreateSupplierCommand(harness.Location.Id, MastersTestHarness.SupplierDetails("Second"), "S001"),
            default);

        created.Error.Code.Should().Be("supplier.duplicate_number");
    }

    [Fact]
    public async Task The_undelete_screen_lists_deleted_records_of_every_kind()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var product = await harness.AddProductAsync("SKU1", "Widget");
        var supplier = await harness.AddSupplierAsync("Acme Supply", "S001");
        var department = await harness.AddDepartmentAsync("Hardware");

        var customer = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace")), default);

        await harness.Products.Handle(new DeleteProductCommand(product.Id), default);
        await harness.Suppliers.Handle(new DeleteSupplierCommand(supplier.Id), default);
        await harness.Customers.Handle(new DeleteCustomerCommand(customer.Value.Id), default);
        await harness.Reference.Handle(new DeleteDepartmentCommand(department.Id), default);

        var rows = await harness.RecycleBin.Handle(new BrowseDeletedQuery(harness.Location.Id), default);

        rows.Select(r => r.Kind).Should().BeEquivalentTo(new[]
        {
            DeletedEntityKind.Product,
            DeletedEntityKind.Customer,
            DeletedEntityKind.Supplier,
            DeletedEntityKind.Department,
        });
    }

    [Fact]
    public async Task A_deleted_department_can_be_restored_from_the_undelete_screen()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var department = await harness.AddDepartmentAsync("Hardware");

        await harness.Reference.Handle(new DeleteDepartmentCommand(department.Id), default);

        var restored = await harness.RestoreReference.Handle(
            new RestoreReferenceRowCommand(DeletedEntityKind.Department, department.Id), default);

        restored.IsSuccess.Should().BeTrue();

        var rows = await harness.Reference.Handle(new ListDepartmentsQuery(harness.Location.Id), default);
        rows.Should().ContainSingle().Which.Name.Should().Be("Hardware");
    }
}
