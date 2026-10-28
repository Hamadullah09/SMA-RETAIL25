using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Cutover.Source;
using RetailPlus.Data.Services;

namespace RetailPlus.Cutover;

public sealed partial class CutoverRunner
{
    /// <summary>A carried-over user: the id and the name the new model records next to it.</summary>
    private sealed record CarriedUser(Guid Id, string Name);

    /// <summary>Users by Retail25 AspNetUsers id.</summary>
    private readonly Dictionary<long, CarriedUser> _users = [];

    /// <summary>Users by Retail25 staff_profiles id: what sales, invoices, rules and the time clock call "staff_id".</summary>
    private readonly Dictionary<long, CarriedUser> _staff = [];

    private CarriedUser? StaffOf(long? staffId) => staffId is long id ? _staff.GetValueOrDefault(id) : null;

    /// <summary>The users whose Retail25 PIN was carried over, for the checks.</summary>
    private readonly HashSet<Guid> _pinUsers = [];

    /// <summary>
    /// Roles and users. Retail25's five standard roles become the standard roles of the same job; another Retail25 role
    /// becomes a custom role with the permissions its keys translate to (<see cref="PermissionMap"/>). Users keep their
    /// Retail25 password hash and their staff code with the PIN's Argon2id hash; both sign them in as before and are
    /// stored again in the program's own format at the first sign-in that proves them (<see cref="SetupService"/>).
    /// </summary>
    private async Task UsersAsync(CancellationToken ct)
    {
        Log("Roles and users");
        var roleArea = _report.Area("Roles", "AspNetRoles, role_permissions");
        Dictionary<string, Role> existingRoles;
        Dictionary<string, Guid> usersByName;
        await using (var db = _w.NewContext())
        {
            existingRoles = await db.Roles.AsNoTracking().Include(r => r.Permissions).ToDictionaryAsync(r => r.Name.ToUpperInvariant(), ct);
            usersByName = await db.UserAccounts.ToDictionaryAsync(u => u.UserName.ToUpperInvariant(), u => u.Id, ct);
        }

        // The standard roles, as a new database gets them, unless they are there already.
        var roles = new List<Role>();
        var grants = new List<RoleGrant>();
        var roleIds = new Dictionary<string, (Guid Id, int Level)>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in Permissions.DefaultRoles)
        {
            if (existingRoles.TryGetValue(preset.Name.ToUpperInvariant(), out var have)) { roleIds[preset.Name] = (have.Id, have.Level); continue; }
            var id = StableIds.For("standard-role", preset.Name);
            roles.Add(new Role { Id = id, Name = preset.Name, Level = preset.Level, Description = preset.Description, IsSystem = true });
            grants.AddRange(preset.Permissions.Select(p => new RoleGrant { Id = StableIds.For("role-grant", id.ToString("N") + "/" + p), RoleId = id, Permission = p }));
            roleIds[preset.Name] = (id, preset.Level);
        }

        var keysOf = _src["role_permissions"].Rows.GroupBy(g => g.Long("role_id"))
            .ToDictionary(g => g.Key, g => g.Select(r => r.Text("permission_key")).OfType<string>().ToList());
        var sourceRoles = new Dictionary<long, (Guid Id, int Level, string Name)>();
        var customIds = new HashSet<Guid>();
        foreach (var row in _src["AspNetRoles"].Rows.OrderBy(r => r.Id))
        {
            roleArea.Read++;
            var name = row.Text("name") ?? "Role " + Number(row.Id);
            var (granted, unknown) = PermissionMap.Translate(keysOf.GetValueOrDefault(row.Id) ?? []);
            if (unknown.Count > 0) Warn($"AspNetRoles {row.Id} ({name}): Retail25 permissions with no counterpart here: {string.Join(", ", unknown)}");
            if (PermissionMap.StandardRoles.TryGetValue(name, out var standard))
            {
                var (id, level) = roleIds[standard];
                sourceRoles[row.Id] = (id, level, standard);
                var preset = Permissions.DefaultRoles.Single(r => r.Name == standard).Permissions;
                var more = granted.Except(preset).ToList();
                Note($"Retail25 role {name} is the standard role {standard}" + (more.Count == 0 ? "." : $"; in Retail25 it could also {string.Join(", ", more)}, which {standard} here does not include (Setup › Roles can add it)."));
                roleArea.Skip("a Retail25 standard role (mapped onto the standard role of the same job)");
                continue;
            }
            int roleLevel = Math.Clamp(row.Int("legacy_level"), 0, 4);
            if (roleIds.TryGetValue(Cut(name, 50), out var namesake))
            {
                sourceRoles[row.Id] = (namesake.Id, namesake.Level, Cut(name, 50));
                Note($"Retail25 role {name} has the name of a standard role here and is that role.");
                roleArea.Skip("same name as a standard role here (mapped onto it)");
                continue;
            }
            var roleId = existingRoles.TryGetValue(Cut(name, 50).ToUpperInvariant(), out var same) ? same.Id : StableIds.For("role", row.Id);
            customIds.Add(roleId);
            // In Retail25 anyone may approve, at another user's till, what their own role may do; here that takes pos.approve.
            if (granted.Count > 0) granted.Add(Permissions.PosApprove);
            roles.Add(From(new Role { Id = roleId, Name = name, Description = row.Text("description"), Level = roleLevel, IsSystem = false }, row));
            grants.AddRange(granted.Select(p => new RoleGrant { Id = StableIds.For("role-grant", roleId.ToString("N") + "/" + p), RoleId = roleId, Permission = p }));
            sourceRoles[row.Id] = (roleId, roleLevel, name);
            Note($"Retail25 role {name} has no standard counterpart; it is a custom role here with {granted.Count} permissions ({string.Join(", ", granted)}).");
        }
        await _w.UpsertAsync(roleArea, roles, ct);
        var grantArea = _report.Area("Role permissions", "role_permissions");
        grantArea.Read = keysOf.Values.Sum(k => k.Count);
        await _w.UpsertAsync(grantArea, grants, ct);
        await _w.RemoveMissingAsync<RoleGrant>(grantArea, db => db.RoleGrants.Where(g => customIds.Contains(g.RoleId)), grants.Select(g => g.Id).ToHashSet(), ct);

        // Users.
        var userArea = _report.Area("Users", "AspNetUsers, AspNetUserRoles, staff_profiles");
        var rolesOfUser = _src["AspNetUserRoles"].Rows.GroupBy(r => r.Long("user_id")).ToDictionary(g => g.Key, g => g.Select(r => r.Long("role_id")).ToList());
        var profiles = _src["staff_profiles"].Rows.GroupBy(p => p.Long("user_id")).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Id).First());
        var users = new List<UserAccount>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int damagedPins = 0;
        foreach (var row in _src["AspNetUsers"].Rows.OrderBy(r => r.Id))
        {
            userArea.Read++;
            var profile = profiles.GetValueOrDefault(row.Id);
            Guid? storeId = null;
            if (row.LongOrNull("default_location_id") is long home)
            {
                if (StoreOf(home) is { } store) storeId = store.StoreId;
                else if (_report.StoreCode is not null) { userArea.Skip("works at a store not carried over (--store-code)"); continue; }
                else Warn($"AspNetUsers {row.Id}: home location {home} is not in Retail25; the user is carried over for every store");
            }
            var userName = row.Text("user_name") ?? row.Text("email") ?? "user" + Number(row.Id);
            if (userName.Length > 50)
            {
                Warn($"AspNetUsers {row.Id}: user name \"{userName}\" is longer than 50 characters; the user signs in as \"{userName[..50]}\"");
                userName = userName[..50];
            }
            if (!names.Add(userName)) { userArea.Skip("same user name as another user"); continue; }

            var (roleId, level, roleName) = (rolesOfUser.GetValueOrDefault(row.Id) ?? [])
                .Where(sourceRoles.ContainsKey).Select(r => sourceRoles[r]).OrderByDescending(r => r.Level).FirstOrDefault();
            if (roleId == Guid.Empty)
            {
                var fallback = Permissions.DefaultRoleForLevel(profile?.Int("access_level") ?? 0);
                (roleId, level) = roleIds[fallback];
                roleName = fallback;
                Note($"Retail25 user {userName} has no role; they get the role {fallback} (their access level).");
            }
            else if ((rolesOfUser.GetValueOrDefault(row.Id)?.Count ?? 0) > 1)
                Note($"Retail25 user {userName} has several roles; they get the one with the most rights, {roleName}.");

            string? code = profile?.Text("staff_code")?.ToUpperInvariant();
            if (code is not null && (code.Length > 10 || !codes.Add(code)))
            {
                Warn($"AspNetUsers {row.Id}: staff code {code} is too long or used twice; the user signs in with the password only");
                code = null;
            }
            var id = usersByName.GetValueOrDefault(userName.ToUpperInvariant(), StableIds.For("user", row.Id));
            // The PIN goes with the staff code it is typed with; one Retail25's hasher did not make cannot be checked.
            string? pin = profile?.Text("pin_hash");
            if (pin is not null && (code is null || !SetupService.IsRetail25PinHash(pin)))
            {
                Warn($"staff_profiles {profile!.Id} ({userName}): {(code is null ? "the PIN has no staff code to go with" : "the PIN hash is not one Retail25 makes")}; they set a new PIN");
                damagedPins++;
                pin = null;
            }
            if (pin is not null) _pinUsers.Add(id);
            var fullName = row.Text("display_name") ?? (profile is null ? null : $"{profile.Text("first_name")} {profile.Text("last_name")}".Trim());
            var hash = row.Text("password_hash");
            users.Add(From(new UserAccount
            {
                Id = id, UserName = userName, FullName = fullName, PasswordHash = hash, MustChangePassword = hash is null,
                AccessLevel = level, RoleId = roleId, StoreId = storeId, StaffCode = code, PinHash = pin,
                IsActive = row.Bool("is_enabled") && (profile?.Bool("is_active") ?? true),
                CreatedAtUtc = Utc(profile?.Instant("created_at")),
            }, row));
            var carried = new CarriedUser(id, Cut(userName, 50));
            _users[row.Id] = carried;
            if (profile is not null)
                foreach (var p in _src["staff_profiles"].Rows.Where(p => p.Long("user_id") == row.Id)) _staff[p.Id] = carried;
        }
        await _w.UpsertAsync(userArea, users, ct, merge: (stored, mapped) =>
        {
            // A password or PIN set in the new program (or a Retail25 hash already upgraded at a sign-in) stays.
            if (stored.PasswordHash is { } kept && !SetupService.IsIdentityHash(kept))
            {
                mapped.PasswordHash = kept;
                mapped.MustChangePassword = stored.MustChangePassword;
            }
            if (stored.PinHash is { } keptPin && !SetupService.IsRetail25PinHash(keptPin))
            {
                mapped.PinHash = keptPin;
                mapped.StaffCode = stored.StaffCode;
            }
            mapped.FailedSignIns = stored.FailedSignIns;
            mapped.LockedUntilUtc = stored.LockedUntilUtc;
        });
        foreach (var orphan in _src["staff_profiles"].Rows.Where(p => !_users.ContainsKey(p.Long("user_id"))))
            userArea.Skip("staff profile without a user");
        if (damagedPins > 0)
            _report.NotCarriedOver.Add(new NotCarried("Staff PINs that cannot be checked (staff_profiles.pin_hash)", damagedPins,
                "Not an Argon2id hash as Retail25's PIN hasher writes it, or no usable staff code to type it with. Each person sets a new PIN (Setup › Users) or signs in with the password."));
        if (users.Count > 0)
            Note("No carried-over user is a super administrator: Retail25 had one location per user. The server makes the user named in Bootstrap:AdminUserName super administrator on its first start (CUTOVER.md 2.6).");
    }
}
