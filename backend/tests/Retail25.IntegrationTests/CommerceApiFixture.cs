using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Testcontainers.PostgreSql;
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
/// the real application: real MediatR pipeline, real handlers, real EF Core against real Postgres,
/// real money arithmetic.
/// </para>
/// </summary>
public sealed class CommerceApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestDatabase = "retail25_commerce_tests";

    private static readonly string? ExternalPostgres =
        Environment.GetEnvironmentVariable("RETAIL25_TEST_PG_CONNECTION");

    private static readonly string? ExternalRedis =
        Environment.GetEnvironmentVariable("RETAIL25_TEST_REDIS");

    private readonly PostgreSqlContainer? _postgres = ExternalPostgres is null
        ? new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase(TestDatabase)
            .WithUsername("retail25")
            .WithPassword("retail25")
            .Build()
        : null;

    private readonly RedisContainer? _redis = ExternalRedis is null
        ? new RedisBuilder().WithImage("redis:7-alpine").Build()
        : null;

    private string _postgresConnection = string.Empty;

    /// <summary>The acting user. Mutable so a scenario can act as a specific staff member.</summary>
    public TestCurrentUser ActingUser { get; } = new();

    public async Task InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
            _postgresConnection = _postgres.GetConnectionString();
        }
        else
        {
            _postgresConnection = await PrepareExternalDatabaseAsync(ExternalPostgres!);
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

        if (_postgres is not null) await _postgres.DisposeAsync();
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
        var builder = new NpgsqlConnectionStringBuilder(adminConnection) { Database = "postgres" };

        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            await using (var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{TestDatabase}\" WITH (FORCE)", connection))
            {
                await drop.ExecuteNonQueryAsync();
            }

            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{TestDatabase}\"", connection);
            await create.ExecuteNonQueryAsync();

            return new NpgsqlConnectionStringBuilder(adminConnection) { Database = TestDatabase }.ConnectionString;
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            // Same trade as AuthApiFixture, and a worse one here: these scenarios assert on totals,
            // so sharing a database with existing data means an assertion can pass for the wrong
            // reason. Every scenario therefore creates its own customer and its own product.
            Console.WriteLine(
                $"[CommerceApiFixture] Cannot create '{TestDatabase}' — the role lacks CREATEDB. "
                + "Falling back to the supplied database. Grant CREATEDB or run with Docker for isolation.");

            return adminConnection;
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgresConnection,
                ["ConnectionStrings:Redis"] = _redis?.GetConnectionString() ?? ExternalRedis!,
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
}

/// <summary>An acting user holding every permission the catalogue defines.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; } = Guid.NewGuid();

    public Guid? StaffId { get; set; }

    public Guid? StationId { get; set; }

    public Guid? LocationId { get; set; }

    public bool IsAuthenticated => true;

    public IReadOnlySet<string> Permissions { get; } =
        new HashSet<string>(PermissionKeys.All, StringComparer.Ordinal);
}

[CollectionDefinition(Name)]
public sealed class CommerceApiCollection : ICollectionFixture<CommerceApiFixture>
{
    public const string Name = "commerce-api";
}
