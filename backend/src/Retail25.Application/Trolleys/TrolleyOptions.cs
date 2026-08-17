using System.Globalization;

namespace Retail25.Application.Trolleys;

/// <summary>
/// Which station codes the phone app may connect to, and whether it may create new ones.
/// <para>
/// The shopper types a station code. Most stations are not for shoppers — station 001 is the front
/// counter — so the range is what separates the two. Without it, typing the till's code would hand a
/// shopper whatever the cashier has on screen.
/// </para>
/// <para>
/// It is configuration rather than a literal because a shop numbers its own counters. This store uses
/// 301–320, so the 300 block is the default.
/// </para>
/// </summary>
public sealed class TrolleyOptions
{
    public const string Section = "Trolleys";

    public int MinStationCode { get; set; } = 300;

    public int MaxStationCode { get; set; } = 399;

    /// <summary>
    /// Register a station for app use the first time somebody connects to it, so adding a counter is
    /// one row in the setup screen staff already use rather than a second list to keep in step.
    /// </summary>
    public bool AutoRegister { get; set; } = true;

    /// <summary>
    /// Create the station itself when a code inside the range names one that does not exist yet.
    /// <para>
    /// This is the "it increases automatically" behaviour: once 301–320 are all taken, connecting to
    /// 321 brings 321 into existence rather than failing. Bounded by the range, so it can only ever
    /// grow into codes already reserved for shoppers.
    /// </para>
    /// </summary>
    public bool AutoCreateStation { get; set; } = true;

    public bool IsClaimable(string? stationCode)
        => int.TryParse(stationCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            && code >= MinStationCode
            && code <= MaxStationCode;
}
