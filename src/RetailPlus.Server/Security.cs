using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

/// <summary>Claim types in the sign-in token.</summary>
public static class ServerClaims
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Role = "role";
    public const string Store = "store";
    public const string Permission = "perm";

    /// <summary>The authorization policy that requires a permission key.</summary>
    public static string Policy(string permission) => "perm:" + permission;
}

/// <summary>Who is signed in, and which stores they may see: one store, or every store with <see cref="Permissions.StoresAll"/>.</summary>
public sealed class UserScope(ClaimsPrincipal user)
{
    public Guid UserId { get; } = Guid.TryParse(user.FindFirstValue(ServerClaims.UserId), out var id) ? id : Guid.Empty;
    public string UserName { get; } = user.FindFirstValue(ServerClaims.Name) ?? "";
    public Guid? StoreId { get; } = Guid.TryParse(user.FindFirstValue(ServerClaims.Store), out var store) ? store : null;
    public bool AllStores { get; } = user.HasClaim(ServerClaims.Permission, Permissions.StoresAll);

    public bool Can(string permission) => user.HasClaim(ServerClaims.Permission, permission);

    /// <summary>A user with a store sees only that store; a user with "every store" sees all.</summary>
    public bool CanSee(Guid storeId) => AllStores || StoreId == storeId;

    public IQueryable<Store> Visible(IQueryable<Store> stores) => AllStores ? stores : stores.Where(s => s.Id == StoreId);
}

/// <summary>Makes and checks the sign-in tokens (JWT, HMAC-SHA256).</summary>
public sealed class TokenService(IOptions<JwtOptions> options, SigningKey key)
{
    public (string Token, DateTime ExpiresAtUtc) Create(UserAccount user, IEnumerable<string> permissions)
    {
        var o = options.Value;
        var expires = DateTime.UtcNow.AddHours(o.Hours);
        var claims = new List<Claim>
        {
            new(ServerClaims.UserId, user.Id.ToString()),
            new(ServerClaims.Name, user.UserName),
            new(ServerClaims.Role, user.Role?.Name ?? ""),
        };
        if (user.StoreId is { } store) claims.Add(new Claim(ServerClaims.Store, store.ToString()));
        claims.AddRange(permissions.Select(p => new Claim(ServerClaims.Permission, p)));
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer, Audience = o.Audience, Subject = new ClaimsIdentity(claims), NotBefore = DateTime.UtcNow, Expires = expires,
            SigningCredentials = new SigningCredentials(key.Key, SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }

    public TokenValidationParameters Validation => new()
    {
        ValidIssuer = options.Value.Issuer, ValidAudience = options.Value.Audience, IssuerSigningKey = key.Key,
        ValidateIssuerSigningKey = true, ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(2),
        NameClaimType = ServerClaims.Name, RoleClaimType = ServerClaims.Role,
    };

    /// <summary>
    /// Every request re-reads the user: a deactivated user is turned away at once, and the permissions are the role's as
    /// they are now, not as they were when the token was made.
    /// </summary>
    public static async Task RefreshAsync(TokenValidatedContext context)
    {
        var data = context.HttpContext.RequestServices.GetRequiredService<DataContext>();
        if (!Guid.TryParse(context.Principal?.FindFirstValue(ServerClaims.UserId), out var id))
        {
            context.Fail("No user in the token.");
            return;
        }
        await using var db = data.Create();
        var user = await db.UserAccounts.AsNoTracking().Include(u => u.Role).ThenInclude(r => r!.Permissions).SingleOrDefaultAsync(u => u.Id == id);
        IReadOnlySet<string> permissions = user is null ? new HashSet<string>() : SetupService.PermissionsOf(user);
        if (user is null || !permissions.Contains(Permissions.WebSignIn))
        {
            context.Fail("The user can no longer sign in.");
            return;
        }
        var identity = new ClaimsIdentity(JwtBearerDefaults.AuthenticationScheme, ServerClaims.Name, ServerClaims.Role);
        identity.AddClaim(new Claim(ServerClaims.UserId, user.Id.ToString()));
        identity.AddClaim(new Claim(ServerClaims.Name, user.UserName));
        identity.AddClaim(new Claim(ServerClaims.Role, user.Role?.Name ?? ""));
        if (user.StoreId is { } store) identity.AddClaim(new Claim(ServerClaims.Store, store.ToString()));
        foreach (var p in permissions) identity.AddClaim(new Claim(ServerClaims.Permission, p));
        context.Principal = new ClaimsPrincipal(identity);
    }
}

/// <summary>
/// The key that signs sign-in tokens: Jwt:Key from the configuration, or a random one made on first start and kept in
/// the data folder (so tokens stay valid when the server restarts).
/// </summary>
public sealed class SigningKey
{
    public SigningKey(IOptions<JwtOptions> jwt, ServerFolders folders)
    {
        var configured = jwt.Value.Key;
        byte[] bytes;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (configured.Length < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");
            bytes = Encoding.UTF8.GetBytes(configured);
        }
        else
        {
            var file = Path.Combine(folders.Data, "jwt.key");
            if (File.Exists(file)) bytes = Convert.FromBase64String(File.ReadAllText(file).Trim());
            else
            {
                bytes = RandomNumberGenerator.GetBytes(64);
                File.WriteAllText(file, Convert.ToBase64String(bytes));
            }
        }
        Key = new SymmetricSecurityKey(bytes);
    }

    public SymmetricSecurityKey Key { get; }
}

/// <summary>The server's own folder (Server:DataFolder, or App_Data under the content root).</summary>
public sealed class ServerFolders
{
    public ServerFolders(IOptions<ServerOptions> options, IWebHostEnvironment environment)
    {
        Data = string.IsNullOrWhiteSpace(options.Value.DataFolder) ? Path.Combine(environment.ContentRootPath, "App_Data") : options.Value.DataFolder!;
        Directory.CreateDirectory(Data);
    }

    public string Data { get; }

    /// <summary>Where packages wait while they are checked and applied.</summary>
    public string Incoming => Directory.CreateDirectory(Path.Combine(Data, "incoming")).FullName;
}

/// <summary>
/// The tills' signing keys, kept in StationLinks encrypted with ASP.NET Core data protection. Its key ring lives in the
/// server's data folder (keys/), encrypted with Windows DPAPI on Windows, so a copy of the database alone does not give
/// away any till's key; the keys never leave the server except once, to the till that joins.
/// </summary>
public sealed class StationKeys(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("RetailPlus.StationKeys.v1");

    public string Protect(string key) => _protector.Protect(key);

    /// <summary>The key, or null when it cannot be read (the key ring was lost or replaced).</summary>
    public string? Unprotect(string? protectedKey)
    {
        if (protectedKey is null) return null;
        try
        {
            return _protector.Unprotect(protectedKey);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>A one-time enrolment code, easy to read out and type: 8 letters and digits without look-alikes, as XXXX-XXXX.</summary>
    public static string NewEnrollmentCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return new string(chars, 0, 4) + "-" + new string(chars, 4, 4);
    }

    /// <summary>The stored form of an enrolment code: SHA-256 of the code without dashes or case.</summary>
    public static string HashCode(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code.Replace("-", "", StringComparison.Ordinal).Trim().ToUpperInvariant())));
}
