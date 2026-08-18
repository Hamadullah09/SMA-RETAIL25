using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Retail25.Application.Abstractions;
using Retail25.Domain.Common;

namespace Retail25.Infrastructure.Identity;

/// <summary>
/// The Identity half of onboarding a colleague. Wraps <see cref="UserManager{TUser}"/> so the
/// application layer can create a sign-in without taking a dependency on Identity itself.
/// </summary>
public sealed class UserProvisioner : IUserProvisioner
{
    /// <summary>
    /// Returned when Identity reports a failure with no error entries at all, which would otherwise
    /// produce a <see cref="Result"/> carrying <see cref="Error.None"/> and throw.
    /// </summary>
    private static readonly Error Unknown = new("staff.create_failed", "The account could not be created.");

    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly Persistence.ApplicationDbContext _db;

    public UserProvisioner(
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        Persistence.ApplicationDbContext db)
    {
        _users = users;
        _roles = roles;
        _db = db;
    }

    public async Task<IReadOnlyList<RoleInfo>> RolesAsync(CancellationToken ct)
        => await _roles.Roles
            .AsNoTracking()
            .OrderBy(r => r.LegacyLevel ?? int.MaxValue)
            .ThenBy(r => r.Name)
            .Select(r => new RoleInfo(r.Name!, r.LegacyLevel, r.Description))
            .ToListAsync(ct);

    public async Task<bool> RoleExistsAsync(string roleName, CancellationToken ct)
        => !string.IsNullOrWhiteSpace(roleName) && await _roles.RoleExistsAsync(roleName);

    public async Task<bool> EmailTakenAsync(string email, CancellationToken ct)
        => await _users.FindByEmailAsync(email) is not null
            || await _users.FindByNameAsync(email) is not null;

    public async Task<Result> DeleteAsync(long userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Already gone is the outcome the caller wanted, so it is a success rather than an error.
        if (user is null)
        {
            return Result.Success();
        }

        var removed = await _users.DeleteAsync(user);

        return removed.Succeeded
            ? Result.Success()
            : Result.Failure(new Error("staff.delete_failed", string.Join(" ", removed.Errors.Select(e => e.Description))));
    }

    public async Task<long?> FindIdByEmailAsync(string email, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(email) ?? await _users.FindByNameAsync(email);

        return user?.Id;
    }

    public async Task<Result<long>> CreateAsync(
        string email,
        string displayName,
        string password,
        string role,
        long? locationId,
        CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,

            // Confirmed on creation because an administrator vouched for the address by typing it.
            // The alternative — waiting for a confirmation email — would leave the account unusable
            // on any deployment without working SMTP, which includes every on-premise shop so far.
            EmailConfirmed = true,
            DisplayName = displayName,
            DefaultLocationId = locationId,
        };

        var created = await _users.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            return Result.Failure<long>(FirstError(created));
        }

        var assigned = await _users.AddToRoleAsync(user, role);

        if (!assigned.Succeeded)
        {
            // The surrounding transaction rolls the user row back, so there is no orphan to clean
            // up here — but the caller still needs to know why.
            return Result.Failure<long>(FirstError(assigned));
        }

        return Result.Success(user.Id);
    }

    public async Task<Result> ResetPasswordAsync(long userId, string newPassword, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));

        if (user is null)
        {
            return Result.Failure(new Error("staff.not_found", "No such member of staff."));
        }

        // Generate-then-redeem rather than a direct hash write: this is the path that runs the
        // configured password validator and stamps a new security stamp, which is what invalidates
        // the sessions of whoever knew the old password.
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var reset = await _users.ResetPasswordAsync(user, token, newPassword);

        return reset.Succeeded ? Result.Success() : Result.Failure(FirstError(reset));
    }

    public async Task<Result> SetEnabledAsync(long userId, bool enabled, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));

        if (user is null)
        {
            return Result.Failure(new Error("staff.not_found", "No such member of staff."));
        }

        user.IsEnabled = enabled;

        // Rotating the stamp is what actually ends a disabled user's current session; flipping the
        // flag alone would leave them signed in until their token happened to expire.
        var updated = await _users.UpdateAsync(user);

        if (updated.Succeeded && !enabled)
        {
            await _users.UpdateSecurityStampAsync(user);
        }

        return updated.Succeeded ? Result.Success() : Result.Failure(FirstError(updated));
    }

    public async Task<bool> IsInRoleAsync(long userId, string role, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));

        return user is not null && await _users.IsInRoleAsync(user, role);
    }

    public async Task<int> CountEnabledInRoleAsync(string role, CancellationToken ct)
    {
        var inRole = await _users.GetUsersInRoleAsync(role);

        return inRole.Count(u => u.IsEnabled);
    }

    public async Task<IReadOnlyDictionary<long, UserAccountInfo>> AccountsAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<long, UserAccountInfo>();
        }

        var ids = userIds.Distinct().ToArray();

        var accounts = await _users.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.EmailConfirmed,
                u.IsEnabled,
                u.LockoutEnd,
            })
            .ToListAsync(ct);

        // The role names in one join rather than a GetRolesAsync per user. Identity's own helper
        // takes a user, so using it here would mean a round trip each — the thing this method exists
        // to avoid.
        var roles = await (
                from userRole in _db.UserRoles
                join role in _db.Roles on userRole.RoleId equals role.Id
                where ids.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .AsNoTracking()
            .ToListAsync(ct);

        var byUser = roles
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(r => r.Name!).Where(n => n is not null).OrderBy(n => n).ToList());

        var now = DateTimeOffset.UtcNow;

        return accounts.ToDictionary(
            a => a.Id,
            a => new UserAccountInfo(
                a.Id,
                a.Email,
                a.EmailConfirmed,
                byUser.TryGetValue(a.Id, out var named) ? named : [],
                // Disabled and locked out are different states with the same consequence, and the
                // screen needs both: one is a decision somebody made, the other is five bad
                // passwords and will clear itself.
                a.IsEnabled && (a.LockoutEnd is null || a.LockoutEnd <= now),
                a.LockoutEnd > now ? a.LockoutEnd : null));
    }

    /// <summary>
    /// Identity returns a list; the UI shows one message. The first is the most specific in
    /// practice, and its <c>Code</c> ("PasswordTooShort") is stable enough to translate against.
    /// </summary>
    private static Error FirstError(IdentityResult result)
    {
        var first = result.Errors.FirstOrDefault();

        return first is null
            ? Unknown
            : new Error($"identity.{ToSnakeCase(first.Code)}", first.Description);
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(value[i]));
        }

        return builder.ToString();
    }
}
