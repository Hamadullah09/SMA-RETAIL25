using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Passwords carried over from Retail25: ASP.NET Core Identity version 3 hashes are accepted as they are, and replaced
/// by this program's own format at the first sign-in.
/// </summary>
public static class IdentityHashes
{
    /// <summary>
    /// Made by ASP.NET Core Identity's own <c>PasswordHasher</c> (HMAC-SHA512, 100,000 iterations) for the password
    /// <see cref="Password"/>: a real hash of the kind Retail25 keeps in AspNetUsers.password_hash.
    /// </summary>
    public const string Cashier = "AQAAAAIAAYagAAAAEAagRNCF5ERwKbFL/4V3AjpKfU1S1BYMH5XJGGGgZpFXEs1h8NBIEsQrN4+G6osmUA==";

    public const string Password = "Cashier-2026";

    /// <summary>An Identity version 3 hash with another function and iteration count (the format of older Identity versions).</summary>
    public static string Make(string password, int prf, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var algorithm = prf switch { 1 => HashAlgorithmName.SHA256, _ => HashAlgorithmName.SHA512 };
        var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, algorithm, 32);
        var bytes = new byte[13 + salt.Length + subkey.Length];
        bytes[0] = 0x01;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(1), (uint)prf);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(5), (uint)iterations);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(9), (uint)salt.Length);
        salt.CopyTo(bytes, 13);
        subkey.CopyTo(bytes, 13 + salt.Length);
        return Convert.ToBase64String(bytes);
    }
}

public class IdentityHashTests
{
    [Fact]
    public void An_Identity_hash_from_Retail25_verifies_with_its_password_only()
    {
        Assert.True(SetupService.IsIdentityHash(IdentityHashes.Cashier));
        Assert.True(SetupService.VerifyPassword(IdentityHashes.Password, IdentityHashes.Cashier));
        Assert.False(SetupService.VerifyPassword("cashier-2026", IdentityHashes.Cashier));
        Assert.False(SetupService.VerifyPassword("", IdentityHashes.Cashier));
    }

    [Fact]
    public void Older_Identity_settings_verify_too()
    {
        var sha256 = IdentityHashes.Make("Old password", prf: 1, iterations: 10_000);
        Assert.StartsWith("AQAAAA", sha256, StringComparison.Ordinal);
        Assert.True(SetupService.VerifyPassword("Old password", sha256));
        Assert.False(SetupService.VerifyPassword("Old passw0rd", sha256));
    }

    [Fact]
    public void Our_own_hashes_and_garbage_are_not_Identity_hashes()
    {
        var ours = SetupService.HashPassword("secret");
        Assert.False(SetupService.IsIdentityHash(ours));
        Assert.True(SetupService.VerifyPassword("secret", ours));
        foreach (var junk in new[] { "", "AQAAAA", "not base64 at all!!", Convert.ToBase64String(new byte[40]), "AQAAAAEAACcQAAAAEA==" })
        {
            Assert.False(SetupService.IsIdentityHash(junk));
            Assert.False(SetupService.VerifyPassword("secret", junk));
        }
    }
}

public abstract class PasswordHashTests(DatabaseFixture db)
{
    private async Task<UserAccount> CarriedOverUserAsync(string hash)
    {
        await using var ctx = db.CreateContext();
        var role = await ctx.Roles.SingleAsync(r => r.Name == Permissions.CashierRole);
        var user = new UserAccount { UserName = "r25" + Guid.NewGuid().ToString("N")[..8], RoleId = role.Id, AccessLevel = 1, PasswordHash = hash };
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    private async Task<string?> StoredHashAsync(Guid userId)
    {
        await using var ctx = db.CreateContext();
        return await ctx.UserAccounts.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync();
    }

    [SkippableFact]
    public async Task A_carried_over_user_signs_in_with_the_old_password_and_is_rehashed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var user = await CarriedOverUserAsync(IdentityHashes.Cashier);
        var setup = new SetupService(db.Data);

        var wrong = await setup.SignInAsync(user.UserName, "Cashier-2025");
        Assert.Null(wrong.User);
        Assert.Equal(IdentityHashes.Cashier, await StoredHashAsync(user.Id));

        var signedIn = await setup.SignInAsync(user.UserName, IdentityHashes.Password);
        Assert.NotNull(signedIn.User);
        var stored = await StoredHashAsync(user.Id);
        Assert.StartsWith("pbkdf2$", stored, StringComparison.Ordinal);
        Assert.False(SetupService.IsIdentityHash(stored!));
        Assert.True(SetupService.VerifyPassword(IdentityHashes.Password, stored!));

        // The new hash works from then on, and is not replaced again.
        Assert.NotNull((await setup.SignInAsync(user.UserName, IdentityHashes.Password)).User);
        Assert.Equal(stored, await StoredHashAsync(user.Id));

        await using var ctx = db.CreateContext();
        var lines = await ctx.AuditEntries.AsNoTracking().Where(a => a.EntityId == user.Id).ToListAsync();
        Assert.Single(lines, a => a.Action == AuditActions.SignIn && a.Details!.Contains("new format", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, a => a.Action == AuditActions.Changed);
    }

    [SkippableFact]
    public async Task Approval_with_a_carried_over_password_rehashes_too()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var user = await CarriedOverUserAsync(IdentityHashes.Make("Approve-me", prf: 1, iterations: 10_000));
        var (approver, problem) = await new SetupService(db.Data).ApproveAsync(user.UserName, "Approve-me", pin: false, Permissions.PosSell, "Test");
        // A cashier may not approve, but the password was right, so it is stored in the new format all the same.
        Assert.Null(approver);
        Assert.NotNull(problem);
        Assert.StartsWith("pbkdf2$", await StoredHashAsync(user.Id), StringComparison.Ordinal);
    }
}

public sealed class PasswordHashTestsOnSqlite(SqliteFixture db) : PasswordHashTests(db), IClassFixture<SqliteFixture>;

public sealed class PasswordHashTestsOnSqlServer(SqlServerFixture db) : PasswordHashTests(db), IClassFixture<SqlServerFixture>;
