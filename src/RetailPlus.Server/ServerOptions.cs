namespace RetailPlus.Server;

/// <summary>The "Server" section of the configuration.</summary>
public sealed class ServerOptions
{
    /// <summary>Where the server keeps its own files (signing key, data-protection keys); empty = App_Data under the content root.</summary>
    public string? DataFolder { get; set; }
    /// <summary>A till that has sent nothing for this many hours is shown as behind.</summary>
    public int BehindAfterHours { get; set; } = 24;
    /// <summary>The largest package accepted, in megabytes (a package may carry a copy of a till's database).</summary>
    public int MaxPackageMegabytes { get; set; } = 512;
    /// <summary>
    /// The web back office's static export (the folder with index.html and _next); empty = web/out, looked for in the
    /// content root and the folders above it. When it is not there the server serves only the API.
    /// </summary>
    public string? WebRoot { get; set; }
}

/// <summary>The "Jwt" section: how sign-in tokens are made.</summary>
public sealed class JwtOptions
{
    /// <summary>The signing key (at least 32 characters); empty = one made on first start and kept in the data folder.</summary>
    public string? Key { get; set; }
    public string Issuer { get; set; } = "SMA RETAIL";
    public string Audience { get; set; } = "SMA RETAIL";
    public int Hours { get; set; } = 12;
}

/// <summary>
/// The "Bootstrap" section: the first super administrator, made when the database has no users. On a database with users
/// but nobody who can manage every store (after the Retail25 cutover), the user with this name is made super administrator
/// (or added, when there is none).
/// </summary>
public sealed class BootstrapOptions
{
    public string? AdminUserName { get; set; }
    /// <summary>Empty = a password is made up and written once to the log.</summary>
    public string? AdminPassword { get; set; }
}
