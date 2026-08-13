using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The whole application, wired to a real PostgreSQL, with the acting user substituted.
/// <para>
/// This exists to run <em>business scenarios</em> rather than HTTP contracts. The phases these tests
/// close ask questions like "does a purchase order, received with freight, sold on account and part
/// paid, leave the customer owing the right amount" — which is about handlers, money and the
/// database, and not about who was allowed to press the button. Authorisation has its own coverage;
/// making every scenario here perform an OIDC dance would add a second thing that can fail without
/// testing anything the auth suite does not already.
/// </para>
/// <para>
/// So <see cref="ICurrentUser"/> is replaced with one holding every permission. Everything else is
/// the real application: real MediatR pipeline, real handlers, real EF Core against real SQL Server,
/// real money arithmetic.
/// </para>
/// </summary>
public sealed class CommerceApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestDatabase = "retail25_commerce_tests";

    private static readonly string? ExternalSqlServer =
        Environment.GetEnvironmentVariable("RETAIL25_TEST_SQL_CONNECTION");

    private static readonly string? ExternalRedis =
        Environment.GetEnvironmentVariable("RETAIL25_TEST_REDIS");

    // No WithDatabase/WithUsername: the SQL Server image ships a fixed `sa` login and creates no
    // user database, so the fixture's own database is created below like any other server's.
    private readonly MsSqlContainer? _sqlServer = ExternalSqlServer is null
        ? new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build()
        : null;

    /// <summary>
    /// A Redis container when Docker can provide one, and nothing at all when it cannot.
    /// <para>
    /// Without the Docker check this constructor threw on a bench that had PostgreSQL but no daemon —
    /// so the whole collection reported nine failures where it should have reported "cannot run here".
    /// A distributed cache is not what these scenarios are about; when there is no Redis to be had the
    /// application's own in-process provider stands in, and the scenarios still test what they claim to.
    /// </para>
    /// </summary>
    private readonly RedisContainer? _redis = ExternalRedis is null && DockerProbe.IsAvailable
        ? new RedisBuilder().WithImage("redis:7-alpine").Build()
        : null;

    private string _sqlConnection = string.Empty;

    /// <summary>
    /// Where this fixture's database actually is, for a test that needs to stand a second, separate
    /// service provider over the same data — the closest a test gets to "the process restarted".
    /// </summary>
    public string ConnectionString => _sqlConnection;

    /// <summary>The acting user. Mutable so a scenario can act as a specific staff member.</summary>
    public TestCurrentUser ActingUser { get; } = new();

    public async Task InitializeAsync()
    {
        if (_sqlServer is not null)
        {
            await _sqlServer.StartAsync();
            _sqlConnection = _sqlServer.GetConnectionString();
        }
        else
        {
            _sqlConnection = await PrepareExternalDatabaseAsync(ExternalSqlServer!);
        }

        if (_redis is not null)
        {
            await _redis.StartAsync();
        }

        _ = Services;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (_sqlServer is not null) await _sqlServer.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    /// <summary>
    /// Drops and recreates the scenario database on a server we do not own.
    /// <para>
    /// A fresh database every run is not fussiness here: these scenarios assert on totals — "the
    /// customer owes exactly this" — and a leftover invoice from a previous run makes that assertion
    /// either fail or, worse, pass for the wrong reason.
    /// </para>
    /// </summary>
    private static async Task<string> PrepareExternalDatabaseAsync(string adminConnection)
    {
        try
        {
            return await SqlServerDatabases.RecreateAsync(adminConnection, TestDatabase);
        }
        catch (SqlException error) when (SqlServerDatabases.IsPermissionError(error))
        {
            // Same trade as AuthApiFixture, and a worse one here: these scenarios assert on totals,
            // so sharing a database with existing data means an assertion can pass for the wrong
            // reason. Every scenario therefore creates its own customer and its own product.
            Console.WriteLine(
                $"[CommerceApiFixture] Cannot create '{TestDatabase}' — the login may not create databases. "
                + "Falling back to the supplied database. Grant dbcreator or run with Docker for isolation.");

            return adminConnection;
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _sqlConnection,
                ["ConnectionStrings:Redis"] = _redis?.GetConnectionString() ?? ExternalRedis ?? string.Empty,

                // SQL Server when there is no Redis, because that is what a shop without Redis
                // actually runs — and because the in-process store is a dictionary that cannot fail
                // the way a database can. Running these scenarios against it meant SqlCartStore,
                // SqlIdempotencyStore and SqlTagDebouncer had no test at all, and a sale that could
                // not be completed on the hosted deployment passed here every time.
                ["Cache:Provider"] = _redis is null && ExternalRedis is null ? "SqlServer" : "Redis",

                ["Database:Seed"] = "true",

                // The scenarios need a location, a currency, tax rows, tenders and number sequences.
                // Those come from the store seed; the demo catalogue is deliberately left off so a
                // scenario's own figures are the only figures in the database.
                ["Demo:SeedCatalogue"] = "false",
                ["Mail:WriteToLog"] = "true",
            }));

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(ActingUser);
        });

    /// <summary>Opens a scope and hands back a sender. Every scenario step is its own scope.</summary>
    public IServiceScope Scope() => Services.CreateScope();

    private readonly SemaphoreSlim _scenarioGate = new(1, 1);
    private object? _scenario;

    /// <summary>
    /// Builds a scenario once for the whole collection, however many tests ask for it.
    /// <para>
    /// xUnit constructs a fresh test-class instance per test method, so anything set up in
    /// <c>IAsyncLifetime.InitializeAsync</c> runs once <em>per test</em>. For a scenario that rings
    /// sales through the till that is not merely wasteful — the takings accumulate, and a report
    /// asserting "the total is £115" sees £115, then £230, then £345 as the suite proceeds. Which is
    /// exactly how this was first written, and exactly how it failed.
    /// </para>
    /// </summary>
    public async Task<T> ScenarioAsync<T>(Func<IServiceScope, Task<T>> build) where T : class
    {
        if (_scenario is T ready)
        {
            return ready;
        }

        await _scenarioGate.WaitAsync();

        try
        {
            if (_scenario is T built)
            {
                return built;
            }

            using var scope = Scope();
            var created = await build(scope);

            _scenario = created;
            return created;
        }
        finally
        {
            _scenarioGate.Release();
        }
    }
}

/// <summary>
/// An acting user holding every permission the catalogue defines — except when a test says
/// otherwise.
/// <para>
/// The permission set is settable so a test can act as a cashier or a trainee and find out what the
/// server actually refuses. Held every permission and no way to hold fewer, the suite could only
/// ever prove that an administrator can do things, which is the half nobody worries about.
/// </para>
/// </summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public long? UserId { get; set; } = TestIds.Next();

    public long? StaffId { get; set; }

    public long? StationId { get; set; }

    public long? LocationId { get; set; }

    public bool IsAuthenticated => true;

    public IReadOnlySet<string> Permissions { get; set; } =
        new HashSet<string>(PermissionKeys.All, StringComparer.Ordinal);

    /// <summary>Acts as somebody holding exactly these permissions, and hands back what to restore.</summary>
    public IReadOnlySet<string> ActAs(IEnumerable<string> permissions)
    {
        var previous = Permissions;
        Permissions = new HashSet<string>(permissions, StringComparer.Ordinal);

        return previous;
    }
}

[CollectionDefinition(Name)]
public sealed class CommerceApiCollection : ICollectionFixture<CommerceApiFixture>
{
    public const string Name = "commerce-api";
}
