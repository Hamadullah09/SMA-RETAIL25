using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Retail25.ArchitectureTests;

/// <summary>
/// One setting decides whether this deployment admits to being served over plain HTTP, and three
/// security properties hang off it: OpenIddict's transport requirement, the <c>__Host-</c> prefix on
/// the identity and antiforgery cookies, and their <c>SecurePolicy</c>.
/// <para>
/// It became one setting deliberately. The cookies used to key off <c>IsDevelopment()</c> while
/// OpenIddict keyed off this flag, and the two disagreed for every deployment that is neither: the
/// API runs as Production over plain http in CI, so the antiforgery system met
/// <c>SecurePolicy = Always</c> on a non-SSL request and threw. ASP.NET Core does not degrade there,
/// it raises — so the sign-in page answered 500, on the page every end-to-end test starts from.
/// </para>
/// <para>
/// Collapsing them onto one switch means the shipped Production settings are now the only thing
/// standing between the live shop and cookies without <c>Secure</c>. That is what this pins. It is a
/// file assertion rather than a boot, because the failure it guards against is an edit to a settings
/// file — someone copying a line out of appsettings.Development.json to make something work locally
/// — and no amount of runtime testing catches that if the test host supplies its own configuration.
/// </para>
/// </summary>
public sealed class TransportSecuritySettingsTests
{
    /// <summary>
    /// The settings files as they ship. They are content of the API project, and land beside this
    /// assembly because these tests reference it — so what is read here is the same bytes that are
    /// published, not a copy in the source tree that a publish step might transform.
    /// </summary>
    private static JsonDocument Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);

        File.Exists(path).Should().BeTrue(
            $"{fileName} ships with the API, and a test that silently skips because it cannot find "
            + "the file is worse than no test");

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static bool? AllowInsecureHttp(JsonDocument settings)
        => settings.RootElement.TryGetProperty("OpenIddict", out var openIddict)
           && openIddict.TryGetProperty("AllowInsecureHttp", out var flag)
            ? flag.GetBoolean()
            : null;

    /// <summary>
    /// Production must not admit to plain HTTP — by saying no, or by not saying anything.
    /// </summary>
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Production.json")]
    public void The_shipped_settings_never_permit_plain_http(string fileName)
    {
        using var settings = Read(fileName);

        AllowInsecureHttp(settings).Should().NotBe(
            true,
            "{0} configures the live shop. Turning this on lifts OpenIddict's transport requirement "
            + "and drops the __Host- prefix and Secure flag from the identity and antiforgery "
            + "cookies — an authorization code and a session cookie crossing the network in the "
            + "clear. A deployment that genuinely is plain HTTP sets it in its own environment, "
            + "where it is a visible decision rather than an inherited default",
            fileName);
    }

    /// <summary>
    /// Development must keep saying yes, and say it explicitly.
    /// <para>
    /// The documented development flow runs the API on <c>http://localhost</c>. Without the flag it
    /// would take the strict branch and the sign-in page would 500 there too — so this is not
    /// symmetry for its own sake, it is the other half of the same contract. Explicitly, because a
    /// switch inferred from the absence of configuration is how a production deployment ends up on
    /// the relaxed branch by forgetting something.
    /// </para>
    /// </summary>
    [Fact]
    public void Development_says_so_explicitly()
    {
        using var settings = Read("appsettings.Development.json");

        AllowInsecureHttp(settings).Should().BeTrue(
            "the documented development flow is plain http://localhost, where __Host- can never be "
            + "satisfied and the antiforgery system throws rather than degrading");
    }
}
