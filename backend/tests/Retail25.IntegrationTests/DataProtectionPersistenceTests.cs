using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The keyring outlives the process.
/// <para>
/// Every authentication cookie, antiforgery token and OpenIddict token in this system is encrypted
/// with a Data Protection key. Unconfigured, the framework keeps those keys in a folder under the
/// profile of whoever started the process — which a container does not have, two replicas do not
/// share, and which is keyed by the content-root path. The practical result is a keyring that is new
/// on every start: everyone is signed out, and a login page fetched a moment before the restart comes
/// back "that form had expired" no matter how carefully it is filled in.
/// </para>
/// <para>
/// The test that matters is therefore not "can it encrypt" but "can a <em>different</em> instance
/// decrypt" — which is what a restart, a second replica and a rolling deploy all look like.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class DataProtectionPersistenceTests
{
    private readonly CommerceApiFixture _api;

    public DataProtectionPersistenceTests(CommerceApiFixture api) => _api = api;

    [RequiresDockerFact]
    public async Task The_keyring_is_in_the_database_rather_than_on_the_starting_user_profile()
    {
        using var scope = _api.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Force a key to exist: the ring is created lazily on first protect.
        scope.ServiceProvider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("probe")
            .Protect("anything");

        (await db.DataProtectionKeys.AsNoTracking().AnyAsync())
            .Should().BeTrue("the keys have to live somewhere every replica and every restart can read");
    }

    /// <summary>
    /// A token minted by one instance is readable by the next. This is the restart, expressed as a
    /// test: a second provider built over the same database, with no shared in-memory state.
    /// </summary>
    [RequiresDockerFact]
    public void A_payload_protected_by_one_instance_is_unprotected_by_another()
    {
        const string purpose = "Microsoft.AspNetCore.Antiforgery.AntiforgeryToken.v1";
        const string secret = "a token issued before the restart";

        string protectedPayload;

        using (var before = Instance())
        {
            protectedPayload = before
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(purpose)
                .Protect(secret);
        }

        // Nothing carried over but the database.
        using var after = Instance();

        var unprotected = after
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(purpose)
            .Unprotect(protectedPayload);

        unprotected.Should().Be(
            secret,
            "a restart must be invisible to whoever was signed in — otherwise every deploy signs "
            + "everybody out and every open login form stops working");
    }

    /// <summary>
    /// A separate service provider over the same configuration — as close to "the process started
    /// again" as a test can get without starting one.
    /// </summary>
    private ServiceProvider Instance()
    {
        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options => options
            .UseSqlServer(_api.ConnectionString)
            .UseSnakeCaseNamingConvention());

        // The same two calls the application makes. SetApplicationName is half the fix: without it
        // the isolation discriminator comes from the content-root path, so the same keyring read from
        // a different directory yields a different — and undecryptable — set of purposes.
        services
            .AddDataProtection()
            .SetApplicationName("Retail25")
            .PersistKeysToDbContext<ApplicationDbContext>();

        return services.BuildServiceProvider();
    }
}
