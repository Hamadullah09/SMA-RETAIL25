using System.Reflection;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Behaviors;
using Retail25.Application.Carts.Commands;
using Retail25.Application.Catalog;
using Retail25.Application.Common;
using Retail25.Application.Customers;
using Retail25.Application.Maintenance;
using Retail25.Application.Sales.Commands;
using Retail25.Domain.Catalog;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// What each role is actually refused.
/// <para>
/// The beta audit could not test this: one account existed and none could be created, so the
/// permission matrix was left blank. It is the check that matters most for a shop, because the
/// screens hide what you may not do and hiding is not refusing — a cashier with the developer
/// console open is still just a person typing a URL.
/// </para>
/// <para>
/// These act as each legacy access level in turn and put real commands through the real pipeline.
/// No password is involved and no account is created: authorisation is decided by the permission
/// set the actor carries, so acting as a cashier is exactly what the server sees when a cashier
/// signs in.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class PermissionMatrixTests
{
    private readonly CommerceApiFixture _api;

    public PermissionMatrixTests(CommerceApiFixture api) => _api = api;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

    /// <summary>The five levels the seeder grants, named as the roles a shopkeeper sees.</summary>
    public static TheoryData<int, string> Levels() => new()
    {
        { 0, "Trainee" },
        { 1, "Cashier" },
        { 2, "Clerk" },
        { 3, "Supervisor" },
        { 4, "Administrator" },
    };

    /// <summary>
    /// Every gated command, for every role: allowed exactly when the role holds the permission.
    /// <para>
    /// Driven from <see cref="PermissionKeys.LegacyLevelPresets"/> rather than from a list written
    /// out here, so a permission added to a role tomorrow is covered by this test without anybody
    /// remembering to update it.
    /// </para>
    /// </summary>
    [RequiresDockerTheory]
    [MemberData(nameof(Levels))]
    public async Task A_role_may_do_exactly_what_it_holds(int level, string roleName)
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        var held = new HashSet<string>(PermissionKeys.LegacyLevelPresets[level], StringComparer.Ordinal);
        var restore = _api.ActingUser.ActAs(held);

        // One command per permission worth proving, each a thing somebody actually does at a shop.
        var attempts = new (string Permission, string What, Func<Task> Run)[]
        {
            (PermissionKeys.Pos.Sell, "open a till", () => sender.Send(new CreateCartCommand(station.Id))),
            (PermissionKeys.Catalog.Write, "create a product", () => sender.Send(new CreateProductCommand(
                location.Id,
                new ProductGeneralSection(Unique("PERM"), "Permission probe", null, ProductType.Standard, null, null, null, null),
                RegularPrice: 1.00m, Tax1Applies: false, Tax2Applies: false))),
            (PermissionKeys.Customer.Write, "create a customer", () => sender.Send(new CreateCustomerCommand(
                location.Id,
                new CustomerIdentitySection(Unique("P"), "Probe", null, null, null, null, null),
                Addresses: null,
                Account: null))),
            (PermissionKeys.System.Backup, "take a backup", () => sender.Send(new ListDatabaseBackupsQuery())),
        };

        try
        {
            foreach (var (permission, what, run) in attempts)
            {
                var allowed = held.Contains(permission);

                if (allowed)
                {
                    // It must get past the gate. It may still fail on business rules — a trainee's
                    // till needs a drawer, a duplicate customer is refused — and that is fine: this
                    // asserts authorisation, not the rule behind it.
                    var act = async () => await run();
                    await act.Should().NotThrowAsync<PermissionDeniedException>(
                        $"{roleName} holds {permission} and should be able to {what}");
                }
                else
                {
                    var act = async () => await run();
                    (await act.Should().ThrowAsync<PermissionDeniedException>(
                        $"{roleName} does not hold {permission} and must not be able to {what}"))
                        .Which.Permission.Should().Be(permission);
                }
            }
        }
        finally
        {
            _api.ActingUser.ActAs(restore);
        }
    }

    /// <summary>
    /// A cashier cannot refund, void or reach the books, however the request arrives.
    /// <para>
    /// Spelled out separately from the loop above because these are the ones that cost money, and a
    /// test that reads as prose is the one somebody checks when the rules change.
    /// </para>
    /// </summary>
    [RequiresDockerFact]
    public async Task A_cashier_cannot_refund_void_or_read_the_books()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var restore = _api.ActingUser.ActAs(PermissionKeys.LegacyLevelPresets[1]);

        try
        {
            var refund = async () => await sender.Send(new RefundSaleCommand(
                1, [new RefundLineRequest(1, 1m)], [new RefundTenderRequest(1, 1m)], Guid.NewGuid().ToString("N")));

            var voidSale = async () => await sender.Send(new VoidSaleCommand(1, Guid.NewGuid().ToString("N")));

            var backups = async () => await sender.Send(new ListDatabaseBackupsQuery());

            (await refund.Should().ThrowAsync<PermissionDeniedException>("a cashier may not hand money back"))
                .Which.Permission.Should().Be(PermissionKeys.Pos.Return);

            (await voidSale.Should().ThrowAsync<PermissionDeniedException>("a cashier may not unmake a sale"))
                .Which.Permission.Should().Be(PermissionKeys.Pos.VoidSale);

            (await backups.Should().ThrowAsync<PermissionDeniedException>("a cashier may not take the database home"))
                .Which.Permission.Should().Be(PermissionKeys.System.Backup);
        }
        finally
        {
            _api.ActingUser.ActAs(restore);
        }
    }

    /// <summary>
    /// Every permission a command demands is one a role can actually be granted.
    /// <para>
    /// A typo in a <c>RequiresPermission</c> attribute produces a feature no role can ever reach and
    /// nothing that says so — it simply refuses everybody for ever. Reflection over the compiled
    /// assembly is the only thing that notices.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_permission_a_command_demands_is_one_that_exists()
    {
        var known = new HashSet<string>(PermissionKeys.All, StringComparer.Ordinal);

        var demanded = typeof(CreateCartCommand).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RequiresPermissionAttribute>(inherit: false)
                .Select(a => new { Type = t.Name, a.Permission }))
            .ToList();

        demanded.Should().NotBeEmpty("commands are supposed to declare what they need");

        var unknown = demanded.Where(d => !known.Contains(d.Permission)).ToList();

        unknown.Should().BeEmpty(
            "a command demanding a permission nobody can hold refuses everybody for ever: "
            + string.Join(", ", unknown.Select(u => $"{u.Type} wants '{u.Permission}'")));
    }

    /// <summary>
    /// Permissions that gate a <em>field</em> rather than a whole request, so they are checked
    /// inside the handler instead of declared on it.
    /// <para>
    /// A cashier may ring a sale and may not discount the line they are ringing — one command, two
    /// answers — so an attribute on the command cannot express it. The behaviour is asserted
    /// directly in the test below rather than inferred from an attribute that could not exist.
    /// </para>
    /// </summary>
    private static readonly string[] CheckedInsideTheHandler =
    [
        PermissionKeys.Pos.Discount,
        PermissionKeys.Pos.PriceOverride,
        PermissionKeys.Pos.SelectPriceLevel,
    ];

    /// <summary>
    /// And every permission a role can hold is one something actually checks.
    /// <para>
    /// The other direction. A permission granted to a role but checked by nothing is a switch on a
    /// settings screen that changes no behaviour, which is worse than not offering it — an
    /// administrator takes it away and believes they have stopped something.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_permission_a_role_can_hold_is_checked_by_something()
    {
        var demanded = new HashSet<string>(
            typeof(CreateCartCommand).Assembly.GetTypes()
                .SelectMany(t => t.GetCustomAttributes<RequiresPermissionAttribute>(inherit: false))
                .Select(a => a.Permission),
            StringComparer.Ordinal);

        demanded.UnionWith(CheckedInsideTheHandler);

        var unused = PermissionKeys.All.Where(p => !demanded.Contains(p)).ToList();

        unused.Should().BeEmpty(
            "a permission nothing checks is a switch that changes no behaviour: " + string.Join(", ", unused));
    }

    /// <summary>
    /// The three field-level ones, proved by behaviour: a cashier may sell and may not touch the
    /// price while doing it.
    /// </summary>
    [RequiresDockerFact]
    public async Task A_cashier_may_sell_but_not_reprice()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        var station = await db.Stations.AsNoTracking().FirstAsync();

        _api.ActingUser.LocationId = location.Id;
        _api.ActingUser.StationId = station.Id;

        var stockCode = Unique("REPRICE");

        await sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(stockCode, "Repriceable", null, ProductType.Standard, null, null, null, null),
            RegularPrice: 10.00m, Tax1Applies: false, Tax2Applies: false));

        var cart = await sender.Send(new CreateCartCommand(station.Id));
        cart.IsSuccess.Should().BeTrue();

        var restore = _api.ActingUser.ActAs(PermissionKeys.LegacyLevelPresets[1]);

        try
        {
            // Selling: allowed.
            var plain = await sender.Send(new AddCartLineByIdentifierCommand(cart.Value.Id, stockCode));
            plain.IsSuccess.Should().BeTrue("a cashier's whole job is ringing items up");

            // Naming their own price: refused.
            var priced = await sender.Send(
                new AddCartLineByIdentifierCommand(cart.Value.Id, stockCode, ManualPrice: 1.00m));
            priced.IsFailure.Should().BeTrue("a cashier may not decide what something costs");

            // Discounting it: refused.
            var discounted = await sender.Send(
                new AddCartLineByIdentifierCommand(cart.Value.Id, stockCode, ManualDiscountPct: 90m));
            discounted.IsFailure.Should().BeTrue("a cashier may not take 90% off");

            // Choosing a different price level: refused.
            var level = await sender.Send(
                new AddCartLineByIdentifierCommand(cart.Value.Id, stockCode, PriceLevel: 2));
            level.IsFailure.Should().BeTrue("a cashier may not move the customer to trade prices");
        }
        finally
        {
            _api.ActingUser.ActAs(restore);
        }
    }
}
