using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Retail25.Infrastructure.Identity;

namespace Retail25.Infrastructure.Persistence.Seeding;

/// <summary>
/// Creates the roles and the first administrator.
/// <para>
/// The legacy system had four numbered access levels and a warning in capitals that losing the
/// level-4 password locks you out of the setup screen (guide p.81). The roles below are those
/// levels by name; authorisation itself is by permission, so the roles are presets rather than the
/// mechanism.
/// </para>
/// </summary>
public sealed class IdentitySeeder
{
    /// <summary>
    /// Roles mapped from the legacy access levels, with the guide's own descriptions of what each
    /// one may do (p.82).
    /// </summary>
    private static readonly (string Name, int Level, string Description)[] Roles =
    [
        ("Trainee", 0, "Can practise at the till. Nothing is saved."),
        ("Cashier", 1, "Can make sales at the point of sale only."),
        ("Clerk", 2, "Can add and change records, but cannot delete or authorise voids."),
        ("Supervisor", 3, "Everything except creating users and changing configuration."),
        ("Administrator", 4, "Unrestricted."),
    ];

    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        IConfiguration configuration,
        ILogger<IdentitySeeder> logger)
    {
        _users = users;
        _roles = roles;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var (name, level, description) in Roles)
        {
            if (await _roles.RoleExistsAsync(name))
            {
                continue;
            }

            await _roles.CreateAsync(new ApplicationRole
            {
                Name = name,
                LegacyLevel = level,
                Description = description,
            });

            _logger.LogInformation("Seeded role {Role} (legacy level {Level}).", name, level);
        }

        await SeedAdministratorAsync(ct);
    }

    private async Task SeedAdministratorAsync(CancellationToken ct)
    {
        var email = _configuration["Seed:AdminEmail"];
        var password = _configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "No administrator seeded: set Seed:AdminEmail and Seed:AdminPassword. " +
                "Until an administrator exists, nobody can sign in.");
            return;
        }

        if (await _users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = _configuration["Seed:AdminDisplayName"] ?? "Administrator",
        };

        var created = await _users.CreateAsync(admin, password);

        if (!created.Succeeded)
        {
            // Almost always the password policy. Say which rule failed rather than "seeding failed".
            _logger.LogError(
                "Could not seed the administrator: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await _users.AddToRoleAsync(admin, "Administrator");
        _logger.LogInformation("Seeded administrator {Email}.", email);
    }
}
