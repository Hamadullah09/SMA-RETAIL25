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
    /// <summary>Frequency of channel zero, the spacing between channels, and how many there are.</summary>
    private static (double BaseMhz, double StepMhz, int Channels) PlanFor(RadioRegion region) => region switch
    {
        RadioRegion.Etsi => (865.100, 0.200, 15),
        RadioRegion.Fcc => (902.750, 0.500, 50),
        RadioRegion.Chn => (920.125, 0.250, 20),

        // An unknown region falls back to the narrowest plan rather than the widest. Guessing wrong
        // in the permissive direction is unlicensed transmission; guessing wrong in the other is a
        // reader with less range than it could have, which somebody will notice and fix.
        _ => (865.100, 0.200, 15),
    };

    public static double ToMegahertz(RadioRegion region, int channel)
    {
        var (baseMhz, step, _) = PlanFor(region);
        return Math.Round(baseMhz + (channel * step), 3);
    }

    public static int ChannelCount(RadioRegion region) => PlanFor(region).Channels;

    /// <summary>The highest channel the region defines — the ceiling a settings form may accept.</summary>
    public static int MaxChannel(RadioRegion region) => Math.Max(0, ChannelCount(region) - 1);

    /// <summary>The band's edges, for a screen that wants to say "865.1 – 868.1 MHz" beside the picker.</summary>
    public static (double LowMhz, double HighMhz) Band(RadioRegion region)
        => (ToMegahertz(region, 0), ToMegahertz(region, MaxChannel(region)));
}
