using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

public enum PrinterOutput
{
    Invoice = 0,
    Slip40 = 1,
    Slip20 = 2,
}

/// <summary>
/// Receipt and slip printer wiring (guide p.78–80).
/// <para>
/// Every escape sequence is a decimal-ASCII string in a column, not a constant in a driver. Epson
/// cuts with <c>27,105</c> and Star with <c>27,100,48</c>; the drawer kick is famously
/// <c>27,112,0,50,250</c> on one till and something else on the next. The legacy system made these
/// editable for exactly this reason, and hard-coding any of them would mean a code change every time
/// a store replaced a printer.
/// </para>
/// </summary>
public sealed class PrinterProfile : Entity, IAuditable, IStationScopedProfile
{
    public PrinterProfile()
    {
    }

    public long LocationId { get; set; }

    public long? StationId { get; set; }

    public string Name { get; set; } = "Default";

    /// <summary>Initialisation sequence sent before every document.</summary>
    public string? SetupCommand { get; set; }

    /// <summary>Paper cutter, e.g. Epson <c>27,105</c>, Star <c>27,100,48</c>.</summary>
    public string? CutterCommand { get; set; }

    public string? RedCommand { get; set; }

    public string? BlackCommand { get; set; }

    /// <summary>Port or device path: <c>COM1</c>, <c>LPT1</c>, a UNC share, or an IP:port.</summary>
    public string? Port { get; set; }

    public int DefaultCopies { get; set; } = 1;

    public bool PageEject { get; set; }

    /// <summary>Print an extra signature copy on card sales (guide p.79).</summary>
    public bool ExtraCopyOnCard { get; set; }

    public bool InitializeSerial { get; set; }

    public PrinterOutput Output { get; set; } = PrinterOutput.Slip40;

    /// <summary>Characters per line for the chosen output width.</summary>
    public int Columns { get; set; } = 40;

    /// <summary>Drawer kick sent through the printer port. Default is the Epson pulse (guide p.80).</summary>
    public string DrawerTrigger { get; set; } = "27,112,0,50,250";

    public int DrawerRepeat { get; set; } = 1;

    public bool OpenDrawerOnPrint { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public long? ModifiedBy { get; set; }

    public static PrinterProfile CreateDefault(long locationId, string name = "Default")
        => new() { LocationId = locationId, Name = name };
}
