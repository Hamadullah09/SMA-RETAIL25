using System.Globalization;
using System.IO.Ports;
using System.Text;

namespace RetailPlus.Devices;

/// <summary>Builds ESC/POS byte streams for receipt printers (Epson TM and compatibles).</summary>
public sealed class EscPos
{
    private static readonly Encoding Cp1252 = CodePagesEncoding();
    private readonly List<byte> _bytes = [0x1B, 0x40, 0x1B, 0x74, 16];   // initialise, code page WPC1252

    private static Encoding CodePagesEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    public EscPos Text(string text)
    {
        _bytes.AddRange(Cp1252.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)));
        return this;
    }

    public EscPos Line(string text = "") => Text(text + "\n");
    public EscPos Bold(bool on) { _bytes.AddRange([0x1B, 0x45, (byte)(on ? 1 : 0)]); return this; }
    public EscPos DoubleSize(bool on) { _bytes.AddRange([0x1D, 0x21, (byte)(on ? 0x11 : 0)]); return this; }
    public EscPos Center(bool on) { _bytes.AddRange([0x1B, 0x61, (byte)(on ? 1 : 0)]); return this; }
    public EscPos Feed(int lines = 4) { _bytes.AddRange([0x1B, 0x64, (byte)lines]); return this; }
    public EscPos Cut() { _bytes.AddRange([0x1D, 0x56, 0x42, 0x00]); return this; }

    /// <summary>Pulse to open a cash drawer connected to the printer's drawer port (pin 2 or pin 5).</summary>
    public EscPos KickDrawer(int pin = 2) { _bytes.AddRange([0x1B, 0x70, (byte)(pin == 5 ? 1 : 0), 25, 250]); return this; }

    public byte[] ToArray() => [.. _bytes];
}

/// <summary>Where receipts go: a Windows printer queue, or a text file when no printer is set up (and for tests).</summary>
public interface IReceiptPrinter
{
    void Print(string receiptText, bool openDrawer, bool cut = true);
    void OpenDrawer();
}

public sealed class WindowsReceiptPrinter(string printerName, int drawerPin = 2) : IReceiptPrinter
{
    public void Print(string receiptText, bool openDrawer, bool cut = true)
    {
        var job = new EscPos().Text(receiptText).Feed();
        if (cut) job.Cut();
        if (openDrawer) job.KickDrawer(drawerPin);
        RawPrinter.Send(printerName, job.ToArray());
    }

    public void OpenDrawer() => RawPrinter.Send(printerName, new EscPos().KickDrawer(drawerPin).ToArray(), "Open drawer");
}

/// <summary>Writes each receipt to a file in a folder (the "printer" of a station without one).</summary>
public sealed class FileReceiptPrinter(string folder) : IReceiptPrinter
{
    public string? LastFile { get; private set; }

    public void Print(string receiptText, bool openDrawer, bool cut = true)
    {
        Directory.CreateDirectory(folder);
        LastFile = Path.Combine(folder, $"receipt_{DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)}.txt");
        File.WriteAllText(LastFile, receiptText + (openDrawer ? "\n[drawer opened]\n" : ""));
    }

    public void OpenDrawer() { }
}

/// <summary>A two-line, 20-column customer pole display on a serial port.</summary>
public sealed class PoleDisplay(string portName, int baudRate = 9600) : IDisposable
{
    private SerialPort? _port;

    public void Show(string line1, string line2 = "")
    {
        _port ??= Open();
        _port.Write([0x0C], 0, 1);                      // clear
        _port.Write(Pad(line1) + Pad(line2));
    }

    private SerialPort Open()
    {
        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One) { WriteTimeout = 500, Encoding = Encoding.ASCII };
        port.Open();
        return port;
    }

    private static string Pad(string s) => s.Length >= 20 ? s[..20] : s.PadRight(20);

    public void Dispose()
    {
        _port?.Dispose();
        _port = null;
    }
}

/// <summary>A serial weigh scale using the common "W" request / weight reply protocol (NCI / Avery / CAS ECR mode).</summary>
public sealed class SerialScale(string portName, int baudRate = 9600)
{
    public decimal? ReadWeight(int timeoutMs = 1500)
    {
        using var port = new SerialPort(portName, baudRate, Parity.Even, 7, StopBits.One) { ReadTimeout = timeoutMs, WriteTimeout = timeoutMs, NewLine = "\r" };
        port.Open();
        port.Write("W\r");
        var reply = port.ReadLine();
        var digits = new string(reply.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var weight) ? weight : null;
    }
}

/// <summary>A cash drawer on its own serial port (opens when any byte arrives).</summary>
public sealed class SerialCashDrawer(string portName)
{
    public void Open()
    {
        using var port = new SerialPort(portName, 9600);
        port.Open();
        port.Write([0x07], 0, 1);
    }
}
