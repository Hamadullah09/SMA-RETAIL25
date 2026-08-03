namespace Retail25.Domain.Terminals;

/// <summary>
/// Converts between the reader protocol's channel numbers and megahertz.
/// <para>
/// The device speaks in channel indices; people speak in megahertz. The conversion lives here, in one
/// place, because the question it answers is a legal one — whether a shop is transmitting inside the
/// band it is licensed for — and an answer that differs between the settings screen and the
/// diagnostics screen is worse than no answer at all.
/// </para>
/// <para>
/// The tables are the protocol's own frequency plan, one row per region.
/// </para>
/// </summary>
public static class RadioFrequencyPlan
{
    /// <summary>
    /// Where each region's channels sit on one shared numbering.
    /// <para>
    /// The reader does not restart its channel numbers per region — it indexes one table, and each
    /// region occupies a window in it. That is why FCC starts at 7 rather than 0: a live D2184B set
    /// to FCC reports channels 7 and 57 while its own utility displays 902.00 and 927.00 MHz, which
    /// fixes both the step (0.5 MHz) and the origin (898.5 MHz) with no room left to guess.
    /// </para>
    /// </summary>
    private static (double BaseMhz, double StepMhz, int First, int Last) PlanFor(RadioRegion region) => region switch
    {
        RadioRegion.Fcc => (898.500, 0.500, 7, 57),
        RadioRegion.Etsi => (865.100, 0.200, 0, 14),
        RadioRegion.Chn => (920.125, 0.250, 0, 19),

        // An unknown region falls back to the narrowest plan rather than the widest. Guessing wrong
        // in the permissive direction is unlicensed transmission; guessing wrong in the other is a
        // reader with less range than it could have, which somebody notices and fixes.
        _ => (865.100, 0.200, 0, 14),
    };

    public static double ToMegahertz(RadioRegion region, int channel)
    {
        var (baseMhz, step, _, _) = PlanFor(region);
        return Math.Round(baseMhz + (channel * step), 3);
    }

    /// <summary>The lowest channel the region uses. Not always zero — see <see cref="PlanFor"/>.</summary>
    public static int MinChannel(RadioRegion region) => PlanFor(region).First;

    /// <summary>The highest channel the region uses — the ceiling a settings form may accept.</summary>
    public static int MaxChannel(RadioRegion region) => PlanFor(region).Last;

    public static int ChannelCount(RadioRegion region)
    {
        var (_, _, first, last) = PlanFor(region);
        return last - first + 1;
    }

    /// <summary>The band's edges, for a screen that wants to name them beside the picker.</summary>
    public static (double LowMhz, double HighMhz) Band(RadioRegion region)
        => (ToMegahertz(region, MinChannel(region)), ToMegahertz(region, MaxChannel(region)));
}
