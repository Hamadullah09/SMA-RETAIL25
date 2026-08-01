using System.Diagnostics;
using Npgsql;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// A <see cref="FactAttribute"/> that skips when there is no Docker daemon to run a container on.
/// <para>
/// These tests need real PostgreSQL, and a developer without Docker Desktop running should get a
/// clear "skipped, needs Docker" rather than thirteen red failures that say nothing about their
/// change. The alternative — leaving them to fail — trains people to ignore a red suite, which costs
/// far more than the coverage is worth.
/// </para>
/// <para>
/// CI provides the daemon, so nothing is skipped there. If these ever start skipping in CI, that is
/// a broken pipeline and should be treated as one.
/// </para>
/// </summary>
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute() => Skip = TestDatabase.SkipReasonForSharedDatabase;
}

/// <summary>The <see cref="TheoryAttribute"/> twin of <see cref="RequiresDockerFactAttribute"/>.</summary>
public sealed class RequiresDockerTheoryAttribute : TheoryAttribute
{
    public RequiresDockerTheoryAttribute() => Skip = TestDatabase.SkipReasonForSharedDatabase;
}

/// <summary>
/// For a suite that needs a database <em>of its own</em>, not merely a database.
/// <para>
/// <see cref="PostgresFixture"/>'s tests assert that a migration applies to a <em>clean</em> schema,
/// which cannot be answered on a database that already has one. Those tests therefore need the
/// server to let them create one — a container always does; an external server only does if the role
/// holds <c>CREATEDB</c>.
/// </para>
/// <para>
/// Distinguishing this from <see cref="RequiresDockerFactAttribute"/> matters because the two answers
/// differ: the scenario suites degrade gracefully onto a shared database and still test what they
/// claim to, while these cannot, and a suite that fails on a missing privilege tells nobody anything
/// about the code.
/// </para>
/// </summary>
public sealed class RequiresIsolatedDatabaseFactAttribute : FactAttribute
{
    public RequiresIsolatedDatabaseFactAttribute() => Skip = TestDatabase.SkipReasonForOwnDatabase;
}

/// <summary>What the environment can actually provide, probed once.</summary>
internal static class TestDatabase
{
    private static readonly string? External =
        Environment.GetEnvironmentVariable("RETAIL25_TEST_PG_CONNECTION");

    private static readonly Lazy<bool> CanCreate = new(ProbeCreateDatabase, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Null when a suite that can share a database is able to run.</summary>
    public static string? SkipReasonForSharedDatabase =>
        DockerProbe.IsAvailable || !string.IsNullOrWhiteSpace(External)
            ? null
            : "Needs a running Docker daemon, or RETAIL25_TEST_PG_CONNECTION pointed at a real PostgreSQL.";

    /// <summary>Null when a suite that must provision its own database is able to run.</summary>
    public static string? SkipReasonForOwnDatabase
    {
        get
        {
            if (DockerProbe.IsAvailable)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(External))
            {
                return "Needs a running Docker daemon, or RETAIL25_TEST_PG_CONNECTION pointed at a real PostgreSQL.";
            }

            return CanCreate.Value
                ? null
                : "RETAIL25_TEST_PG_CONNECTION is set but its role cannot CREATE DATABASE, and this suite needs a "
                  + "database of its own to prove a migration applies to a clean schema. "
                  + "Run `ALTER ROLE <role> CREATEDB;` as a superuser, or start Docker.";
        }
    }

    private static bool ProbeCreateDatabase()
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(External!) { Database = "postgres" };

            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();

            using var command = new NpgsqlCommand(
                "SELECT rolcreatedb OR rolsuper FROM pg_roles WHERE rolname = current_user", connection);

            return command.ExecuteScalar() is true;
        }
        catch (Exception)
        {
            // Unreachable, wrong credentials, no such role — all the same answer here.
            return false;
        }
    }
}

internal static class DockerProbe
{
    private static readonly Lazy<bool> Probe = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    public static bool IsAvailable => Probe.Value;

    /// <summary>
    /// Asks the Docker CLI whether a daemon is answering. Shelling out once is cheap and it is the
    /// same question the container library will ask a moment later — checking for the socket file by
    /// hand gets the answer wrong on Windows, WSL and rootless setups in three different ways.
    /// </summary>
    private static bool Detect()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info --format {{.ServerVersion}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(15_000))
            {
                return false;
            }

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception)
        {
            // No docker on PATH, no permission, no daemon — all the same answer here.
            return false;
        }
    }
}
