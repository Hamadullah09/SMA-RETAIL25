using Retail25.Domain.Configuration;

namespace Retail25.Infrastructure.Persistence.Seeding;

/// <summary>
/// The defaults a new installation starts with, bound from the <c>Seed</c> configuration section.
/// <para>
/// These are starting values, not rules. Every one of them becomes an editable database row the
/// moment it is written, so changing a tax rate later is a settings change rather than a redeploy.
/// They live in configuration so that standing up a store for a different country does not require
/// touching code.
/// </para>
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Whether to seed at all. Off in production once the store is established.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Also load the sample hiking and camping catalog from the legacy TST location.</summary>
    public bool DemoData { get; set; }

    // --- Location --------------------------------------------------------------------------

    /// <summary>Legacy three-character location code (guide p.3).</summary>
    public string LocationCode { get; set; } = "MAI";

    public string LocationName { get; set; } = "Main Store";

    /// <summary>IANA time zone used to derive the business date.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// Local time the trading day rolls over. Stores that trade past midnight set this to their
    /// closing time so a sale at 00:30 lands on the right day's takings.
    /// </summary>
    public TimeOnly BusinessDayStart { get; set; } = TimeOnly.MinValue;

    // --- Currency --------------------------------------------------------------------------

    public string CurrencyCode { get; set; } = "USD";

    public string CurrencyName { get; set; } = "US Dollar";

    public string CurrencySymbol { get; set; } = "$";

    public int CurrencyScale { get; set; } = 2;

    /// <summary>Smallest coin accepted, which drives cash rounding (guide p.84).</summary>
    public decimal MinimumTender { get; set; } = 0.01m;

    // --- Tax -------------------------------------------------------------------------------

    public string Tax1Name { get; set; } = "Tax 1";

    /// <summary>Percentage as entered: five percent is <c>5</c>, not <c>0.05</c>.</summary>
    public decimal Tax1Rate { get; set; }

    public string Tax2Name { get; set; } = "Tax 2";

    public decimal Tax2Rate { get; set; }

    /// <summary>Whether tax 2 is charged on an amount that already includes tax 1 (guide p.77).</summary>
    public bool Tax2Compound { get; set; }

    /// <summary>Whether shelf prices already contain tax.</summary>
    public TaxationType TaxationType { get; set; } = TaxationType.Exclusive;

    /// <summary>
    /// First day the seeded rates apply. Deliberately far in the past so that back-dated documents
    /// and imported history still find a configuration in force.
    /// </summary>
    public DateOnly TaxEffectiveFrom { get; set; } = new(2000, 1, 1);

    // --- Station ---------------------------------------------------------------------------

    /// <summary>Legacy station identifier, 001 to 999 (guide p.78).</summary>
    public string StationCode { get; set; } = "001";

    public string StationName { get; set; } = "Front Counter";
}
