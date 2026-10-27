using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;

namespace RetailPlus.Data.Services;

/// <summary>Why a sign-in failed.</summary>
public enum SignInFailure
{
    None,
    WrongCredentials,
    NotActive,
    Locked,
    /// <summary>The user works at another store than this station's.</summary>
    OtherStore,
}

/// <summary>The outcome of a sign-in: the user, or why not (a locked account says until when, in UTC).</summary>
public sealed record SignInResult(UserAccount? User, SignInFailure Failure, DateTime? LockedUntilUtc)
{
    /// <summary>What to tell the person at the till.</summary>
    public string? Message => Failure switch
    {
        SignInFailure.None => null,
        SignInFailure.NotActive => "This user is not active. Ask a manager.",
        SignInFailure.OtherStore => "This user works at another store.",
        SignInFailure.Locked => $"Locked after too many wrong tries. Try again in {MinutesLeft(DateTime.UtcNow)} minute{(MinutesLeft(DateTime.UtcNow) == 1 ? "" : "s")}.",
        _ => "The name or password (or staff code or PIN) is not right.",
    };

    /// <summary>Whole minutes until the lock ends (at least 1).</summary>
    public int MinutesLeft(DateTime nowUtc) => LockedUntilUtc is { } until ? Math.Max(1, (int)Math.Ceiling((until - nowUtc).TotalMinutes)) : 0;
}

/// <summary>Setup screen data: store settings, locations, stations, users, tender types, lists and texts.</summary>
public sealed class SetupService(DataContext data)
{
    public static readonly (string Name, TenderKind Kind)[] DefaultTenders =
    [
        ("Cash", TenderKind.Cash), ("Visa", TenderKind.Card), ("MasterCard", TenderKind.Card), ("Amex", TenderKind.Card),
        ("Debit", TenderKind.Card), ("Cheque", TenderKind.Cheque), ("Gift Card", TenderKind.GiftCard), ("On Account", TenderKind.OnAccount),
    ];

    /// <summary>
    /// Creates the minimum a new database needs (a store with its settings row and a location, tender types and the
    /// station) and from then on works for that station: rows saved through <see cref="DataContext"/> get its store and
    /// station, and document numbers come from its range. The station's store is used when it already has one.
    /// </summary>
    public async Task<StationIdentity> SeedAsync(int stationNumber, CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        var station = await db.Stations.AsNoTracking().SingleOrDefaultAsync(s => s.Number == stationNumber, cancellationToken);
        var store = station is not null
            ? await db.Stores.SingleAsync(s => s.Id == station.StoreId, cancellationToken)
            : await db.Stores.OrderBy(s => s.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (store is null)
        {
            store = new Store { Code = "MAIN", Name = "Main store", TimeZoneId = TimeZoneInfo.Local.Id };
            db.Stores.Add(store);
        }
        if (!await db.StoreSettings.AnyAsync(s => s.StoreId == store.Id, cancellationToken))
            db.StoreSettings.Add(new StoreSettings { StoreId = store.Id, CompanyName = "My Store", Tax1Name = "Tax 1", Tax2Name = "Tax 2", MinimumTender = 0.01m, GracePeriodDays = 30 });
        if (!await db.Locations.AnyAsync(l => l.StoreId == store.Id, cancellationToken))
            db.Locations.Add(new Location { StoreId = store.Id, Code = "POS", Name = "Main store" });
        if (!await db.TenderTypes.AnyAsync(cancellationToken))
        {
            int order = 0;
            foreach (var (name, kind) in DefaultTenders)
                db.TenderTypes.Add(new TenderType { Name = name, Kind = kind, SortOrder = ++order, OpensDrawer = kind is TenderKind.Cash or TenderKind.Cheque });
        }
        if (station is null)
            db.Stations.Add(new Station { StoreId = store.Id, Number = stationNumber, Name = $"Station {stationNumber:000}" });
        await SeedRolesAsync(db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        data.Station = new StationIdentity(store.Id, stationNumber);
        return data.Station;
    }

    /// <summary>Rows of this program's store (all rows when no station is set).</summary>
    private IQueryable<T> OfThisStore<T>(IQueryable<T> rows) where T : class, IStoreScoped =>
        data.Station is { } here ? rows.Where(r => r.StoreId == here.StoreId) : rows;

    public async Task<bool> HasDataAsync(CancellationToken cancellationToken = default)
    {
        await using var db = data.Create();
        return await db.Items.AnyAsync(cancellationToken) || await db.Clients.AnyAsync(cancellationToken);
    }

    // Store settings ------------------------------------------------------------------------------------------

    /// <summary>A shop-wide value stored by name (such as the RFID hub address every station uses).</summary>
    public async Task<string?> GetExtraAsync(string name)
    {
        await using var db = data.Create();
        return await OfThisStore(db.StoreSettingExtras).Where(x => x.Name == name).Select(x => x.Value).FirstOrDefaultAsync();
    }

    public async Task SetExtraAsync(string name, string? value)
    {
        await using var db = data.Create();
        var row = await OfThisStore(db.StoreSettingExtras).FirstOrDefaultAsync(x => x.Name == name);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (row is not null) db.StoreSettingExtras.Remove(row);
        }
        else if (row is null) db.StoreSettingExtras.Add(new StoreSettingExtra { Name = name, Value = value.Trim() });
        else row.Value = value.Trim();
        await db.SaveChangesAsync();
    }

    public async Task<StoreSettings> GetSettingsAsync()
    {
        await using var db = data.Create();
        return await db.CurrentStoreSettings.AsNoTracking().SingleOrDefaultAsync() ?? new StoreSettings();
    }

    /// <summary>Saves the settings of this program's store (the row is matched by store, whatever id the copy has).</summary>
    public async Task SaveSettingsAsync(StoreSettings settings)
    {
        await using var db = data.Create();
        var existing = await db.CurrentStoreSettings.AsNoTracking().Select(s => new { s.Id, s.StoreId, s.CreatedAtUtc }).SingleOrDefaultAsync();
        if (existing is null)
        {
            settings.Id = Guid.Empty;
            db.StoreSettings.Add(settings);
        }
        else
        {
            settings.Id = existing.Id;
            settings.StoreId = existing.StoreId;
            settings.CreatedAtUtc = existing.CreatedAtUtc;
            db.StoreSettings.Update(settings);
        }
        await db.SaveChangesAsync();
    }

    // Locations and stations ------------------------------------------------------------------------------------

    public async Task<List<Location>> GetLocationsAsync()
    {
        await using var db = data.Create();
        return await OfThisStore(db.Locations.AsNoTracking()).OrderBy(l => l.Code).ToListAsync();
    }

    public async Task<Location> AddLocationAsync(string code, string? name)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length is < 1 or > 3) throw new BusinessRuleException("A location code has 1 to 3 characters.");
        await using var db = data.Create();
        if (await OfThisStore(db.Locations).AnyAsync(l => l.Code == code)) throw new BusinessRuleException($"Location {code} already exists.");
        var location = new Location { Code = code, Name = name };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location;
    }

    public async Task<List<Station>> GetStationsAsync()
    {
        await using var db = data.Create();
        return await db.Stations.AsNoTracking().OrderBy(s => s.Number).ToListAsync();
    }

    public async Task<Dictionary<string, string>> GetStationSettingsAsync(int stationNumber)
    {
        await using var db = data.Create();
        return await db.StationSettings.AsNoTracking()
            .Where(s => db.Stations.Any(st => st.Id == s.StationId && st.Number == stationNumber))
            .ToDictionaryAsync(s => s.Section + "/" + s.Key, s => s.Value ?? "", StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetStationSettingAsync(int stationNumber, string section, string key, string? value)
    {
        await using var db = data.Create();
        var station = await db.Stations.SingleOrDefaultAsync(s => s.Number == stationNumber)
                      ?? db.Stations.Add(new Station { Number = stationNumber, Name = $"Station {stationNumber:000}" }).Entity;
        await db.SaveChangesAsync();
        var row = await db.StationSettings.SingleOrDefaultAsync(s => s.StationId == station.Id && s.Section == section && s.Key == key);
        if (row is null) db.StationSettings.Add(new StationSetting { StationId = station.Id, Section = section, Key = key, Value = value });
        else row.Value = value;
        await db.SaveChangesAsync();
    }

    // Roles ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds the default roles when the database has none, and gives users without a role the default role of their
    /// access level (users brought in by the importer).
    /// </summary>
    private static async Task SeedRolesAsync(RetailPlusDbContext db, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.ToListAsync(cancellationToken);
        if (roles.Count == 0)
            foreach (var preset in Permissions.DefaultRoles)
            {
                var role = new Role { Name = preset.Name, Level = preset.Level, Description = preset.Description, IsSystem = true };
                role.Permissions.AddRange(preset.Permissions.Select(p => new RoleGrant { Permission = p }));
                db.Roles.Add(role);
                roles.Add(role);
            }
        foreach (var user in await db.UserAccounts.Where(u => u.RoleId == null).ToListAsync(cancellationToken))
        {
            var role = roles.Single(r => r.Name == Permissions.DefaultRoleForLevel(user.AccessLevel));
            user.RoleId = role.Id;
            user.Role = role;
        }
    }

    public async Task<List<Role>> GetRolesAsync()
    {
        await using var db = data.Create();
        return await db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Level).ThenBy(r => r.Name).ToListAsync();
    }

    /// <summary>How many users have each role (roles nobody has are left out).</summary>
    public async Task<Dictionary<Guid, int>> GetRoleUsageAsync()
    {
        await using var db = data.Create();
        return await db.UserAccounts.Where(u => u.RoleId != null).GroupBy(u => u.RoleId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
    }

    /// <summary>
    /// Adds or changes a role with exactly the given permissions; its users get the role's access level. A standard role
    /// keeps its name, and no change may leave the users without anyone who can manage users and roles.
    /// </summary>
    public async Task<Role> SaveRoleAsync(Role role, IEnumerable<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(role.Name)) throw new BusinessRuleException("Enter a role name.");
        if (role.Level is < 0 or > 4) throw new BusinessRuleException("Access levels are 0 to 4.");
        var keys = permissions.Distinct().ToList();
        if (keys.FirstOrDefault(k => !Permissions.IsKnown(k)) is { } unknown) throw new BusinessRuleException($"There is no permission \"{unknown}\".");
        var name = role.Name.Trim();
        await using var db = data.Create();
        var existing = role.Id == Guid.Empty ? null : await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == role.Id);
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != role.Id)) throw new BusinessRuleException($"There is already a role called {name}.");
        if (existing is { IsSystem: true } && !string.Equals(existing.Name, name, StringComparison.Ordinal))
            throw new BusinessRuleException($"{existing.Name} is one of the standard roles; its name stays.");
        if (existing is null)
        {
            existing = new Role();
            db.Roles.Add(existing);
        }
        existing.Name = name;
        existing.Description = role.Description;
        existing.Level = role.Level;
        existing.Permissions.RemoveAll(p => !keys.Contains(p.Permission));
        existing.Permissions.AddRange(keys.Where(k => existing.Permissions.All(p => p.Permission != k)).Select(k => new RoleGrant { Permission = k }));
        foreach (var user in await db.UserAccounts.Where(u => u.RoleId == existing.Id && u.AccessLevel != role.Level).ToListAsync())
            user.AccessLevel = role.Level;
        await SaveKeepingAManagerAsync(db);
        return existing;
    }

    /// <summary>Deletes a role nobody has. The standard roles cannot be deleted.</summary>
    public async Task DeleteRoleAsync(Guid id)
    {
        await using var db = data.Create();
        var role = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Id == id);
        if (role.IsSystem) throw new BusinessRuleException($"{role.Name} is one of the standard roles; change it instead.");
        int users = await db.UserAccounts.CountAsync(u => u.RoleId == id);
        if (users > 0) throw new BusinessRuleException($"{users} user{(users == 1 ? " has" : "s have")} the role {role.Name}; give them another role first.");
        db.Roles.Remove(role);
        await db.SaveChangesAsync();
    }

    /// <summary>The permission keys of a user's role (none for a user without a role or not active).</summary>
    public static IReadOnlySet<string> PermissionsOf(UserAccount user) =>
        user.IsActive && user.Role is { } role ? role.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal) : new HashSet<string>();

    /// <summary>
    /// Saves, but refuses (and saves nothing) when the change would leave no active user who may manage users and roles
    /// while there was one before: nobody could put it right from the tills.
    /// </summary>
    private static async Task SaveKeepingAManagerAsync(RetailPlusDbContext db)
    {
        static IQueryable<UserAccount> Managers(RetailPlusDbContext db) =>
            db.UserAccounts.Where(u => u.IsActive && u.Role != null && u.Role.Permissions.Any(p => p.Permission == Permissions.SetupUsers));
        bool hadOne = await Managers(db).AnyAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        if (hadOne && !await Managers(db).AnyAsync())
            throw new BusinessRuleException("Nobody would be left who can manage users and roles. Keep at least one active user with that permission.");
        await tx.CommitAsync();
    }

    // Users ------------------------------------------------------------------------------------------------------

    /// <summary>Wrong passwords or PINs in a row before an account locks, and for how long.</summary>
    public const int SignInAttempts = 5;
    public static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(5);

    public async Task<List<UserAccount>> GetUsersAsync()
    {
        await using var db = data.Create();
        return await db.UserAccounts.AsNoTracking().Include(u => u.Role).OrderBy(u => u.UserName).ToListAsync();
    }

    public async Task<List<Store>> GetStoresAsync()
    {
        await using var db = data.Create();
        return await db.Stores.AsNoTracking().OrderBy(s => s.Code).ToListAsync();
    }

    /// <summary>
    /// Adds or changes a user. The access level follows the role; a user saved without a role gets the default role of
    /// the access level given. A new user needs a password (or must choose one at the first sign-in).
    /// </summary>
    public async Task<UserAccount> SaveUserAsync(UserAccount user, string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(user.UserName)) throw new BusinessRuleException("Enter a user name.");
        if (user.AccessLevel is < 0 or > 4) throw new BusinessRuleException("Access levels are 0 to 4.");
        var name = user.UserName.Trim();
        await using var db = data.Create();
        if (await db.UserAccounts.AnyAsync(u => u.UserName == name && u.Id != user.Id)) throw new BusinessRuleException($"There is already a user called {name}.");
        var role = user.RoleId is { } roleId
            ? await db.Roles.SingleOrDefaultAsync(r => r.Id == roleId) ?? throw new BusinessRuleException("That role no longer exists.")
            : await db.Roles.SingleOrDefaultAsync(r => r.Name == Permissions.DefaultRoleForLevel(user.AccessLevel));
        var existing = user.Id == Guid.Empty ? null : await db.UserAccounts.FindAsync(user.Id);
        if (existing is null)
        {
            if (string.IsNullOrEmpty(newPassword) && !user.MustChangePassword) throw new BusinessRuleException("Enter a password for the new user.");
            existing = new UserAccount();
            db.UserAccounts.Add(existing);
        }
        existing.UserName = name;
        existing.FullName = string.IsNullOrWhiteSpace(user.FullName) ? null : user.FullName.Trim();
        existing.RoleId = role?.Id;
        existing.AccessLevel = role?.Level ?? user.AccessLevel;
        existing.StoreId = user.StoreId;
        existing.IsActive = user.IsActive;
        existing.MustChangePassword = user.MustChangePassword;
        if (!string.IsNullOrEmpty(newPassword))
        {
            existing.PasswordHash = HashPassword(newPassword);
            existing.MustChangePassword = false;
            existing.FailedSignIns = 0;
            existing.LockedUntilUtc = null;
        }
        await SaveKeepingAManagerAsync(db);
        return existing;
    }

    /// <summary>Gives a user a temporary password and unlocks the account: they choose their own at the next sign-in.</summary>
    public async Task ResetPasswordAsync(Guid userId, string temporaryPassword)
    {
        if (string.IsNullOrEmpty(temporaryPassword)) throw new BusinessRuleException("Enter a temporary password.");
        await using var db = data.Create();
        var user = await db.UserAccounts.SingleAsync(u => u.Id == userId);
        user.PasswordHash = HashPassword(temporaryPassword);
        user.MustChangePassword = true;
        user.FailedSignIns = 0;
        user.LockedUntilUtc = null;
        await db.SaveChangesAsync();
    }

    /// <summary>Sets the staff code and PIN for the quick sign-in at the till; an empty PIN removes both.</summary>
    public async Task SetPinAsync(Guid userId, string? staffCode, string? pin)
    {
        await using var db = data.Create();
        var user = await db.UserAccounts.SingleAsync(u => u.Id == userId);
        if (string.IsNullOrEmpty(pin))
        {
            user.StaffCode = null;
            user.PinHash = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(staffCode)) throw new BusinessRuleException("Enter a staff code.");
            if (pin.Length < 4 || !pin.All(char.IsAsciiDigit)) throw new BusinessRuleException("A PIN is at least 4 digits.");
            var code = staffCode.Trim();
            if (code.Length > 10) throw new BusinessRuleException("A staff code has at most 10 characters.");
            if (await db.UserAccounts.AnyAsync(u => u.StaffCode == code && u.Id != userId)) throw new BusinessRuleException($"Staff code {code} is taken.");
            user.StaffCode = code;
            user.PinHash = HashPassword(pin);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes a user. Users with time clock entries or commission rules stay (their history needs them): make them not
    /// active instead.
    /// </summary>
    public async Task DeleteUserAsync(Guid id)
    {
        await using var db = data.Create();
        var user = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == id);
        if (user is null) return;
        if (await db.TimeClockEntries.AnyAsync(t => t.UserId == id) || await db.CommissionRules.AnyAsync(r => r.UserId == id))
            throw new BusinessRuleException($"{user.UserName} has time clock entries or commission rules. Make the user not active instead.");
        db.UserAccounts.Remove(user);
        await SaveKeepingAManagerAsync(db);
    }

    /// <summary>
    /// Returns the user, with the role and its permissions, when the password matches; null otherwise. Inactive and
    /// locked users are refused, and <see cref="SignInAttempts"/> wrong passwords in a row lock the account for
    /// <see cref="LockoutTime"/>.
    /// </summary>
    public async Task<UserAccount?> AuthenticateAsync(string userName, string password) => (await SignInAsync(userName, password)).User;

    /// <summary>The quick sign-in at the till with a staff code and PIN, by the same rules as <see cref="AuthenticateAsync"/>.</summary>
    public async Task<UserAccount?> AuthenticatePinAsync(string staffCode, string pin) => (await SignInWithPinAsync(staffCode, pin)).User;

    /// <summary>
    /// Signs in with a user name and password, saying why when it fails (a locked account says until when). The sign-in
    /// goes to the audit log as <paramref name="purpose"/> (null writes nothing for a success), every failure as a failed
    /// sign-in.
    /// </summary>
    public Task<SignInResult> SignInAsync(string userName, string password, string? purpose = AuditActions.SignIn)
    {
        var name = userName.Trim();
        return CheckAsync(u => u.UserName == name, name, false,
            user => user.PasswordHash is null ? user.MustChangePassword : VerifyPassword(password, user.PasswordHash), purpose);
    }

    /// <summary>Signs in with a staff code and PIN, by the same rules as <see cref="SignInAsync"/>.</summary>
    public Task<SignInResult> SignInWithPinAsync(string staffCode, string pin, string? purpose = AuditActions.SignIn)
    {
        var code = staffCode.Trim();
        return CheckAsync(u => u.StaffCode == code, code, true, user => user.PinHash is not null && VerifyPassword(pin, user.PinHash), purpose);
    }

    private async Task<SignInResult> CheckAsync(System.Linq.Expressions.Expression<Func<UserAccount, bool>> find, string typed, bool pin,
        Func<UserAccount, bool> check, string? purpose)
    {
        // Nothing typed: nothing to look up, and nothing worth a line in the audit log.
        if (typed.Length == 0) return new SignInResult(null, SignInFailure.WrongCredentials, null);
        await using var db = data.Create();
        var user = await db.UserAccounts.Include(u => u.Role).ThenInclude(r => r!.Permissions).SingleOrDefaultAsync(find);
        var now = DateTime.UtcNow;
        string why = purpose is null or AuditActions.SignIn ? "" : $" ({purpose})";
        void Failed(string details) => db.Audit(AuditActions.FailedSignIn, details + why, nameof(UserAccount), user?.Id, userId: user?.Id, userName: user?.UserName ?? typed);
        SignInResult result;
        if (user is null)
        {
            Failed(pin ? $"Unknown staff code {typed}" : $"Unknown user {typed}");
            result = new SignInResult(null, SignInFailure.WrongCredentials, null);
        }
        else if (!user.IsActive)
        {
            Failed($"{user.UserName} is not active");
            result = new SignInResult(null, SignInFailure.NotActive, null);
        }
        else if (user.LockedUntilUtc > now)
        {
            Failed($"{user.UserName} is locked");
            result = new SignInResult(null, SignInFailure.Locked, user.LockedUntilUtc);
        }
        else if (user.StoreId is { } home && data.Station is { } here && home != here.StoreId)
        {
            Failed($"{user.UserName} works at another store");
            result = new SignInResult(null, SignInFailure.OtherStore, null);
        }
        else if (check(user))
        {
            user.FailedSignIns = 0;
            user.LockedUntilUtc = null;
            if (purpose is not null) db.Audit(purpose, user.UserName + why, nameof(UserAccount), user.Id, userId: user.Id, userName: user.UserName);
            result = new SignInResult(user, SignInFailure.None, null);
        }
        else
        {
            bool locks = ++user.FailedSignIns >= SignInAttempts;
            if (locks)
            {
                user.FailedSignIns = 0;
                user.LockedUntilUtc = now + LockoutTime;
            }
            Failed($"{user.UserName}: wrong {(pin ? "PIN" : "password")}{(locks ? "; locked for " + LockoutTime.TotalMinutes + " minutes" : "")}");
            result = new SignInResult(null, locks ? SignInFailure.Locked : SignInFailure.WrongCredentials, user.LockedUntilUtc);
        }
        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// The "needs a supervisor" approval: another user signs in with a password (or, with <paramref name="pin"/>, a staff
    /// code and PIN) and is accepted when their role has both <see cref="Permissions.PosApprove"/> and
    /// <paramref name="permission"/>. Approvals and refusals go to the audit log with <paramref name="action"/>.
    /// Returns the approver, or null with the reason.
    /// </summary>
    public async Task<(UserAccount? Approver, string? Problem)> ApproveAsync(string nameOrCode, string secret, bool pin, string permission, string action)
    {
        var signIn = pin ? await SignInWithPinAsync(nameOrCode, secret, purpose: null) : await SignInAsync(nameOrCode, secret, purpose: null);
        if (signIn.User is not { } approver) return (null, signIn.Message);
        bool allowed = Permissions.CanApprove(PermissionsOf(approver), permission);
        await using var db = data.Create();
        var asked = data.CurrentUser?.UserName;
        db.Audit(allowed ? AuditActions.Approval : AuditActions.ApprovalRefused,
            $"{action}: {(allowed ? "approved" : "may not approve")} by {approver.UserName}{(asked is null ? "" : " for " + asked)}",
            nameof(UserAccount), approver.Id);
        await db.SaveChangesAsync();
        return allowed ? (approver, null) : (null, $"{approver.UserName} may not approve this.");
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        var iterations = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // Lists ------------------------------------------------------------------------------------------------------

    public async Task<List<TenderType>> GetTenderTypesAsync(bool activeOnly = true)
    {
        await using var db = data.Create();
        return await db.TenderTypes.AsNoTracking().Where(t => !activeOnly || t.IsActive).OrderBy(t => t.SortOrder).ToListAsync();
    }

    public async Task SaveTenderTypesAsync(IEnumerable<TenderType> tenders)
    {
        await using var db = data.Create();
        var currencies = await db.FxRates.Select(r => r.Currency).ToListAsync();
        foreach (var t in tenders)
        {
            if (string.IsNullOrWhiteSpace(t.Name)) continue;
            t.Currency = string.IsNullOrWhiteSpace(t.Currency) ? null : t.Currency.Trim().ToUpperInvariant();
            if (t.Currency is not null && !currencies.Contains(t.Currency, StringComparer.OrdinalIgnoreCase))
                throw new BusinessRuleException($"{t.Name}: there is no exchange rate for {t.Currency}. Add the rate first.");
            if (t.Id == Guid.Empty) db.TenderTypes.Add(t);
            else db.TenderTypes.Update(t);
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<string>> GetDepartmentsAsync()
    {
        await using var db = data.Create();
        return await db.Departments.OrderBy(d => d.Name).Select(d => d.Name).ToListAsync();
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        await using var db = data.Create();
        return await db.Categories.OrderBy(d => d.Name).Select(d => d.Name).ToListAsync();
    }

    public async Task AddDepartmentAsync(string name)
    {
        name = name.Trim().ToUpperInvariant();
        if (name.Length == 0) return;
        await using var db = data.Create();
        if (!await db.Departments.AnyAsync(d => d.Name == name)) db.Departments.Add(new Department { Name = name });
        await db.SaveChangesAsync();
    }

    public async Task AddCategoryAsync(string name)
    {
        name = name.Trim().ToUpperInvariant();
        if (name.Length == 0) return;
        await using var db = data.Create();
        if (!await db.Categories.AnyAsync(d => d.Name == name)) db.Categories.Add(new Category { Name = name });
        await db.SaveChangesAsync();
    }

    public async Task DeleteDepartmentAsync(string name)
    {
        await using var db = data.Create();
        var dept = await db.Departments.SingleOrDefaultAsync(d => d.Name == name);
        if (dept is null) return;
        if (await db.CommissionRules.Where(r => r.DepartmentId == dept.Id).Select(r => r.Name).FirstOrDefaultAsync() is { } rule)
            throw new BusinessRuleException($"The commission rule \"{rule}\" is for department {name}. Change or delete the rule first.");
        var now = DateTime.UtcNow;
        await db.Items.Where(i => i.DepartmentId == dept.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.DepartmentId, (Guid?)null).SetProperty(i => i.UpdatedAtUtc, now));
        db.Departments.Remove(dept);
        await db.SaveChangesAsync();
    }

    public async Task DeleteCategoryAsync(string name)
    {
        await using var db = data.Create();
        var cat = await db.Categories.SingleOrDefaultAsync(d => d.Name == name);
        if (cat is null) return;
        if (await db.CommissionRules.Where(r => r.CategoryId == cat.Id).Select(r => r.Name).FirstOrDefaultAsync() is { } rule)
            throw new BusinessRuleException($"The commission rule \"{rule}\" is for category {name}. Change or delete the rule first.");
        var now = DateTime.UtcNow;
        await db.Items.Where(i => i.CategoryId == cat.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.CategoryId, (Guid?)null).SetProperty(i => i.UpdatedAtUtc, now));
        db.Categories.Remove(cat);
        await db.SaveChangesAsync();
    }

    public async Task<List<TaxConfiguration>> GetTaxConfigurationsAsync()
    {
        await using var db = data.Create();
        return await db.TaxConfigurations.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
    }

    /// <summary>
    /// Saves the out-of-state tax configurations: rows read earlier are changed, new ones added and the ones left out
    /// deleted, so the audit log and the server see what really changed.
    /// </summary>
    public async Task SaveTaxConfigurationsAsync(IEnumerable<TaxConfiguration> configurations)
    {
        var list = configurations.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        if (list.Count > 12) throw new BusinessRuleException("You can enter up to 12 tax configurations.");
        await using var db = data.Create();
        var stored = await db.TaxConfigurations.ToListAsync();
        db.TaxConfigurations.RemoveRange(stored.Where(s => list.All(c => c.Id != s.Id)));
        foreach (var c in list)
        {
            var row = stored.FirstOrDefault(s => s.Id == c.Id && c.Id != Guid.Empty);
            if (row is null) db.TaxConfigurations.Add(row = new TaxConfiguration());
            row.Name = c.Name.Trim(); row.Tax1Name = c.Tax1Name; row.Tax1Rate = c.Tax1Rate; row.Tax2Name = c.Tax2Name; row.Tax2Rate = c.Tax2Rate;
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<FxRate>> GetFxRatesAsync()
    {
        await using var db = data.Create();
        return await db.FxRates.AsNoTracking().OrderBy(r => r.Currency).ToListAsync();
    }

    /// <summary>
    /// Saves the exchange rates (units of each currency one unit of home currency buys). Rows are changed in place and
    /// removed one by one, so the change reaches the other tills; a currency a tender still takes cannot be removed.
    /// </summary>
    public async Task SaveFxRatesAsync(IEnumerable<FxRate> rates)
    {
        var wanted = rates.Where(r => !string.IsNullOrWhiteSpace(r.Currency) && r.Rate > 0)
            .GroupBy(r => r.Currency.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.Last().Rate);
        await using var db = data.Create();
        var existing = await db.FxRates.ToListAsync();
        var used = await db.TenderTypes.Where(t => t.Currency != null).Select(t => t.Currency!).ToListAsync();
        foreach (var row in existing.Where(r => !wanted.ContainsKey(r.Currency)))
        {
            if (used.Contains(row.Currency, StringComparer.OrdinalIgnoreCase))
                throw new BusinessRuleException($"A tender takes {row.Currency}; change that tender before removing its rate.");
            db.FxRates.Remove(row);
        }
        foreach (var (currency, rate) in wanted)
        {
            var row = existing.FirstOrDefault(r => string.Equals(r.Currency, currency, StringComparison.OrdinalIgnoreCase));
            if (row is null) db.FxRates.Add(new FxRate { Currency = currency, Rate = rate });
            else row.Rate = rate;
        }
        await db.SaveChangesAsync();
    }

    public async Task<string> GetTextAsync(DocumentTextKind kind, Guid? locationId = null)
    {
        await using var db = data.Create();
        return await OfThisStore(db.DocumentTexts).Where(t => t.Kind == kind && t.LocationId == locationId).Select(t => t.Text).SingleOrDefaultAsync() ?? "";
    }

    public async Task SetTextAsync(DocumentTextKind kind, string text, Guid? locationId = null)
    {
        await using var db = data.Create();
        var row = await OfThisStore(db.DocumentTexts).SingleOrDefaultAsync(t => t.Kind == kind && t.LocationId == locationId);
        if (row is null) db.DocumentTexts.Add(new DocumentText { Kind = kind, LocationId = locationId, Text = text });
        else row.Text = text;
        await db.SaveChangesAsync();
    }

    public async Task<List<FastKey>> GetFastKeysAsync(int panel)
    {
        await using var db = data.Create();
        return await db.FastKeys.AsNoTracking().Where(k => k.Panel == panel).OrderBy(k => k.KeyNumber).ToListAsync();
    }

    public async Task SaveFastKeyAsync(FastKey key)
    {
        await using var db = data.Create();
        var existing = await db.FastKeys.SingleOrDefaultAsync(k => k.Panel == key.Panel && k.KeyNumber == key.KeyNumber);
        if (existing is null) db.FastKeys.Add(key);
        else
        {
            existing.Name = key.Name; existing.Plu = key.Plu; existing.FixedPrice = key.FixedPrice; existing.IsCredit = key.IsCredit;
            existing.Taxable1 = key.Taxable1; existing.Taxable2 = key.Taxable2; existing.IsCategory = key.IsCategory; existing.Color = key.Color;
        }
        await db.SaveChangesAsync();
    }
}
