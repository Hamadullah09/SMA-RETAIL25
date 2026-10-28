using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RetailPlus.Core.Model;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Data.Tests;

/// <summary>
/// Passwords and PINs carried over from Retail25: ASP.NET Core Identity version 3 password hashes and Argon2id PIN hashes
/// are accepted as they are, and replaced by this program's own format at the first sign-in.
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

/// <summary>A staff PIN hash made by Retail25's own Argon2PinHasher (Argon2id, 4 iterations, 64 MiB, 2 lanes).</summary>
public static class Retail25Pins
{
    public const string Hash = "argon2id$4$65536$2$VuHfyaqut8xZTcPPVYCVNA==$xx6dCgcCmMrbNN5reZYyFztFAmRG++kr0ljicZANdPc=";

    public const string Pin = "0042";
}

public class Retail25PinHashTests
{
    [Fact]
    public void A_Retail25_PIN_hash_verifies_with_its_PIN_only()
    {
        Assert.True(SetupService.IsRetail25PinHash(Retail25Pins.Hash));
        Assert.True(SetupService.VerifyPin(Retail25Pins.Pin, Retail25Pins.Hash));
        foreach (var wrong in new[] { "0043", "42", "00420", "", " " })
            Assert.False(SetupService.VerifyPin(wrong, Retail25Pins.Hash));
    }

    [Fact]
    public void The_Argon2id_library_gives_the_RFC_9106_test_vector()
    {
        // RFC 9106, section 5.3: Argon2id, version 0x13, 32 KiB, 3 passes, 4 lanes, with a secret and associated data.
        using var argon2 = new Konscious.Security.Cryptography.Argon2id(Enumerable.Repeat((byte)0x01, 32).ToArray())
        {
            Salt = Enumerable.Repeat((byte)0x02, 16).ToArray(),
            KnownSecret = Enumerable.Repeat((byte)0x03, 8).ToArray(),
            AssociatedData = Enumerable.Repeat((byte)0x04, 12).ToArray(),
            Iterations = 3, MemorySize = 32, DegreeOfParallelism = 4,
        };
        Assert.Equal("0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659", Convert.ToHexStringLower(argon2.GetBytes(32)));
    }

    [Fact]
    public void Damaged_and_other_hashes_are_not_Retail25_PIN_hashes()
    {
        string salt = "VuHfyaqut8xZTcPPVYCVNA==", hash = "xx6dCgcCmMrbNN5reZYyFztFAmRG++kr0ljicZANdPc=";
        var junk = new[]
        {
            "", "argon2id", $"argon2i$4$65536$2${salt}${hash}", $"argon2id$4$65536${salt}${hash}", $"argon2id$0$65536$2${salt}${hash}",
            $"argon2id$999$65536$2${salt}${hash}", $"argon2id$4$4194304$2${salt}${hash}", $"argon2id$4$65536$0${salt}${hash}",
            $"argon2id$4$65536$99${salt}${hash}", $"argon2id$4$8$2${salt}${hash}", $"argon2id$-4$65536$2${salt}${hash}",
            $"argon2id$4$65536$2$not base64${hash}", $"argon2id$4$65536$2${salt}$c2hvcnQ=", "argon2id$4$65536$2$c2FsdA==$" + hash,
            IdentityHashes.Cashier,
        };
        foreach (var stored in junk)
        {
            Assert.False(SetupService.IsRetail25PinHash(stored), stored);
            Assert.False(SetupService.VerifyPin(Retail25Pins.Pin, stored), stored);
        }
        var ours = SetupService.HashPassword("1234");
        Assert.False(SetupService.IsRetail25PinHash(ours));
        Assert.True(SetupService.VerifyPin("1234", ours));
    }

    [Fact]
    public async Task PINs_checked_at_the_same_time_all_come_out_right()
    {
        var checks = Enumerable.Range(0, 6).Select(i => Task.Run(() => (Right: i % 2 == 0, Matched: SetupService.VerifyPin(i % 2 == 0 ? Retail25Pins.Pin : "9999", Retail25Pins.Hash))));
        foreach (var (right, matched) in await Task.WhenAll(checks)) Assert.Equal(right, matched);
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
    private async Task<UserAccount> CarriedOverUserAsync(string hash, string? pinHash = null, string role = Permissions.CashierRole)
    {
        await using var ctx = db.CreateContext();
        var found = await ctx.Roles.SingleAsync(r => r.Name == role);
        var user = new UserAccount
        {
            UserName = "r25" + Guid.NewGuid().ToString("N")[..8], RoleId = found.Id, AccessLevel = found.Level, PasswordHash = hash,
            StaffCode = pinHash is null ? null : "P" + Guid.NewGuid().ToString("N")[..8], PinHash = pinHash,
        };
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    private async Task<string?> StoredHashAsync(Guid userId)
    {
        await using var ctx = db.CreateContext();
        return await ctx.UserAccounts.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync();
    }

    private async Task<string?> StoredPinAsync(Guid userId)
    {
        await using var ctx = db.CreateContext();
        return await ctx.UserAccounts.Where(u => u.Id == userId).Select(u => u.PinHash).SingleAsync();
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

    [SkippableFact]
    public async Task A_carried_over_PIN_signs_in_and_is_rehashed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var user = await CarriedOverUserAsync(IdentityHashes.Cashier, Retail25Pins.Hash);
        var setup = new SetupService(db.Data);

        Assert.Null((await setup.SignInWithPinAsync(user.StaffCode!, "0043")).User);
        Assert.Equal(Retail25Pins.Hash, await StoredPinAsync(user.Id));

        var signedIn = await setup.SignInWithPinAsync(user.StaffCode!.ToLowerInvariant(), Retail25Pins.Pin);
        Assert.NotNull(signedIn.User);
        var stored = await StoredPinAsync(user.Id);
        Assert.StartsWith("pbkdf2$", stored, StringComparison.Ordinal);
        Assert.False(SetupService.IsRetail25PinHash(stored!));
        Assert.True(SetupService.VerifyPin(Retail25Pins.Pin, stored!));
        // The password is left as it was: only the secret that was proved is stored again.
        Assert.Equal(IdentityHashes.Cashier, await StoredHashAsync(user.Id));

        Assert.NotNull((await setup.SignInWithPinAsync(user.StaffCode, Retail25Pins.Pin)).User);
        Assert.Equal(stored, await StoredPinAsync(user.Id));

        await using var ctx = db.CreateContext();
        var lines = await ctx.AuditEntries.AsNoTracking().Where(a => a.EntityId == user.Id).ToListAsync();
        Assert.Single(lines, a => a.Action == AuditActions.SignIn && a.Details!.Contains("PIN now stored in the new format", StringComparison.Ordinal));
        Assert.Single(lines, a => a.Action == AuditActions.FailedSignIn);
        Assert.DoesNotContain(lines, a => a.Action == AuditActions.Changed);
    }

    [SkippableFact]
    public async Task Approval_with_a_carried_over_PIN_rehashes_too()
    {
        Skip.IfNot(db.Available, db.UnavailableReason);
        var user = await CarriedOverUserAsync(IdentityHashes.Cashier, Retail25Pins.Hash, Permissions.SupervisorRole);
        var (approver, problem) = await new SetupService(db.Data).ApproveAsync(user.StaffCode!, Retail25Pins.Pin, pin: true, Permissions.PosVoidSale, "Void sale");
        Assert.Null(problem);
        Assert.Equal(user.Id, approver!.Id);
        Assert.StartsWith("pbkdf2$", await StoredPinAsync(user.Id), StringComparison.Ordinal);
    }
}

public sealed class PasswordHashTestsOnSqlite(SqliteFixture db) : PasswordHashTests(db), IClassFixture<SqliteFixture>;

public sealed class PasswordHashTestsOnSqlServer(SqlServerFixture db) : PasswordHashTests(db), IClassFixture<SqlServerFixture>;
