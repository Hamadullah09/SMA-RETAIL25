using System.Globalization;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Retail25.TerminalAgent.Peripherals;

/// <summary>
/// Somewhere bytes can be sent: a COM port, a parallel port, a share, or a network printer.
/// <para>
/// The abstraction is what makes the peripheral services testable at all — a unit test can assert
/// the exact drawer-kick bytes without a printer, and a developer machine can run the agent with
/// every device disabled instead of failing to open COM1.
/// </para>
/// </summary>
public interface IDeviceWriter : IDisposable
{
    string Description { get; }

    bool IsAvailable { get; }

    Task WriteAsync(byte[] bytes, CancellationToken ct);
}

/// <summary>A duplex device — a scale is asked a question and answers.</summary>
public interface IDeviceTransceiver : IDeviceWriter
{
    Task<string?> QueryAsync(string command, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Creates writers from the port string in a device profile.</summary>
public interface IDeviceFactory
{
    IDeviceWriter CreateWriter(string? port);

    IDeviceTransceiver CreateTransceiver(string port, int baudRate, int dataBits, string parity, string stopBits);
}

public sealed class DeviceFactory : IDeviceFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly bool _disabled;

    public DeviceFactory(ILoggerFactory loggerFactory, bool disabled)
    {
        _loggerFactory = loggerFactory;
        _disabled = disabled;
    }

    public IDeviceWriter CreateWriter(string? port)
    {
        if (_disabled || string.IsNullOrWhiteSpace(port))
        {
            return new NullDeviceWriter(port ?? "none", _loggerFactory.CreateLogger<NullDeviceWriter>());
        }

        // "host:port" is a network printer; anything with a path separator is a share or a device
        // file; everything else is a serial or parallel port name.
        if (TryParseEndpoint(port, out var host, out var tcpPort))
        {
            return new NetworkDeviceWriter(host, tcpPort, _loggerFactory.CreateLogger<NetworkDeviceWriter>());
        }

        if (port.Contains('/', StringComparison.Ordinal) || port.Contains('\\', StringComparison.Ordinal))
        {
            return new FileDeviceWriter(port, _loggerFactory.CreateLogger<FileDeviceWriter>());
        }

        return new SerialDeviceWriter(port, 9600, 8, "None", "One", _loggerFactory.CreateLogger<SerialDeviceWriter>());
    }

    public IDeviceTransceiver CreateTransceiver(string port, int baudRate, int dataBits, string parity, string stopBits)
    {
        if (_disabled || string.IsNullOrWhiteSpace(port))
        {
            return new NullDeviceWriter(port ?? "none", _loggerFactory.CreateLogger<NullDeviceWriter>());
        }

        return new SerialDeviceWriter(port, baudRate, dataBits, parity, stopBits, _loggerFactory.CreateLogger<SerialDeviceWriter>());
    }

    private static bool TryParseEndpoint(string port, out string host, out int tcpPort)
    {
        host = string.Empty;
        tcpPort = 0;

        var parts = port.Split(':', 2);
        return parts.Length == 2
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out tcpPort)
               && !string.IsNullOrWhiteSpace(host = parts[0]);
    }
}

/// <summary>Accepts and logs everything. Used when peripherals are disabled or unconfigured.</summary>
public sealed class NullDeviceWriter : IDeviceTransceiver
{
    private readonly ILogger _logger;

    public NullDeviceWriter(string description, ILogger logger)
    {
        Description = $"none ({description})";
        _logger = logger;
    }

    public string Description { get; }

    /// <summary>
    /// Reported as unavailable so the status strip is honest: a till with no printer configured shows
    /// red rather than pretending receipts are printing into a void.
    /// </summary>
    public bool IsAvailable => false;

    public Task WriteAsync(byte[] bytes, CancellationToken ct)
    {
        _logger.LogDebug("Discarding {Count} bytes for {Device}", bytes.Length, Description);
        return Task.CompletedTask;
    }

    public Task<string?> QueryAsync(string command, TimeSpan timeout, CancellationToken ct)
        => Task.FromResult<string?>(null);

    public void Dispose()
    {
    }
}

public sealed class SerialDeviceWriter : IDeviceTransceiver
{
    private readonly ILogger _logger;
    private readonly SerialPort? _port;

    public SerialDeviceWriter(string portName, int baudRate, int dataBits, string parity, string stopBits, ILogger logger)
    {
        _logger = logger;
        Description = $"{portName} @ {baudRate}";

        try
        {
            _port = new SerialPort(portName, baudRate, ParseParity(parity), dataBits, ParseStopBits(stopBits))
            {
                ReadTimeout = 2000,
                WriteTimeout = 2000,
            };

            _port.Open();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // A missing or busy port is a configuration problem, not a crash. The status strip will
            // show the device offline and the till keeps selling.
            _logger.LogWarning(ex, "Could not open {Port}", Description);
            _port = null;
        }
    }

    public string Description { get; }

    public bool IsAvailable => _port?.IsOpen == true;

    public async Task WriteAsync(byte[] bytes, CancellationToken ct)
    {
        if (_port is not { IsOpen: true })
        {
            return;
        }

        await _port.BaseStream.WriteAsync(bytes, ct);
        await _port.BaseStream.FlushAsync(ct);
    }

    public async Task<string?> QueryAsync(string command, TimeSpan timeout, CancellationToken ct)
    {
        if (_port is not { IsOpen: true })
        {
            return null;
        }

        _port.DiscardInBuffer();
        await WriteAsync(Encoding.ASCII.GetBytes(command), ct);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        var buffer = new byte[64];

        try
        {
            var read = await _port.BaseStream.ReadAsync(buffer, deadline.Token);
            return read == 0 ? null : Encoding.ASCII.GetString(buffer, 0, read).Trim();
        }
        catch (OperationCanceledException)
        {
            // A scale that does not answer in time is a stale reading waiting to happen; better no
            // weight at all than a weight from the previous item.
            _logger.LogWarning("{Port} did not answer '{Command}' within {Timeout}", Description, command, timeout);
            return null;
        }
    }

    public void Dispose() => _port?.Dispose();

    private static Parity ParseParity(string parity)
        => Enum.TryParse<Parity>(parity, ignoreCase: true, out var parsed) ? parsed : Parity.None;

    private static StopBits ParseStopBits(string stopBits)
        => Enum.TryParse<StopBits>(stopBits, ignoreCase: true, out var parsed) ? parsed : StopBits.One;
}

/// <summary>A parallel port, a device file or a Windows share — anything opened as a stream.</summary>
public sealed class FileDeviceWriter : IDeviceWriter
{
    private readonly string _path;
    private readonly ILogger _logger;

    public FileDeviceWriter(string path, ILogger logger)
    {
        _path = path;
        _logger = logger;
        Description = path;
    }

    public string Description { get; }

    public bool IsAvailable => true;

    public async Task WriteAsync(byte[] bytes, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            await stream.WriteAsync(bytes, ct);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Writing to {Path} failed", _path);
            throw;
        }
    }

    public void Dispose()
    {
    }
}

/// <summary>A network printer on raw port 9100 or similar.</summary>
public sealed class NetworkDeviceWriter : IDeviceWriter
{
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger _logger;

    public NetworkDeviceWriter(string host, int port, ILogger logger)
    {
        _host = host;
        _port = port;
        _logger = logger;
        Description = $"{host}:{port}";
    }

    public string Description { get; }

    public bool IsAvailable => true;

    public async Task WriteAsync(byte[] bytes, CancellationToken ct)
    {
        // A short-lived connection per job: network printers commonly accept one session at a time,
        // and holding one open blocks every other till in the shop.
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port, ct);

        await using var stream = client.GetStream();
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);

        _logger.LogDebug("Sent {Count} bytes to {Device}", bytes.Length, Description);
    }

    public void Dispose()
    {
    }
}
