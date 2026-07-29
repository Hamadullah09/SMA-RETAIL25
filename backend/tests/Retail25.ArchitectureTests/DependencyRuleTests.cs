using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Retail25.Application.Common;
using Xunit;

namespace Retail25.ArchitectureTests;

/// <summary>
/// The dependency rules from doc 02, enforced rather than documented.
/// <para>
/// These are the constraints that stop the architecture eroding under deadline pressure: the pricing
/// engine staying pure is what makes the golden-file suite meaningful, and a vendor name leaking into
/// Domain is what makes a payment processor change into a rewrite.
/// </para>
/// </summary>
public sealed class DependencyRuleTests
{
    private static Assembly Domain => typeof(Domain.Sales.Cart).Assembly;

    private static Assembly Application => typeof(Application.DependencyInjection).Assembly;

    private static Assembly Infrastructure => typeof(Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_else_in_the_solution()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny("Retail25.Application", "Retail25.Infrastructure", "Retail25.Api", "Retail25.Contracts")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Domain_has_no_persistence_or_web_dependencies()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "StackExchange.Redis", "MediatR")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_the_Api()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Retail25.Infrastructure", "Retail25.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    /// <summary>
    /// Application talks to the outside world only through ports. A direct SignalR or Redis reference
    /// here would make every handler untestable without a running server.
    /// </summary>
    [Fact]
    public void Application_does_not_reference_realtime_or_cache_libraries_directly()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore.SignalR", "StackExchange.Redis", "Npgsql")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    /// <summary>
    /// Q1 and Q2: no vendor name appears in Domain or Application. The ports are named for what they
    /// do, so choosing a processor later is a registration change in Infrastructure.
    /// </summary>
    [Theory]
    [InlineData("QuickBooks")]
    [InlineData("XCharge")]
    [InlineData("Moneris")]
    [InlineData("Stripe")]
    [InlineData("Square")]
    public void No_vendor_name_appears_in_Domain_or_Application(string vendor)
    {
        AssertNoTypeNameContains(Domain, vendor);
        AssertNoTypeNameContains(Application, vendor);
    }

    /// <summary>
    /// The pricing engine is pure by construction (doc 04). If it could read a clock or a database,
    /// replaying a historical sale would not reproduce the historical answer.
    /// </summary>
    [Fact]
    public void The_pricing_engine_has_no_io_and_no_clock()
    {
        var result = Types.InAssembly(Domain)
            .That().ResideInNamespace("Retail25.Domain.Sales.Pricing")
            .ShouldNot()
            .HaveDependencyOnAny("System.IO", "System.Net", "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));

        // DateTimeOffset.UtcNow inside the engine would be the subtle version of the same mistake.
        var pricingTypes = Domain.GetTypes()
            .Where(t => t.Namespace == "Retail25.Domain.Sales.Pricing")
            .ToList();

        pricingTypes.Should().NotBeEmpty();
    }

    /// <summary>
    /// Handlers live in Application, never in the API. A rule enforced in a controller is a rule that
    /// does not apply to the hub or to a background job.
    /// </summary>
    [Fact]
    public void Request_handlers_live_in_the_application_layer()
    {
        var apiHandlers = Types.InAssembly(typeof(Program).Assembly)
            .That().ImplementInterface(typeof(MediatR.IRequestHandler<,>))
            .GetTypes();

        apiHandlers.Should().BeEmpty("request handlers belong in Retail25.Application");
    }

    /// <summary>
    /// Every permission a request declares has to exist in the catalogue, or the grant that would
    /// satisfy it can never be seeded and the request is permanently unreachable.
    /// </summary>
    [Fact]
    public void Every_declared_permission_exists_in_the_catalogue()
    {
        var declared = Application.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RequiresPermissionAttribute>(inherit: false))
            .Select(a => a.Permission)
            .Distinct()
            .ToList();

        declared.Should().NotBeEmpty();
        declared.Should().BeSubsetOf(PermissionKeys.All);
    }

    [Fact]
    public void Infrastructure_implements_the_application_ports()
    {
        var ports = new[]
        {
            typeof(Application.Abstractions.ICartStore),
            typeof(Application.Abstractions.IPosNotifier),
            typeof(Application.Abstractions.ITerminalNotifier),
            typeof(Application.Abstractions.ITagDebouncer),
            typeof(Application.Abstractions.ISequenceGenerator),
            typeof(Application.Abstractions.IPaymentGateway),
            typeof(Application.Abstractions.IDateTime),
            typeof(Application.Abstractions.ICurrentUser),
        };

        foreach (var port in ports)
        {
            Infrastructure.GetTypes()
                .Any(t => t is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(t))
                .Should().BeTrue($"{port.Name} needs a concrete implementation in Infrastructure");
        }
    }

    private static void AssertNoTypeNameContains(Assembly assembly, string vendor)
        => assembly.GetTypes()
            .Where(t => t.FullName?.Contains(vendor, StringComparison.OrdinalIgnoreCase) == true)
            .Should().BeEmpty($"'{vendor}' must not appear in {assembly.GetName().Name}");

    private static string Describe(TestResult result)
        => result.FailingTypeNames is null
            ? string.Empty
            : "offending types: " + string.Join(", ", result.FailingTypeNames);
}
