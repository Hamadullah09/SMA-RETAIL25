using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Retail25.Application.Abstractions;
using Retail25.Application.Common;
using Retail25.Domain.Security;
using Retail25.Domain.Staff;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// Seeds the permission catalogue, the five preset roles, the first administrator and the OAuth
/// clients (doc 07).
/// <para>
/// The roles mirror the legacy access levels 0–4 (guide p.82) because fifteen years of staff records
/// are mapped onto them and an import has to land somewhere sensible. They are only presets:
/// authorisation is by permission, and an administrator can reshape any role without a release.
/// </para>
/// <para>
/// Idempotent, so it is safe on every start. It adds what is missing and never revokes an existing
/// grant — an administrator who removed a permission from Supervisor should not find it back
/// tomorrow because the service restarted.
/// </para>
/// </summary>
public sealed class IdentitySeeder
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly IOpenIddictApplicationManager _applications;
    private readonly IOpenIddictScopeManager _scopes;
    private readonly IPinHasher _pinHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        IOpenIddictApplicationManager applications,
        IOpenIddictScopeManager scopes,
        IPinHasher pinHasher,
        IConfiguration configuration,
        ILogger<IdentitySeeder> logger)
    {
        _db = db;
        _users = users;
        _roles = roles;
        _applications = applications;
        _scopes = scopes;
        _pinHasher = pinHasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedPermissionsAsync(ct);
        await SeedRolesAsync(ct);
        await SeedScopesAsync(ct);
        await SeedClientsAsync(ct);
        await SeedAdministratorAsync(ct);
        await SeedCashierAsync(ct);
    }

    /// <summary>Writes any permission constant that has no row yet, so the catalogue is administrable.</summary>
    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.Select(p => p.Key).ToListAsync(ct);
        var known = new HashSet<string>(existing, StringComparer.Ordinal);

        var added = 0;

        foreach (var key in PermissionKeys.All)
        {
            if (known.Contains(key))
            {
                continue;
            }

            _db.Permissions.Add(Permission.Create(key, Describe(key), GroupOf(key), IsSensitive(key)));
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} permissions", added);
        }
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        foreach (var (level, name, description) in LegacyRoles)
        {
            var role = await _roles.FindByNameAsync(name);

            if (role is null)
            {
                role = new ApplicationRole
                {
                    Name = name,
                    NormalizedName = name.ToUpperInvariant(),
                    LegacyLevel = level,
                    Description = description,
                };

                var created = await _roles.CreateAsync(role);
                if (!created.Succeeded)
                {
                    _logger.LogError("Could not create role {Role}: {Errors}", name, Join(created));
                    continue;
                }
            }

            await GrantAsync(role, PermissionKeys.LegacyLevelPresets[level], ct);
        }
    }

    /// <summary>
    /// Adds the preset's grants that are missing. Never removes: an administrator who took a
    /// permission away from a role should not find it restored by the next restart.
    /// </summary>
    private async Task GrantAsync(ApplicationRole role, IReadOnlyList<string> permissions, CancellationToken ct)
    {
        var held = await _db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Select(rp => rp.PermissionKey)
            .ToListAsync(ct);

        var existing = new HashSet<string>(held, StringComparer.Ordinal);
        var added = 0;

        foreach (var permission in permissions.Where(p => !existing.Contains(p)))
        {
            _db.RolePermissions.Add(RolePermission.Create(role.Id, permission));
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Granted {Count} permissions to {Role}", added, role.Name);
        }
    }

    private async Task SeedScopesAsync(CancellationToken ct)
    {
        foreach (var (name, display, resource) in new[]
                 {
                     (AuthConstants.ApiScope, "Retail25 API", "retail25-api"),
                     (AuthConstants.TerminalScope, "Retail25 terminal", "retail25-api"),
                 })
        {
            if (await _scopes.FindByNameAsync(name, ct) is not null)
            {
                continue;
            }

            await _scopes.CreateAsync(
                new OpenIddictScopeDescriptor
                {
                    Name = name,
                    DisplayName = display,
                    Resources = { resource },
                },
                ct);
        }
    }

    private async Task SeedClientsAsync(CancellationToken ct)
    {
        var webOrigin = _configuration["Auth:WebOrigin"] ?? "http://localhost:3000";

        if (await _applications.FindByClientIdAsync(AuthConstants.WebClientId, ct) is null)
        {
            // Public client: the BFF runs on a server but the flow starts in a browser, and a
            // client secret in a redirect-based flow buys nothing PKCE does not already give.
            await _applications.CreateAsync(
                new OpenIddictApplicationDescriptor
                {
                    ClientId = AuthConstants.WebClientId,
                    DisplayName = "Retail25 web",
                    ClientType = OpenIddictConstants.ClientTypes.Public,
                    ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                    RedirectUris = { new Uri($"{webOrigin.TrimEnd('/')}/api/auth/callback") },
                    PostLogoutRedirectUris = { new Uri($"{webOrigin.TrimEnd('/')}/") },
                    Permissions =
                    {
                        OpenIddictConstants.Permissions.Endpoints.Authorization,
                        OpenIddictConstants.Permissions.Endpoints.Token,
                        OpenIddictConstants.Permissions.Endpoints.Logout,
                        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                        OpenIddictConstants.Permissions.ResponseTypes.Code,
                        OpenIddictConstants.Permissions.Scopes.Profile,
                        OpenIddictConstants.Permissions.Scopes.Roles,
                        OpenIddictConstants.Permissions.Prefixes.Scope + AuthConstants.ApiScope,
                    },
                    Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange },
                },
                ct);

            _logger.LogInformation("Registered the web client for {Origin}", webOrigin);
        }

        if (await _applications.FindByClientIdAsync(AuthConstants.AgentClientId, ct) is not null)
        {
            // Registered already, and deliberately not updated: rotating a shared credential on every
            // restart would knock every till in the shop offline at once.
            //
            // But the case where the configured secret no longer matches the registered one is a
            // genuine trap — every agent gets `invalid_client`, the agent's answer to a failed
            // profile fetch is to keep its defaults and carry on, and the visible symptom is a till
            // that reads no tags for no stated reason. Saying so here is the difference between a
            // one-line fix and an afternoon.
            if (!string.IsNullOrWhiteSpace(_configuration["Auth:AgentClientSecret"]))
            {
                _logger.LogInformation(
                    "The terminal agent client is already registered; its secret was left as it is. "
                    + "If agents are being refused with invalid_client, the registered secret differs "
                    + "from Auth:AgentClientSecret — delete the {ClientId} row to re-register it.",
                    AuthConstants.AgentClientId);
            }
        }
        else
        {
            var secret = _configuration["Auth:AgentClientSecret"];

            if (string.IsNullOrWhiteSpace(secret))
            {
                // Never invented and never defaulted: a generated secret would be logged or silently
                // shared across every till, and a default one is a published credential.
                _logger.LogWarning(
                    "Auth:AgentClientSecret is not configured, so the terminal agent client was not registered");
                return;
            }

            await _applications.CreateAsync(
                new OpenIddictApplicationDescriptor
                {
                    ClientId = AuthConstants.AgentClientId,
                    ClientSecret = secret,
                    DisplayName = "Retail25 terminal agent",
                    ClientType = OpenIddictConstants.ClientTypes.Confidential,
                    Permissions =
                    {
                        OpenIddictConstants.Permissions.Endpoints.Token,
                        OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                        OpenIddictConstants.Permissions.Prefixes.Scope + AuthConstants.TerminalScope,
                    },
                },
                ct);

            _logger.LogInformation("Registered the terminal agent client");
        }
    }

    /// <summary>
    /// Creates the first administrator, from configuration only.
    /// <para>
    /// There is no fallback password. A seeded default credential is a published credential — it ends
    /// up in a screenshot, a runbook, or a shop that never changed it — so an unconfigured deployment
    /// gets a loud warning and no account instead.
    /// </para>
    /// </summary>
    private async Task SeedAdministratorAsync(CancellationToken ct)
    {
        var email = _configuration["Auth:AdminEmail"];
        var password = _configuration["Auth:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            if (!await _users.Users.AnyAsync(ct))
            {
                _logger.LogWarning(
                    "No users exist and Auth:AdminEmail / Auth:AdminPassword are not set, so nobody can sign in. "
                    + "Set both and restart.");
            }

            return;
        }

        await SeedOperatorAsync(
            email,
            password,
            _configuration["Auth:AdminDisplayName"] ?? "Administrator",
            role: "Administrator",
            staffCode: "ADM",
            given: "System",
            family: "Administrator",
            accessLevel: 4,
            pin: _configuration["Auth:AdminPin"],
            ct);
    }

    /// <summary>
    /// Creates a till operator for demonstration and testing, from configuration only.
    /// <para>
    /// Same rule as the administrator: no default password, so an unconfigured deployment simply
    /// does not get this account. It exists because most of what there is to try out — ringing a
    /// sale, a shift, a commission — cannot be exercised from an administrator login, and reviewers
    /// otherwise end up granting themselves cashier rights just to see the till.
    /// </para>
    /// </summary>
    private async Task SeedCashierAsync(CancellationToken ct)
    {
        var email = _configuration["Auth:CashierEmail"];
        var password = _configuration["Auth:CashierPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await SeedOperatorAsync(
            email,
            password,
            _configuration["Auth:CashierDisplayName"] ?? "Cashier",
            role: "Cashier",
            staffCode: "CSH",
            given: "Demo",
            family: "Cashier",
            accessLevel: 1,
            pin: _configuration["Auth:CashierPin"],
            ct);
    }

    private async Task SeedOperatorAsync(
        string email,
        string password,
        string displayName,
        string role,
        string staffCode,
        string given,
        string family,
        int accessLevel,
        string? pin,
        CancellationToken ct)
    {
        if (await _users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var location = await _db.Locations.AsNoTracking().Select(l => (long?)l.Id).FirstOrDefaultAsync(ct);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            DefaultLocationId = location,
        };

        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            _logger.LogError("Could not create the {Role} account: {Errors}", role, Join(created));
            return;
        }

        await _users.AddToRoleAsync(user, role);

        // A staff profile as well, so the account can actually work a till: sales are attributed to
        // staff, not to Identity users.
        var staff = StaffProfile.Create(user.Id, await FreeStaffCodeAsync(staffCode, ct), given, family, accessLevel);

        if (!string.IsNullOrWhiteSpace(pin) && pin.Trim().Length >= 4)
        {
            staff.SetPin(_pinHasher.Hash(pin.Trim()));
        }

        _db.StaffProfiles.Add(staff);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created the {Role} account {Email}", role, email);
    }

    /// <summary>
    /// The preferred staff code, or the first free variant of it.
    /// <para>
    /// Staff codes are unique, and this seeder wants "ADM" and "CSH" — which works exactly once. A
    /// deployment that seeds a second administrator under a different email, or a test database that
    /// survives between runs, hits a constraint violation during startup and the API refuses to boot.
    /// The account is what matters here; the code is a label, so it yields.
    /// </para>
    /// </summary>
    private async Task<string> FreeStaffCodeAsync(string preferred, CancellationToken ct)
    {
        var taken = await _db.StaffProfiles
            .Where(s => s.StaffCode.StartsWith(preferred))
            .Select(s => s.StaffCode)
            .ToListAsync(ct);

        if (!taken.Contains(preferred, StringComparer.OrdinalIgnoreCase))
        {
            return preferred;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{preferred}{suffix}");

            if (!taken.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        // A thousand administrators is not a real situation; falling back to something certainly
        // unique beats throwing during startup.
        return string.Create(CultureInfo.InvariantCulture, $"{preferred}{Guid.NewGuid():N}")[..8].ToUpperInvariant();
    }

    /// <summary>The legacy access levels (guide p.82), kept so migrated staff land somewhere sensible.</summary>
    private static readonly (int Level, string Name, string Description)[] LegacyRoles =
    [
        (0, "Trainee", "Practice at the till. Nothing is committed."),
        (1, "Cashier", "Sell at the till."),
        (2, "Clerk", "Sell, and add or edit records. No deletes, no void authorisation."),
        (3, "Supervisor", "Everything except user management and system setup."),
        (4, "Administrator", "Unrestricted."),
    ];

    private static string GroupOf(string key) => key.Split('.')[0] switch
    {
        "pos" => "Point of sale",
        "drawer" => "Cash drawer",
        "catalog" => "Catalogue",
        "inventory" => "Inventory",
        "customer" => "Customers",
        "ar" => "Receivables",
        "purchasing" => "Purchasing",
        "staff" => "Staff",
        "reports" => "Reports",
        "settings" => "Settings",
        "terminals" => "Terminals",
        _ => "System",
    };

    /// <summary>Permissions that move money, destroy data or change who can do what.</summary>
    private static bool IsSensitive(string key) => key is
        PermissionKeys.Pos.VoidSale or
        PermissionKeys.Pos.PriceOverride or
        PermissionKeys.Pos.TaxOverride or
        PermissionKeys.Catalog.Delete or
        PermissionKeys.Customer.Delete or
        PermissionKeys.Ar.VoidInvoice or
        PermissionKeys.Ar.Refund or
        PermissionKeys.Inventory.YearEnd or
        PermissionKeys.System.UsersManage or
        PermissionKeys.System.MigrationRun or
        PermissionKeys.Settings.Taxes;

    /// <summary>Turns <c>pos.void_sale</c> into "Void sale", so the role editor is readable.</summary>
    private static string Describe(string key)
    {
        var action = key.Split('.').Last().Replace('_', ' ');
        return char.ToUpperInvariant(action[0]) + action[1..];
    }

    private static string Join(IdentityResult result)
        => string.Join("; ", result.Errors.Select(e => e.Description));
}
