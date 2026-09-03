using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Retail25.Application.Abstractions;
using Retail25.Infrastructure.Identity.Shoppers;
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

    /// <summary>
    /// Every permission in one space-delimited claim.
    /// <para>
    /// The same set as sixty-one <see cref="PermissionClaim"/> entries, at roughly half the bytes,
    /// because the cost was never the names but sixty-one repetitions of the JSON around them. It
    /// matters because the token has to survive being sealed into a browser cookie, and a cookie
    /// over about 4KB is discarded silently — a sign-in that returns 200 and then behaves as though
    /// nobody signed in.
    /// </para>
    /// <para>
    /// Both forms are read. The till agent still authenticates through OpenIddict, which writes the
    /// individual claims, so dropping support for those would have taken the readers offline.
    /// </para>
    /// </summary>
    public const string PackedPermissionsClaim = "perms";
    public const string StaffIdClaim = "staff_id";
    public const string StationIdClaim = "station_id";
    public const string LocationIdClaim = "location_id";
    public const string AccessLevelClaim = "access_level";

    /// <summary>
    /// Present only on a phone app's hub connection, naming the single cart it may subscribe to.
    /// See <see cref="Application.Abstractions.HubTicket"/>.
    /// </summary>
    public const string CartIdClaim = "cart_id";

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
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                // Lockout is on by default: an unlimited password prompt on a machine sitting in a
                // shop is a machine anyone can work on all afternoon.
                options.Lockout.AllowedForNewUsers = true;

                // Length over composition, which is NIST SP 800-63B's position and the opposite of
                // what this used to do. Eight characters plus a digit is satisfied by "password1",
                // and the rules that would reject it — a capital, a symbol — are satisfied by
                // "Password1!", which is no better. Twelve characters with a banned-password check
                // (see WeakPasswordValidator) refuses both and stops pushing people towards the
                // predictable substitutions that composition rules reward.
                //
                // Configurable because a password policy is an operational decision, not a
                // deployment constant, and the audit listed this among the values that were neither.
                options.Password.RequiredLength = configuration.GetValue("Auth:Password:MinimumLength", 12);
                options.Password.RequireDigit = configuration.GetValue("Auth:Password:RequireDigit", false);
                options.Password.RequireLowercase = configuration.GetValue("Auth:Password:RequireLowercase", false);
                options.Password.RequireUppercase = configuration.GetValue("Auth:Password:RequireUppercase", false);
                options.Password.RequireNonAlphanumeric = configuration.GetValue("Auth:Password:RequireSymbol", false);
                options.Password.RequiredUniqueChars = configuration.GetValue("Auth:Password:RequiredUniqueChars", 4);

                options.Lockout.MaxFailedAccessAttempts =
                    configuration.GetValue("Auth:Lockout:MaxFailedAttempts", 5);
                options.Lockout.DefaultLockoutTimeSpan =
                    TimeSpan.FromMinutes(configuration.GetValue("Auth:Lockout:Minutes", 15));

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders()

            // Runs alongside the built-in rules rather than replacing them: Identity collects every
            // validator's verdict, so a password can fail on length and on being guessable at once
            // and the person setting it is told both.
            .AddPasswordValidator<WeakPasswordValidator>();

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
                // "EndSession" rather than "Logout": OpenIddict 6 renamed these to the words the
                // OpenID Connect spec uses. The route stays `connect/logout` — the clients and the
                // BFF already point at it, and the path is ours to choose regardless of the API name.
                options
                    .SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetEndSessionEndpointUris("connect/logout")
                    .SetUserInfoEndpointUris("connect/userinfo")
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

                ConfigureCertificates(options, configuration, environment);

                var aspNetCore = options
                    .UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
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

        AddShopperAuthentication(services, configuration, environment);
        AddStaffAuthentication(services, configuration);

        // Every business controller carries a plain [Authorize], not [Authorize(AuthenticationSchemes
        // = ...)]. AddIdentity's own AddAuthentication call sets the default authenticate/challenge
        // scheme to the Identity cookie, so without this, an unadorned [Authorize] checks for that
        // cookie — which a server-to-server Bearer call from the BFF never carries — and every API
        // request gets redirected (302) to the login page instead of authenticated. This overrides
        // only the default *authorization policy*'s scheme, not AddAuthentication's default, so the
        // interactive sign-in page and /connect/authorize's own cookie challenge are unaffected.
        services.AddAuthorization(options =>
        {
            // Two schemes, for two kinds of caller.
            //
            // StaffJwt is how a person signs in now: the front end's back end posts a username and
            // password to /auth/token and holds the returned token in its own encrypted cookie. The
            // interactive redirect flow that used to serve people is gone, and so is the
            // server-rendered login page that could never use the design system.
            //
            // OpenIddict remains for exactly one caller — the till agent's client_credentials grant,
            // a machine with no human behind it. Taking that away in the same change would have put
            // the tills' readers offline to save a dependency.
            options.DefaultPolicy = new AuthorizationPolicyBuilder(
                    StaffAuthentication.Scheme,
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
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
        services.AddScoped<IUserProvisioner, UserProvisioner>();
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
    /// The phone app's authentication, kept entirely separate from staff sign-in.
    /// <para>
    /// A second scheme rather than another OpenIddict grant, because the two audiences share nothing:
    /// different subject table, different lifetime, different revocation story, and — the point of the
    /// exercise — a claim set with no permissions in it. Two schemes means a shopper token and a staff
    /// token are validated by different keys against different issuers, so neither can ever be
    /// mistaken for the other however the endpoints are attributed.
    /// </para>
    /// </summary>
    private static void AddShopperAuthentication(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<ShopperTokenOptions>(configuration.GetSection(ShopperTokenOptions.Section));

        var options = new ShopperTokenOptions();
        configuration.GetSection(ShopperTokenOptions.Section).Bind(options);

        // Configured if configured, generated and kept on the host if not. See ShopperSigningKey for
        // why an unconfigured deployment is worth handling rather than refusing: this runs on shared
        // hosting where nobody can reach an environment editor, and the alternative is an API that
        // starts happily and 500s the first customer who tries to create an account.
        var signingKey = ShopperSigningKey.Resolve(configuration, environment);

        // The issuer reads its key from options, not from here, so the resolved value has to be put
        // back — otherwise validation would use the generated key while issuing still saw the empty
        // one, and every freshly minted token would be rejected by the request that carried it.
        services.PostConfigure<ShopperTokenOptions>(o => o.SigningKey = signingKey);

        // Still possible to have no key at all: a content root nothing can write to. The phone app is
        // simply not enabled there — the POS itself does not need it, and a store that never bought
        // trolleys should not be blocked from starting. Registering the scheme anyway, against a key
        // nothing can hold, keeps that an honest 401 rather than a 500 from an unregistered scheme.
        var key = Encoding.UTF8.GetByteCount(signingKey) >= 32
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
            : new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));

        services.AddAuthentication()
            .AddJwtBearer(ShopperAuthentication.Scheme, jwt =>
            {
                // Left on, the handler rewrites "sub" to the long ClaimTypes.NameIdentifier URI, and
                // CurrentShopper — which reads "sub", the claim the token actually carries — finds
                // nothing and reports every authenticated shopper as anonymous.
                jwt.MapInboundClaims = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,

                    // The default five minutes is a long time to keep honouring an expired token when
                    // the server and a handset both get their clock from the network.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddScoped<ICurrentShopper, CurrentShopper>();
        services.AddScoped<IShopperPasswordHasher, ShopperPasswordHasher>();
        services.AddScoped<IShopperTokenIssuer, ShopperTokenIssuer>();
    }

    /// <summary>
    /// The scheme a signed-in member of staff authenticates with.
    /// <para>
    /// Self-contained: the permissions travel in the token, so authorising a request costs a
    /// signature check rather than a database lookup. The cost of that is a permission revoked a
    /// minute ago surviving until the token expires, which is why the access token is short and why
    /// the refresh carries Identity's security stamp — see <see cref="StaffTokenIssuer"/>.
    /// </para>
    /// </summary>
    private static void AddStaffAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StaffTokenOptions>(configuration.GetSection(StaffTokenOptions.Section));
        services.AddSingleton<StaffTokenIssuer>();

        var signingKey = configuration[$"{StaffTokenOptions.Section}:SigningKey"] ?? string.Empty;

        // A random key when none is configured, rather than a shipped constant. Every token this
        // process issued stops validating when it restarts, which is a nuisance in development and
        // exactly right everywhere else: a default key in source is a key every deployment shares,
        // and anyone holding it could mint an administrator. StaffTokenIssuer refuses to start
        // without a real one, so this branch only ever runs where nothing has tried to sign in yet.
        var key = Encoding.UTF8.GetByteCount(signingKey) >= 32
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
            : new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));

        var issuer = configuration[$"{StaffTokenOptions.Section}:Issuer"] ?? "retail25";
        var audience = configuration[$"{StaffTokenOptions.Section}:Audience"] ?? "retail25.api";

        services.AddAuthentication()
            .AddJwtBearer(StaffAuthentication.Scheme, jwt =>
            {
                // Off, or the handler rewrites "sub" to the long ClaimTypes.NameIdentifier URI and
                // renames the rest, and CurrentUser — which reads staff_id, location_id and
                // permission by their short names — finds none of them.
                jwt.MapInboundClaims = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),

                    // "role", not ClaimTypes.Role. The handler writes Identity's long-URI role
                    // claim out as the short "role", and with MapInboundClaims off it stays short on
                    // the way back in — so a RoleClaimType of ClaimTypes.Role finds nothing and
                    // IsInRole answers false for everybody. That is not cosmetic: CurrentUser
                    // refuses a customer account by asking IsInRole, and a guard that always says
                    // "not a customer" is not a guard.
                    RoleClaimType = "role",
                    NameClaimType = "name",
                };

                jwt.Events = new JwtBearerEvents
                {
                    // Logged, because a token that fails here is otherwise invisible: the challenge
                    // is issued by whichever scheme is the default, so the log names that one and
                    // says nothing about why this handler refused. That cost an afternoon once.
                    OnAuthenticationFailed = context =>
                    {
                        context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger(StaffAuthentication.Scheme)
                            .LogInformation(context.Exception, "Staff token rejected.");

                        return Task.CompletedTask;
                    },

                    OnTokenValidated = context =>
                    {
                        // A refresh token is signed by the same key as an access token. Without this
                        // it would authenticate every request it was presented on, which would make
                        // the fourteen-day token the real session length.
                        var use = context.Principal?.FindFirst(StaffAuthentication.TokenUseClaim)?.Value;

                        if (use != StaffAuthentication.AccessTokenUse)
                        {
                            context.Fail("This token cannot be used to authorise a request.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });
    }

    /// <summary>
    /// Development uses ephemeral keys so nothing has to be provisioned to run the thing. Production
    /// must not: ephemeral keys are regenerated on restart, which silently invalidates every issued
    /// token and every session across the shop.
    /// </summary>
    private static void ConfigureCertificates(
        OpenIddictServerBuilder options,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var signing = configuration["OpenIddict:SigningCertificatePath"];
        var encryption = configuration["OpenIddict:EncryptionCertificatePath"];
        var password = configuration["OpenIddict:CertificatePassword"];

        var hasSigning = !string.IsNullOrWhiteSpace(signing) && File.Exists(signing);
        var hasEncryption = !string.IsNullOrWhiteSpace(encryption) && File.Exists(encryption);

        // Outside development the fallback is refused rather than taken. The development helpers
        // keep their key in the launching user's certificate store, and shared IIS hosting runs the
        // pool under an identity with no loaded user profile — so the key is regenerated on every
        // recycle, or cannot be written at all.
        //
        // The failure that produces is the worst kind: nothing logs an error, the site serves
        // normally, and users are signed out at intervals nobody can account for. Refusing to start
        // turns a mystery into a sentence.
        if (!environment.IsDevelopment() && (!hasSigning || !hasEncryption))
        {
            var missing = !hasSigning && !hasEncryption ? "signing and encryption certificates"
                : !hasSigning ? "a signing certificate"
                : "an encryption certificate";

            throw new InvalidOperationException(
                $"OpenIddict has no {missing} configured, and the development fallback is only "
                + $"permitted in the Development environment (this is '{environment.EnvironmentName}'). "
                + "Set OpenIddict:SigningCertificatePath and OpenIddict:EncryptionCertificatePath to "
                + "readable .pem or .pfx files, with OpenIddict:CertificatePassword if they are "
                + "protected. Prefer .pem on locked-down Windows hosting, where PKCS#12 import can "
                + "fail for reasons that have nothing to do with the file. "
                + "Ephemeral keys would sign every token this deployment issues and be discarded on "
                + "the next restart, signing out the whole shop without explanation.");
        }

        if (hasSigning)
        {
            options.AddSigningKey(LoadKey(signing!, password));
        }
        else
        {
            options.AddDevelopmentSigningCertificate();
        }

        if (hasEncryption)
        {
            options.AddEncryptionKey(LoadKey(encryption!, password));
        }
        else
        {
            options.AddDevelopmentEncryptionCertificate();
        }
    }

    /// <summary>
    /// Reads a signing or encryption key from disk. A <c>.pem</c> is loaded as a bare RSA key; a
    /// <c>.pfx</c> is loaded as a certificate whose private key never touches disk.
    /// <para>
    /// The <c>.pem</c> path exists because PKCS#12 import is not reliably available on locked-down
    /// Windows hosting. Importing a .pfx goes through the platform certificate stack, which wants
    /// somewhere to materialise the private key — the calling account's key container, under a user
    /// profile an IIS application pool need not have loaded, and a writable temp directory. Where
    /// any of that is missing the import fails with <c>CryptographicException: The system cannot
    /// find the file specified</c>, which names no file and is not about the .pfx, sitting there
    /// perfectly readable. <see cref="X509KeyStorageFlags.EphemeralKeySet"/> does not save it, and
    /// neither does enabling the profile; both were tried on the deployment this comment comes from.
    /// </para>
    /// <para>
    /// A PEM sidesteps the whole apparatus: <see cref="RSA.ImportFromEncryptedPem"/> parses bytes
    /// into an in-memory key with no store, no container and no temp file, so it works anywhere the
    /// process can read a file. Nothing here needs a certificate — OpenIddict publishes the public
    /// half in its JWKS document, and no client validates a chain against these.
    /// </para>
    /// </summary>
    private static SecurityKey LoadKey(string path, string? password)
    {
        if (path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
        {
            var rsa = RSA.Create();
            var pem = File.ReadAllText(path);

            // An unencrypted key is allowed but discouraged: the file is then the entire secret.
            if (string.IsNullOrEmpty(password))
            {
                rsa.ImportFromPem(pem);
            }
            else
            {
                rsa.ImportFromEncryptedPem(pem, password);
            }

            return new RsaSecurityKey(rsa);
        }

        return new X509SecurityKey(
            X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet));
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
