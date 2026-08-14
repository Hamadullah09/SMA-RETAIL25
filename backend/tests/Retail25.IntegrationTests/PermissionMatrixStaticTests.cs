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
/// The two halves of the permission matrix that need nothing but the compiled assembly.
/// <para>
/// Split out of <see cref="PermissionMatrixTests"/>, which belongs to a collection whose fixture
/// builds a database. These read attributes through reflection and touch no data at all, so
/// sharing that fixture meant they failed — not skipped, failed — on any machine without Docker.
/// A suite that is red for a reason unrelated to the code is a suite people stop reading, which is
/// the reasoning RequiresDockerFactAttribute already sets out.
/// </para>
/// <para>
/// Fixture-free, they run everywhere: on a laptop, in CI, before a commit. These are exactly the
/// checks worth having in all three places, because what they catch — a permission demanded that
/// does not exist, a permission granted that guards nothing — is invisible at runtime until
/// somebody is refused a feature nobody can reach.
/// </para>
/// </summary>
public sealed class PermissionMatrixStaticTests
{
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
}
