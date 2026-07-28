using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Retail25.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> and the Visual Studio Package Manager Console build a context without
/// starting the API.
/// <para>
/// Design-time tooling otherwise has to boot the whole host — including database and Redis
/// connections — just to read the model. The connection string here is only used to decide SQL
/// dialect when scaffolding a migration; it is never connected to.
/// </para>
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string DesignTimeConnection =
        "Host=localhost;Port=5432;Database=retail25;Username=retail25;Password=design-time-only";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("RETAIL25_DESIGNTIME_CONNECTION")
                         ?? DesignTimeConnection;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options);
    }
}
