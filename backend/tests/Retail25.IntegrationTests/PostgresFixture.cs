using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// A real PostgreSQL 16 for the tests that cannot be answered without one.
/// <para>
/// The unit suites run against the in-memory provider, which is the right trade for handler
/// behaviour but is silent about two whole classes of defect: a LINQ expression that cannot be
/// translated to SQL (it just runs client-side instead), and anything to do with the actual schema —
/// value converters, owned types, constraints, sequences. Both have already produced bugs in this
/// codebase that the unit tests passed straight through.
/// </para>
/// <para>
/// One container for the whole collection, because starting Postgres costs seconds and the tests
/// here do not need isolation from each other — each seeds the rows it asserts on.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>
    /// Points the suite at a server that already exists instead of starting a container — for a
    /// machine where Docker cannot run at all (no nested virtualization) but a real PostgreSQL is
    /// reachable some other way. Not used by CI; CI has a real Docker daemon and gets full isolation
    /// from Testcontainers instead.
    /// </summary>
    private static readonly string? ExternalConnectionString =
        Environment.GetEnvironmentVariable("RETAIL25_TEST_PG_CONNECTION");

    private readonly PostgreSqlContainer? _container = ExternalConnectionString is null
        ? new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("retail25_tests")
            .WithUsername("retail25")
            .WithPassword("retail25")
            .Build()
        : null;

    public string ConnectionString => ExternalConnectionString ?? _container!.GetConnectionString();

    /// <summary>
    /// Starts the container only when a daemon is there to start it on. Without this the fixture
    /// throws during collection setup and every test in the collection reports as failed rather than
    /// skipped, which is the wrong answer to "you do not have Docker running".
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_container is not null && DockerProbe.IsAvailable)
        {
            await _container.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null && DockerProbe.IsAvailable)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// A context wired exactly as the application wires it — same naming convention, same migrations
    /// assembly, same auditing interceptor. A fixture that configured it differently would prove
    /// something about the fixture rather than about the system.
    /// </summary>
    public ApplicationDbContext CreateContext(string? connectionString = null)
    {
        var clock = Substitute.For<IDateTime>();
        clock.Now.Returns(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(TestIds.Next());

        var interceptor = new AuditingInterceptor(currentUser, Substitute.For<IRequestContext>(), clock);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString ?? ConnectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;

        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Creates an empty database on the running server and returns its connection string, so a test
    /// can prove something about a database nothing has touched yet.
    /// <para>
    /// Several tests reuse the same database name across the life of the process (one per test
    /// class, recreated on every test method), and Npgsql pools physical connections per connection
    /// string rather than per <see cref="ApplicationDbContext"/> instance. Disposing a context
    /// returns its connection to that pool as idle rather than closing the socket — so the next test
    /// against the same name would otherwise reuse a live-looking connection whose backend process
    /// <c>DROP DATABASE ... WITH (FORCE)</c> has just killed server-side, and fail with a raw socket
    /// reset instead of a database error. Clearing the pool for this exact connection string forces
    /// the next checkout to open a genuinely new connection.
    /// </para>
    /// </summary>
    public async Task<string> CreateEmptyDatabaseAsync(string name)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name };
        var targetConnectionString = builder.ConnectionString;

        await using (var target = new NpgsqlConnection(targetConnectionString))
        {
            NpgsqlConnection.ClearPool(target);
        }

        await using var admin = CreateContext();

#pragma warning disable EF1002 // The name is generated by the caller from a test method name.
        await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{name}\"");
#pragma warning restore EF1002

        return targetConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
