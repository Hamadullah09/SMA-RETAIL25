using Retail25.Domain.Common;

namespace Retail25.Domain.Terminals;

public enum PrinterOutput
{
    Invoice = 0,
    Slip40 = 1,
    Slip20 = 2,
}

/// <summary>
/// Receipt/slip printer configuration (guide p.78–80). Consumed by the Terminal Agent
/// to drive ESC/POS commands. All escape codes are configurable, not hardcoded.
/// </summary>
public sealed class PrinterProfile : Entity, IAuditable
{
    private PrinterProfile()
    {
    }

    public Guid LocationId { get; set; }

    public string Name { get; set; } = "Default";

    /// <summary>Initialisation command (e.g. "27,77" for Epson).</summary>
    public string? SetupCommand { get; set; }

    /// <summary>Paper cutter command (e.g. "27,105" Epson, "27,100,48" Star).</summary>
    public string? CutterCommand { get; set; }

    public string? RedCommand { get; set; }

    public string? BlackCommand { get; set; }

    /// <summary>Port or USB path (e.g. "COM1", "LPT1", "/dev/usb/lp0").</summary>
    public string? Port { get; set; }

    public int DefaultCopies { get; set; } = 1;

    public bool PageEject { get; set; }

    /// <summary>Print extra copy on card sales (guide p.79).</summary>
    public bool ExtraCopyOnCard { get; set; }

    public bool InitializeSerial { get; set; }

    public PrinterOutput Output { get; set; } = PrinterOutput.Invoice;

    /// <summary>ESC/POS pulse to open cash drawer (guide p.80). Default: "27,112,0,50,250".</summary>
    public string DrawerTrigger { get; set; } = "27,112,0,50,250";

    public int DrawerRepeat { get; set; } = 1;

    public bool OpenDrawerOnPrint { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? ModifiedBy { get; set; }
}
