using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace RetailPlus.Migration.Tests;

/// <summary>
/// The C# reader must produce exactly what the Python oracle (tools/dbf/dbf_to_json.py) produces: for the committed
/// synthetic fixtures against their golden JSON, and for real Retail Plus data folders by running the oracle live.
/// </summary>
public sealed class OracleTests(ITestOutputHelper output)
{
    public static TheoryData<string> Fixtures => new(TestPaths.FixtureTables.Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Reader_matches_the_golden_oracle_output_for_fixture(string table)
    {
        var path = Path.Combine(TestPaths.FixtureFolder, table);
        var expected = JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(path, ".json")));
        var actual = CanonicalJson.Export(path);
        AssertSame(expected, actual, table);
    }

    [SkippableFact]
    public void Reader_matches_the_live_oracle_on_real_Retail_Plus_data()
    {
        var folders = TestPaths.SampleFolders();
        Skip.If(folders.Count == 0, "No Retail Plus data folder (set RETAILPLUS_DBF_SAMPLES, or create spike-data/baseline).");
        var python = FindPython();
        Skip.If(python is null, "Python 3 not found on PATH; it runs the oracle.");

        var oracle = Path.Combine(TestPaths.RepoRoot, "tools", "dbf", "dbf_to_json.py");
        var tables = folders.SelectMany(TestPaths.TablesIn).ToList();
        Assert.NotEmpty(tables);
        int records = 0;
        foreach (var table in tables)
        {
            var expected = JsonNode.Parse(Run(python!, $"-I \"{oracle}\" \"{table}\""));
            var actual = CanonicalJson.Export(table);
            AssertSame(expected, actual, table);
            records += actual["records"]!.AsArray().Count;
        }
        output.WriteLine($"Compared {tables.Count} tables ({records} records) in {string.Join(", ", folders)}");
    }

    private static void AssertSame(JsonNode? expected, JsonNode actual, string table)
    {
        if (JsonNode.DeepEquals(expected, actual)) return;
        Assert.Fail($"{table}: reader and oracle differ at {FirstDifference(expected, actual, "$")}");
    }

    /// <summary>Path and values of the first difference, for a readable failure message.</summary>
    private static string FirstDifference(JsonNode? a, JsonNode? b, string path)
    {
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var key in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
                if (!JsonNode.DeepEquals(oa[key], ob[key]))
                    return FirstDifference(oa[key], ob[key], $"{path}.{key}");
        }
        else if (a is JsonArray aa && b is JsonArray ab)
        {
            for (int i = 0; i < Math.Max(aa.Count, ab.Count); i++)
                if (i >= aa.Count || i >= ab.Count || !JsonNode.DeepEquals(aa[i], ab[i]))
                    return i >= aa.Count || i >= ab.Count ? $"{path}[{i}] (lengths {aa.Count} vs {ab.Count})" : FirstDifference(aa[i], ab[i], $"{path}[{i}]");
        }
        return $"{path}: oracle {a?.ToJsonString() ?? "null"}, reader {b?.ToJsonString() ?? "null"}";
    }

    private static string? FindPython()
    {
        foreach (var candidate in new[] { "python", "python3", "py" })
        {
            try
            {
                var version = Run(candidate, "--version");
                if (version.Contains("Python 3", StringComparison.Ordinal)) return candidate;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // not installed under this name
            }
        }
        return null;
    }

    private static string Run(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{file} {arguments} failed: {error.Result}");
        return output.Result + error.Result;   // python --version prints to stderr on old versions
    }
}
