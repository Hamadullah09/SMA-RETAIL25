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
/// The connection string is read from <c>RETAIL25_DESIGN_CONNECTION</c> when present so
/// <c>database update</c> can be pointed at a real server, and otherwise falls back to a placeholder
/// that is never connected to during <c>migrations add</c>.
/// </para>
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    /// <summary>
    /// LocalDB, which every machine with the SQL Server tooling already has. Never connected to
    /// during <c>migrations add</c> — it only has to be a string the provider will parse.
    /// </summary>
    private const string Fallback =
        "Server=(localdb)\\MSSQLLocalDB;Database=retail25;Trusted_Connection=True;TrustServerCertificate=True";

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
