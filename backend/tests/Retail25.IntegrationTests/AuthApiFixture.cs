using Microsoft.AspNetCore.Hosting;
using Npgsql;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Retail25.Application.Abstractions;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The whole API, running, against a real PostgreSQL and a real Redis.
/// <para>
/// The auth endpoints cannot be tested any other way. What they do is almost entirely interaction
/// with ASP.NET Core Identity — password validation, token generation, security stamps, lockout —
/// and a substituted <c>UserManager</c> would only assert that the test's own fake behaves as the
/// test expects. The interesting failures are in the seams: a reset token that does not survive the
/// URL, a migration the Identity tables need, a rate limiter that rejects the second request.
/// </para>
/// <para>
/// Its own containers rather than <see cref="PostgresFixture"/>'s: this one runs migrations and
/// seeds an administrator, and the query-translation suite asserts against a database it populated
/// itself. Sharing one would make each suite's fixtures the other's mystery rows.
/// </para>
/// </summary>
public sealed class AuthApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// The administrator the API seeds from configuration. A test constant, generated here rather
    /// than written into a settings file so it cannot escape into a real deployment.
    /// </summary>
    /// <para>
    /// Unique per run. The seeder skips an address that already has an account — correctly, it must
    /// not reset a live administrator's password on every restart — so a fixed address would seed
    /// once and then silently reuse whatever password the first run happened to pick. That only bites
    /// on the fallback path, where the database survives between runs, and it fails as a wrong
    /// password rather than as anything that points at the cause.
    /// </para>
    public static readonly string AdminEmail = $"integration-admin-{Guid.NewGuid():N}@retail25.test";

    public static readonly string AdminPassword = $"Integration!{Guid.NewGuid():N}";

    /// <summary>
    /// The database these tests build and throw away. Named, not random, so a developer can look at
    /// it after a failure — and dropped and recreated on start, so a leftover from a previous run
    /// cannot make a test pass on rows it did not write.
    /// </summary>
    private const string TestDatabase = "retail25_auth_tests";

    /// <summary>
    /// Same escape hatch <see cref="PostgresFixture"/> offers: point at a PostgreSQL that already
    /// exists instead of starting a container, for a machine where Docker cannot run. CI has a real
    /// daemon and takes the container path.
    /// </summary>
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

        // Forces the host to build now rather than on the first request, so a startup failure is
        // reported by the fixture instead of by whichever test happened to run first.
        _ = Services;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }
    }

    /// <summary>
    /// Drops and recreates the test database on a server we do not own.
    /// <para>
    /// A container is thrown away wholesale; a shared server is not, so the reset has to be explicit.
    /// It targets one named database and never the one in the connection string it was given — the
    /// environment variable points at a server, and dropping whatever database happened to be named
    /// on it would be an unpleasant surprise.
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
            // The role cannot create databases. Rather than failing every test on a permissions
            // problem that has nothing to do with the code, fall back to the database it was pointed
            // at. That is a real trade and it is stated here rather than hidden: every test in this
            // suite generates its own unique email, so they neither collide with each other nor with
            // whatever is already in that database — but they do leave rows behind.
            //
            // The clean run is the container path, or `ALTER ROLE <role> CREATEDB`.
            Console.WriteLine(
                $"[AuthApiFixture] Cannot create '{TestDatabase}' — the role lacks CREATEDB. "
                + "Falling back to the supplied database; test accounts will be left behind. "
                + "Grant CREATEDB or run with Docker for an isolated run.");

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

                // Migrate and seed on start: the Identity tables and the seeded roles are what these
                // tests exercise.
                ["Database:Seed"] = "true",
                ["Auth:AdminEmail"] = AdminEmail,
                ["Auth:AdminPassword"] = AdminPassword,

                // Self sign-up is off by default, which is the right production posture and the wrong
                // one for a suite that has to prove the endpoint works. The disabled case gets its own
                // fixture rather than a mutable switch — a shared flag one test flips is a shared flag
                // another test fails on.
                ["Auth:SelfRegistration:Enabled"] = "true",
                ["Auth:WebOrigin"] = "http://localhost:3000",

                // No relay, and log the link instead. That is exactly what the notifier's
                // WriteToLog escape hatch is for, and it is the only way a test can see the token.
                ["Mail:WriteToLog"] = "true",
            }));

        return base.CreateHost(builder);
    }

    /// <summary>
    /// The reset link the notifier produced, without going near a log parser.
    /// <para>
    /// Registered over the real notifier so the endpoint's own call is what fills this in. Reading it
    /// out of the log text would test the log format.
    /// </para>
    /// </summary>
    public RecordingAccountNotifier Notifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccountNotifier>();
            services.AddSingleton<IAccountNotifier>(Notifier);
        });
}

/// <summary>Captures what would have been emailed, so a test can follow the link.</summary>
public sealed class RecordingAccountNotifier : IAccountNotifier
{
    private readonly List<(string Email, string Link)> _resets = [];
    private readonly List<string> _welcomes = [];

    /// <summary>Set to make the next send fail, standing in for an unconfigured relay.</summary>
    public bool ThrowOnSend { get; set; }

    public IReadOnlyList<(string Email, string Link)> Resets
    {
        get
        {
            lock (_resets)
            {
                return _resets.ToList();
            }
        }
    }

    public IReadOnlyList<string> Welcomes
    {
        get
        {
            lock (_welcomes)
            {
                return _welcomes.ToList();
            }
        }
    }

    public Task SendPasswordResetAsync(string email, string displayName, string resetLink, CancellationToken ct = default)
    {
        if (ThrowOnSend)
        {
            throw new InvalidOperationException("No mail relay is configured.");
        }

        lock (_resets)
        {
            _resets.Add((email, resetLink));
        }

        return Task.CompletedTask;
    }

    public Task SendWelcomeAsync(string email, string displayName, CancellationToken ct = default)
    {
        lock (_welcomes)
        {
            _welcomes.Add(email);
        }

        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class AuthApiCollection : ICollectionFixture<AuthApiFixture>
{
    public const string Name = "auth-api";
}
