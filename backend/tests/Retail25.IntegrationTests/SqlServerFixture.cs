using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// A real SQL Server for the tests that cannot be answered without one.
/// <para>
/// The unit suites run against the in-memory provider, which is the right trade for handler
/// behaviour but is silent about two whole classes of defect: a LINQ expression that cannot be
/// translated to SQL (it just runs client-side instead), and anything to do with the actual schema —
/// value converters, owned types, constraints, sequences. Both have already produced bugs in this
/// codebase that the unit tests passed straight through.
/// </para>
/// <para>
/// One container for the whole collection, because starting SQL Server costs seconds and the tests
/// here do not need isolation from each other — each seeds the rows it asserts on.
/// </para>
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>
    /// Points the suite at a server that already exists instead of starting a container — for a
    /// machine where Docker cannot run at all (no nested virtualization) but a real SQL Server is
    /// reachable some other way. Not used by CI; CI has a real Docker daemon and gets full isolation
    /// from Testcontainers instead.
    /// </summary>
    private static readonly string? ExternalConnectionString =
        Environment.GetEnvironmentVariable("RETAIL25_TEST_SQL_CONNECTION");

    private readonly MsSqlContainer? _container = ExternalConnectionString is null
        ? new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
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
            .UseSqlServer(
                connectionString ?? ConnectionString,
                sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
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
    /// class, recreated on every test method), and the client pools physical connections per
    /// connection string rather than per <see cref="ApplicationDbContext"/> instance. Disposing a
    /// context returns its connection to that pool as idle rather than closing the socket — so the
    /// next test against the same name would otherwise check out a live-looking connection to a
    /// database that no longer exists, and fail on a connection error rather than anything to do
    /// with the test. <see cref="SqlServerDatabases.RecreateAsync"/> clears the pool for exactly
    /// that connection string; it also has to evict live sessions before the drop, which SQL Server
    /// will not do for itself.
    /// </para>
    /// </summary>
    public Task<string> CreateEmptyDatabaseAsync(string name)
        => SqlServerDatabases.RecreateAsync(ConnectionString, name);
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sqlserver";
}
