namespace Retail25.IntegrationTests;

/// <summary>
/// Hands out distinct entity ids for tests.
/// <para>
/// Replaces <c>Guid.NewGuid()</c>, which used to serve the same purpose. Under integer keys a test
/// still needs "an id that is not any other id", and a literal like <c>1</c> repeated across a file
/// is how two different products come to compare equal and a test passes for the wrong reason.
/// </para>
/// <para>
/// Starts high so a fabricated id can never collide with one a seeded database would generate, and
/// so a number appearing in a failure message is recognisably from here.
/// </para>
/// </summary>
internal static class TestIds
{
    private static long _next = 900_000_000;

    public static long Next() => Interlocked.Increment(ref _next);
}
