using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Persistence;

namespace Retail25.Infrastructure.Identity;

/// <summary>Client ids and scopes, in one place so the seeder and the token handler agree.</summary>
public static class AuthConstants
{
    /// <summary>The Next.js BFF. Public client — it cannot keep a secret, so PKCE is mandatory.</summary>
    public const string WebClientId = "retail25-web";

    /// <summary>The terminal agent. Confidential: it runs as a service and can hold a credential.</summary>
    public const string AgentClientId = "retail25-agent";

    public const string ApiScope = "retail25.api";
    public const string TerminalScope = "retail25.terminal";

    public const string PermissionClaim = "permission";
    public const string StaffIdClaim = "staff_id";
    public const string StationIdClaim = "station_id";
    public const string LocationIdClaim = "location_id";
    public const string AccessLevelClaim = "access_level";

    /// <summary>
    /// Fifteen minutes. Short enough that a leaked access token has a small window, long enough that
    /// a till is not renewing constantly mid-queue (doc 07).
    /// </summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Eight hours — one trading day, so a cashier signs in once per shift.</summary>
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromHours(8);
}

public static class IdentityRegistration
{
    /// <summary>The authorization policy hub classes use — see the registration below for why.</summary>
    public const string HubAuthorizationPolicy = "HubTicket";

    /// <summary>
    /// ASP.NET Core Identity plus OpenIddict, configured for authorization code with PKCE and
    /// rotating refresh tokens (doc 07).
    /// </summary>
    public static IServiceCollection AddIdentityAndOpenIddict(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                // Lockout is on by default: an unlimited password prompt on a machine sitting in a
                // shop is a machine anyone can work on all afternoon.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        // Without this, AddIdentity's own default UserClaimsPrincipalFactory is what actually runs —
        // ApplicationClaimsPrincipalFactory below is fully implemented but inert unless it explicitly
        // overrides the interface Identity resolves. Every permission, staff-id and location claim
        // this app's authorization depends on came from this factory and only this factory; without
        // this line CreateUserPrincipalAsync produced a principal with a name and role but no
        // permission claims at all, so every [RequiresPermission] check saw an empty permission set.
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationClaimsPrincipalFactory>();

        // OpenIddict looks these up by its own claim names rather than Identity's defaults.
        services.Configure<IdentityOptions>(options =>
        {
            options.ClaimsIdentity.UserNameClaimType = OpenIddictConstants.Claims.Name;
            options.ClaimsIdentity.UserIdClaimType = OpenIddictConstants.Claims.Subject;
            options.ClaimsIdentity.RoleClaimType = OpenIddictConstants.Claims.Role;
        });

        services
            .AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<ApplicationDbContext>()
                .ReplaceDefaultEntities<long>())
            .AddServer(options =>
            {
                options
                    .SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetLogoutEndpointUris("connect/logout")
                    .SetUserinfoEndpointUris("connect/userinfo")
                    .SetIntrospectionEndpointUris("connect/introspect");

                options
                    .AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow();

                // PKCE is required, not merely allowed. A public client that could exchange a code
                // without a verifier is a public client whose codes are worth stealing.
                options.RequireProofKeyForCodeExchange();

                // S256 only. The `plain` method sends the verifier as the challenge, so anyone who
                // can see the authorization request already has what the token request needs —
                // which makes PKCE ceremony rather than protection. OpenIddict advertises both by
                // default, so the weak one is removed explicitly.
                options.Configure(server =>
                {
                    server.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain);
                });

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Roles,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    AuthConstants.ApiScope,
                    AuthConstants.TerminalScope);

                options.SetAccessTokenLifetime(AuthConstants.AccessTokenLifetime);
                options.SetRefreshTokenLifetime(AuthConstants.RefreshTokenLifetime);

                // Rotation with reuse detection: replaying a spent refresh token revokes the whole
                // family, so a stolen token is worth one use and then burns the session it came from.
                options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);

                // Reference tokens so a session can actually be revoked. A self-contained JWT stays
                // valid until it expires no matter what the server decides afterwards.
                options.UseReferenceAccessTokens();
                options.UseReferenceRefreshTokens();

                ConfigureCertificates(options, configuration);

                var aspNetCore = options
                    .UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableLogoutEndpointPassthrough()
                    .EnableUserinfoEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();

                // OpenIddict refuses plain HTTP by default, which is right: an authorization code or
                // a token crossing a shop network in the clear is the whole attack. Development runs
                // on http://localhost, so the requirement is lifted there and only there —
                // deliberately keyed to an explicit setting rather than to the absence of one, so a
                // production deployment cannot end up here by forgetting to configure something.
                if (configuration.GetValue<bool>("OpenIddict:AllowInsecureHttp"))
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                // Same process, so validation talks to the server directly rather than over HTTP.
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        // Every business controller carries a plain [Authorize], not [Authorize(AuthenticationSchemes
        // = ...)]. AddIdentity's own AddAuthentication call sets the default authenticate/challenge
        // scheme to the Identity cookie, so without this, an unadorned [Authorize] checks for that
        // cookie — which a server-to-server Bearer call from the BFF never carries — and every API
        // request gets redirected (302) to the login page instead of authenticated. This overrides
        // only the default *authorization policy*'s scheme, not AddAuthentication's default, so the
        // interactive sign-in page and /connect/authorize's own cookie challenge are unaffected.
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();

            // Hub connections never carry a Bearer token — HubTicketMiddleware authenticates them by
            // redeeming a single-use ticket and building the principal itself, entirely outside the
            // scheme system, before the auth middleware runs. A policy naming a specific scheme makes
            // AuthorizationMiddleware re-authenticate via that scheme and overwrite context.User with
            // its (empty) result, discarding the ticket-built principal. This policy has no scheme
            // constraint, so it accepts whatever HubTicketMiddleware already put on the context.
            options.AddPolicy(HubAuthorizationPolicy, policy => policy.RequireAuthenticatedUser());
        });

        services.AddScoped<IPinHasher, Argon2PinHasher>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();

        // The default IMemoryCache is also consumed by OpenIddict's internal scope/application/token
        // caches, which call GetOrCreate without ever setting an entry Size — so it must stay
        // unbounded. PermissionResolver gets its own sized, named instance instead of a SizeLimit on
        // the shared one, which previously crashed every OpenIddict cache lookup on first use.
        services.AddMemoryCache();
        services.AddKeyedSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(
            "permissions",
            (_, _) => new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions { SizeLimit = 10_000 }));

        return services;
    }

    /// <summary>
    /// Development uses ephemeral keys so nothing has to be provisioned to run the thing. Production
    /// must not: ephemeral keys are regenerated on restart, which silently invalidates every issued
    /// token and every session across the shop.
    /// </summary>
    private static void ConfigureCertificates(OpenIddictServerBuilder options, IConfiguration configuration)
    {
        var signing = configuration["OpenIddict:SigningCertificatePath"];
        var encryption = configuration["OpenIddict:EncryptionCertificatePath"];
        var password = configuration["OpenIddict:CertificatePassword"];

        if (!string.IsNullOrWhiteSpace(signing) && File.Exists(signing))
        {
            using var stream = File.OpenRead(signing);
            options.AddSigningCertificate(stream, password);
        }
        else
        {
            options.AddDevelopmentSigningCertificate();
        }

        if (!string.IsNullOrWhiteSpace(encryption) && File.Exists(encryption))
        {
            using var stream = File.OpenRead(encryption);
            options.AddEncryptionCertificate(stream, password);
        }
        else
        {
            options.AddDevelopmentEncryptionCertificate();
        }
    }
}

/// <summary>
/// Puts the permission, staff and location claims on the principal at sign-in.
/// <para>
/// Resolving them once and carrying them on the token is what keeps the authorisation behaviour off
/// the database: it runs on every command, and a round trip there would sit inside the till's quote
/// budget.
/// </para>
/// </summary>
public sealed class ApplicationClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    private readonly ApplicationDbContext _db;
    private readonly IPermissionResolver _permissions;

    public ApplicationClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        Microsoft.Extensions.Options.IOptions<IdentityOptions> options,
        ApplicationDbContext db,
        IPermissionResolver permissions)
        : base(userManager, roleManager, options)
    {
        _db = db;
        _permissions = permissions;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, user.DisplayName));

        foreach (var permission in await _permissions.ResolveForUserAsync(user.Id))
        {
            identity.AddClaim(new Claim(AuthConstants.PermissionClaim, permission));
        }

        var staff = await _db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == user.Id);
        if (staff is not null)
        {
            identity.AddClaim(new Claim(AuthConstants.StaffIdClaim, staff.Id.ToString(CultureInfo.InvariantCulture)));
            identity.AddClaim(new Claim(
                AuthConstants.AccessLevelClaim,
                staff.AccessLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (user.DefaultLocationId is { } locationId)
        {
            identity.AddClaim(new Claim(AuthConstants.LocationIdClaim, locationId.ToString(CultureInfo.InvariantCulture)));
        }

        return identity;
    }
}
