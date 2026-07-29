using System.Diagnostics;
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
    public RequiresDockerFactAttribute()
    {
        var hasExternalServer = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("RETAIL25_TEST_PG_CONNECTION"));

        if (!DockerProbe.IsAvailable && !hasExternalServer)
        {
            Skip = "Needs a running Docker daemon, or RETAIL25_TEST_PG_CONNECTION pointed at a real PostgreSQL. Start Docker Desktop and re-run.";
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
