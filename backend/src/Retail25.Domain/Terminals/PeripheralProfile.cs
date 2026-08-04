using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

/// <summary>
/// Weigh-scale wiring (guide p.81). The command characters and the line settings are configuration
/// because scales from different makers answer to different letters, and a store should not need a
/// release to swap one.
/// </summary>
public sealed class ScaleProfile : Entity, IAuditable, IStationScopedProfile
{
    public ScaleProfile()
    {
    }

    public long LocationId { get; set; }

    public long? StationId { get; set; }

    public string Name { get; set; } = "Default";

    public string Port { get; set; } = "COM1";

    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 7;

    /// <summary>"None", "Odd", "Even", "Mark", "Space" — matched to <c>System.IO.Ports.Parity</c> by the agent.</summary>
    public string Parity { get; set; } = "Even";

    /// <summary>"One", "Two", "OnePointFive".</summary>
    public string StopBits { get; set; } = "One";

    /// <summary>Character that asks for a weight. Mettler-Toledo answers to <c>W</c> (guide p.81).</summary>
    public string GetWeightCommand { get; set; } = "W";

    /// <summary>Character that zeroes the platter. Default <c>Z</c>.</summary>
    public string ZeroCommand { get; set; } = "Z";

    /// <summary>Unit label echoed to the UI and stamped on the line ("kg", "lb").</summary>
    public string Unit { get; set; } = "kg";

    public int TimeoutMs { get; set; } = 1500;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static ScaleProfile CreateDefault(long locationId) => new() { LocationId = locationId };
}

/// <summary>
/// Customer-facing pole display (guide p.80–81). Line lengths differ by model, so the truncation
/// widths are data: a PD3000 shows 45 characters scrolling on line 1 and 19 fixed on line 2.
/// </summary>
public sealed class PoleDisplayProfile : Entity, IAuditable, IStationScopedProfile
{
    public PoleDisplayProfile()
    {
    }

    public long LocationId { get; set; }

    public long? StationId { get; set; }

    public string Name { get; set; } = "Default";

    public string Port { get; set; } = "COM2";

    public int BaudRate { get; set; } = 9600;

    public int Line1Width { get; set; } = 45;

    public int Line2Width { get; set; } = 19;

    /// <summary>Shown between sales. Line 1 scrolls, line 2 is fixed.</summary>
    public string IdleLine1 { get; set; } = "Welcome";

    public string IdleLine2 { get; set; } = string.Empty;

    /// <summary>Decimal-ASCII escape sequence that clears the display.</summary>
    public string ClearCommand { get; set; } = "12";

    /// <summary>Cursor-to-line-1 and cursor-to-line-2 sequences.</summary>
    public string Line1Command { get; set; } = "27,81,65";

    public string Line2Command { get; set; } = "27,81,66";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static PoleDisplayProfile CreateDefault(long locationId) => new() { LocationId = locationId };

    /// <summary>Clips a line to the width this model can actually show, so text never wraps into noise.</summary>
    public string FitLine1(string? text) => Fit(text, Line1Width);

    public string FitLine2(string? text) => Fit(text, Line2Width);

    private static string Fit(string? text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= width ? text : text[..width];
    }
}
