namespace RetailPlus.Migration.Tests;

internal static class TestPaths
{
    /// <summary>The repository root (the folder holding RetailPlus.sln).</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string FixtureFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "dbf");

    public static IEnumerable<string> FixtureTables =>
        Directory.EnumerateFiles(FixtureFolder, "*.dbf").OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>
    /// Real Retail Plus data folders for the live oracle run: RETAILPLUS_DBF_SAMPLES (folders separated by ';'),
    /// otherwise the git-ignored spike copies if they exist. These hold vendor sample data and never go into git.
    /// </summary>
    public static IReadOnlyList<string> SampleFolders()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPLUS_DBF_SAMPLES");
        var candidates = configured is { Length: > 0 }
            ? configured.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [Path.Combine(RepoRoot, "spike-data", "baseline"), Path.Combine(RepoRoot, "spike-data", "run2")];
        return candidates.Where(Directory.Exists).ToList();
    }

    /// <summary>DBF tables in a data folder: *.DBF everywhere, plus PO files, which use the location code as extension.</summary>
    public static IEnumerable<string> TablesIn(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).Equals(".dbf", StringComparison.OrdinalIgnoreCase)
                        || (Path.GetFileName(f).StartsWith("PO", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "PO", StringComparison.OrdinalIgnoreCase)
                            && Path.GetExtension(f).Length == 4
                            && !Path.GetExtension(f).Equals(".fpt", StringComparison.OrdinalIgnoreCase)
                            && !Path.GetExtension(f).Equals(".cdx", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "RetailPlus.sln")))
                return dir.FullName;
        throw new InvalidOperationException("RetailPlus.sln not found above " + AppContext.BaseDirectory);
    }
}
