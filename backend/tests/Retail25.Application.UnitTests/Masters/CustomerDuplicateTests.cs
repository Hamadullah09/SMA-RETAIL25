using FluentAssertions;
using Retail25.Application.Customers;
using Retail25.Domain.ValueObjects;
using Xunit;

namespace Retail25.Application.UnitTests.Masters;

/// <summary>
/// Refusing to put the same customer on file twice.
/// <para>
/// The live database holds two "Hamadullah Arain" rows created minutes apart, each free to accrue
/// its own balance, credit limit and loyalty points — nothing checked. These pin what is refused
/// and, just as importantly, what is not: a name is not a duplicate, because two customers can
/// genuinely be called the same thing and a shop must be able to serve a family.
/// </para>
/// </summary>
public sealed class CustomerDuplicateTests
{
    private static CustomerAddressSection With(string? email = null, string? phone = null, string? mobile = null)
        => new(new Address(), new Address(), new ContactDetails(Phone: phone, Mobile: mobile, Email: email));

    [Fact]
    public async Task A_second_customer_on_the_same_email_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var first = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace"),
                With(email: "ada@shop.test")), default);
        first.IsSuccess.Should().BeTrue();

        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("A.", "Lovelace"),
                With(email: "ada@shop.test")), default);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be(CustomerCommandHandlers.DuplicateContact.Code);
    }

    /// <summary>The message has to name who is already on file, or it is not actionable.</summary>
    [Fact]
    public async Task The_refusal_names_the_customer_already_on_file()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace"),
                With(email: "ada@shop.test")), default);

        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Someone", "Else"),
                With(email: "ADA@shop.test")), default);

        second.Error.Arguments!["existingCustomerName"].Should().Be("Ada Lovelace");
        second.Error.Arguments!["existingCustomerNumber"].Should().Be(1L);
        second.Error.Arguments!["matchedOn"].Should().Be("email");
    }

    [Fact]
    public async Task An_email_differing_only_in_case_is_still_the_same_email()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace"),
                With(email: "ada@shop.test")), default);

        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace"),
                With(email: "  Ada@Shop.Test  ")), default);

        second.IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// <c>+92 21 3257 4100</c> and <c>+922132574100</c> are one telephone. A plain equality test
    /// says they are two, which is how a duplicate walks straight past a check that looks present.
    /// </summary>
    [Fact]
    public async Task A_phone_number_written_differently_is_still_the_same_number()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Imran", "Sheikh"),
                With(phone: "+92 21 3257 4100")), default);

        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Imran", "Sheikh"),
                With(phone: "+922132574100")), default);

        second.IsFailure.Should().BeTrue();
        second.Error.Arguments!["matchedOn"].Should().Be("phone");
    }

    [Fact]
    public async Task A_number_on_file_as_a_mobile_still_matches_a_landline_field()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Imran", "Sheikh"),
                With(mobile: "0300 1234567")), default);

        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Imran", "Sheikh"),
                With(phone: "03001234567")), default);

        second.IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// The limit of the rule, and it is deliberate. Refusing on a name would stop a shop putting a
    /// father and son on file, which is a worse failure than the one being prevented.
    /// </summary>
    [Fact]
    public async Task Two_customers_with_the_same_name_and_no_contact_details_are_both_allowed()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var first = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Hamadullah", "Arain")), default);
        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Hamadullah", "Arain")), default);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Different_people_with_different_details_are_both_allowed()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var first = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace"),
                With(email: "ada@shop.test", phone: "0300 1111111")), default);
        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Grace", "Hopper"),
                With(email: "grace@shop.test", phone: "0300 2222222")), default);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Too short to identify anybody. An extension typed into a phone field must not collide with
    /// every other short entry in the shop.
    /// </summary>
    [Fact]
    public async Task A_number_too_short_to_identify_anybody_does_not_match()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace"),
                With(phone: "1234")), default);

        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Grace", "Hopper"),
                With(phone: "1234")), default);

        second.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Guarding only creation would leave the same collision one edit away, by a route nothing was
    /// watching.
    /// </summary>
    [Fact]
    public async Task An_edit_cannot_move_a_customer_onto_another_customers_email()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace"),
                With(email: "ada@shop.test")), default);
        var second = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Grace", "Hopper"),
                With(email: "grace@shop.test")), default);

        var edit = await harness.Customers.Handle(
            new UpdateCustomerCommand(second.Value.Id, Addresses: With(email: "ada@shop.test")), default);

        edit.IsFailure.Should().BeTrue();
        edit.Error.Code.Should().Be(CustomerCommandHandlers.DuplicateContact.Code);
    }

    /// <summary>A customer keeping their own address is not colliding with themselves.</summary>
    [Fact]
    public async Task A_customer_can_be_saved_again_with_their_own_details()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var created = await harness.Customers.Handle(
            new CreateCustomerCommand(harness.Location.Id, MastersTestHarness.Person("Ada", "Lovelace"),
                With(email: "ada@shop.test", phone: "0300 1111111")), default);

        var edit = await harness.Customers.Handle(
            new UpdateCustomerCommand(created.Value.Id, Addresses: With(email: "ada@shop.test", phone: "0300 1111111")),
            default);

        edit.IsSuccess.Should().BeTrue();
    }
}
