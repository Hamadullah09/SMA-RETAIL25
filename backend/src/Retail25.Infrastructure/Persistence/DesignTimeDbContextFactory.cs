using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Builds a <see cref="ApplicationDbContext"/> for the <c>dotnet ef</c> tooling.
/// <para>
/// Without this, generating a migration means booting the API's host — which wants Redis, an
/// OpenIddict signing certificate and a working connection string, none of which a developer
/// scaffolding a schema change should have to have running. Migrations are a compile-time artefact
/// of the model; they need a provider, not a database.
/// </para>
/// <para>
/// The connection string is read from <c>RETAIL25_DESIGN_CONNECTION</c>, or from the
/// <c>--connection</c> switch the tooling passes through. With neither set, the fallback names a
/// LocalDB instance that deliberately does not exist: <c>migrations add</c> never opens a
/// connection, so it still works, while <c>database update</c> fails immediately instead of
/// migrating whatever happened to answer.
/// </para>
/// <para>
/// This used to fall back to a real <c>(localdb)\MSSQLLocalDB;Database=retail25</c>. On a machine
/// where the application's own database lives on a different server — a user secret pointing at
/// <c>.\SQLEXPRESS</c>, say — <c>database update</c> reported success having migrated a stale
/// LocalDB copy nobody runs, and the real database silently kept the old schema.
/// </para>
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    /// <summary>
    /// Parseable, and unreachable on purpose. See the type remarks.
    /// </summary>
    private const string Fallback =
        "Server=(localdb)\\Retail25DesignTimeOnly;Database=retail25;Trusted_Connection=True;TrustServerCertificate=True";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("RETAIL25_DESIGN_CONNECTION") ?? Fallback;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options);
    }
}
