using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Retail25.Contracts.Terminals;
using Retail25.TerminalAgent.Server;

namespace Retail25.TerminalAgent.Peripherals;

/// <summary>
/// Owns every device on this till and executes what the server asks for.
/// <para>
/// One owner rather than four independent services, because the devices are not independent: the
/// drawer is kicked through the printer's port, printing may imply a kick, and both must not be
/// interleaved with another job halfway through a cut. A single lock around device access is the
/// simplest thing that makes that true.
/// </para>
/// <para>
/// Nothing here throws for a missing device. A till with no pole display still sells; a till that
/// crashed because a pole display was unplugged would not.
/// </para>
/// </summary>
public sealed class PeripheralCoordinator : ITerminalCommandHandler, IDisposable
{
    private readonly IDeviceFactory _factory;
    private readonly ILogger<PeripheralCoordinator> _logger;
    private readonly SemaphoreSlim _deviceGate = new(1, 1);

    private TerminalProfileContract? _profile;
    private IDeviceWriter? _printer;
    private IDeviceTransceiver? _scale;
    private IDeviceWriter? _poleDisplay;

    private Func<decimal, string, bool, CancellationToken, Task>? _onWeight;
    private Func<ReaderMode, CancellationToken, Task>? _onReaderMode;
    private Func<TerminalProfileContract, CancellationToken, Task>? _onProfileChanged;

    public PeripheralCoordinator(IDeviceFactory factory, ILogger<PeripheralCoordinator> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public bool PrinterOnline => _printer?.IsAvailable ?? false;

    public bool ScaleOnline => _scale?.IsAvailable ?? false;

    public bool PoleDisplayOnline => _poleDisplay?.IsAvailable ?? false;

    /// <summary>The drawer is kicked through the printer, so its health is the printer's health.</summary>
    public bool DrawerOnline => PrinterOnline;

    public void OnWeightRead(Func<decimal, string, bool, CancellationToken, Task> handler) => _onWeight = handler;

    public void OnReaderModeChanged(Func<ReaderMode, CancellationToken, Task> handler) => _onReaderMode = handler;

    public void OnProfileChanged(Func<TerminalProfileContract, CancellationToken, Task> handler) => _onProfileChanged = handler;

    /// <summary>Opens the devices the profile describes, closing anything the previous profile owned.</summary>
    public async Task ApplyProfileAsync(TerminalProfileContract profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _deviceGate.WaitAsync(ct);

        try
        {
            CloseDevices();
            _profile = profile;

            if (profile.Printer is { } printer)
            {
                _printer = _factory.CreateWriter(printer.Port);
                _logger.LogInformation("Printer: {Device}", _printer.Description);
            }

            if (profile.Scale is { } scale)
            {
                _scale = _factory.CreateTransceiver(scale.Port, scale.BaudRate, scale.DataBits, scale.Parity, scale.StopBits);
                _logger.LogInformation("Scale: {Device}", _scale.Description);
            }

            if (profile.PoleDisplay is { } pole)
            {
                _poleDisplay = _factory.CreateWriter(pole.Port);
                _logger.LogInformation("Pole display: {Device}", _poleDisplay.Description);
            }
        }
        finally
        {
            _deviceGate.Release();
        }

        await ShowIdleAsync(ct);
    }

    public async Task PrintReceiptAsync(ReceiptDocument document, int copies, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_profile?.Printer is not { } profile || _printer is null)
        {
            _logger.LogWarning("A receipt arrived but this till has no printer configured");
            return;
        }

        var payload = EscPosRenderer.Render(document, profile);
        var total = Math.Max(1, copies);

        await _deviceGate.WaitAsync(ct);

        try
        {
            for (var i = 0; i < total; i++)
            {
                await _printer.WriteAsync(payload, ct);
            }

            if (profile.OpenDrawerOnPrint)
            {
                await KickDrawerAsync(profile, ct);
            }
        }
        finally
        {
            _deviceGate.Release();
        }

        _logger.LogInformation(
            "Printed transaction {TransactionNumber} ×{Copies} to {Device}",
            document.TransactionNumber,
            total,
            _printer.Description);
    }

    public async Task OpenDrawerAsync(CancellationToken ct)
    {
        if (_profile?.Printer is not { } profile)
        {
            _logger.LogWarning("A drawer pop arrived but this till has no printer profile to kick through");
            return;
        }

        await _deviceGate.WaitAsync(ct);

        try
        {
            await KickDrawerAsync(profile, ct);
        }
        finally
        {
            _deviceGate.Release();
        }
    }

    private async Task KickDrawerAsync(PrinterProfileContract profile, CancellationToken ct)
    {
        var pulse = EscapeSequence.Parse(profile.DrawerTrigger);

        if (pulse.Length == 0 || _printer is null)
        {
            return;
        }

        // Some drawers need the pulse more than once; the repeat count is configuration for exactly
        // that reason (guide p.80).
        for (var i = 0; i < Math.Max(1, profile.DrawerRepeat); i++)
        {
            await _printer.WriteAsync(pulse, ct);
        }

        _logger.LogInformation("Drawer kicked with {Pulse}", EscapeSequence.Format(pulse));
    }

    public async Task DisplayPoleAsync(string line1, string line2, CancellationToken ct)
    {
        if (_profile?.PoleDisplay is not { } profile || _poleDisplay is null)
        {
            return;
        }

        var payload = BuildPolePayload(profile, line1, line2);

        await _deviceGate.WaitAsync(ct);

        try
        {
            await _poleDisplay.WriteAsync(payload, ct);
        }
        finally
        {
            _deviceGate.Release();
        }
    }

    /// <summary>Puts the idle greeting back up. Called after a sale and whenever a profile is applied.</summary>
    public Task ShowIdleAsync(CancellationToken ct)
        => _profile?.PoleDisplay is { } profile
            ? DisplayPoleAsync(profile.IdleLine1, profile.IdleLine2, ct)
            : Task.CompletedTask;

    private static byte[] BuildPolePayload(PoleDisplayProfileContract profile, string line1, string line2)
    {
        using var stream = new MemoryStream();

        void Write(byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
        void WriteText(string text, int width) => Write(Encoding.ASCII.GetBytes(Fit(text, width)));

        Write(EscapeSequence.Parse(profile.ClearCommand));
        Write(EscapeSequence.Parse(profile.Line1Command));
        WriteText(line1, profile.Line1Width);
        Write(EscapeSequence.Parse(profile.Line2Command));
        WriteText(line2, profile.Line2Width);

        return stream.ToArray();

        // Clipped, never wrapped: a PD3000 that runs past its line length shows the overflow in the
        // wrong place rather than on a second line.
        static string Fit(string? text, int width)
            => string.IsNullOrEmpty(text) ? string.Empty : text.Length <= width ? text : text[..width];
    }

    public async Task RequestWeightAsync(CancellationToken ct)
    {
        if (_profile?.Scale is not { } profile || _scale is null)
        {
            _logger.LogWarning("A weight was requested but this till has no scale configured");
            return;
        }

        var response = await _scale.QueryAsync(profile.GetWeightCommand, TimeSpan.FromMilliseconds(profile.TimeoutMs), ct);

        if (!TryParseWeight(response, out var value, out var stable))
        {
            _logger.LogWarning("Could not read a weight from {Device} (answer: {Response})", _scale.Description, response);
            return;
        }

        if (_onWeight is not null)
        {
            await _onWeight(value, profile.Unit, stable, ct);
        }
    }

    public async Task ZeroScaleAsync(CancellationToken ct)
    {
        if (_profile?.Scale is not { } profile || _scale is null)
        {
            return;
        }

        await _scale.QueryAsync(profile.ZeroCommand, TimeSpan.FromMilliseconds(profile.TimeoutMs), ct);
        _logger.LogInformation("Scale zeroed");
    }

    /// <summary>
    /// Scales answer in several dialects. What they have in common is a number somewhere and, often,
    /// a stability marker — so the number is extracted rather than the format assumed. An unstable
    /// reading is reported as unstable rather than discarded, because the cashier watching the platter
    /// is better placed than the agent to decide whether to wait.
    /// </summary>
    internal static bool TryParseWeight(string? response, out decimal value, out bool stable)
    {
        value = 0m;
        stable = false;

        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        var text = response.Trim();

        // A leading or trailing "US"/"?" marks an unstable reading on Mettler-Toledo units.
        stable = !text.Contains('?', StringComparison.Ordinal)
                 && !text.Contains("US", StringComparison.OrdinalIgnoreCase);

        var digits = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsAsciiDigit(character) || character == '.' || (character == '-' && digits.Length == 0))
            {
                digits.Append(character);
            }
            else if (digits.Length > 0)
            {
                break;
            }
        }

        return digits.Length > 0
               && decimal.TryParse(digits.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public async Task SetReaderModeAsync(ReaderMode mode, CancellationToken ct)
    {
        if (_onReaderMode is not null)
        {
            await _onReaderMode(mode, ct);
        }
    }

    public async Task UpdateProfileAsync(TerminalProfileContract profile, CancellationToken ct)
    {
        _logger.LogInformation("Applying an updated device profile from the server");
        await ApplyProfileAsync(profile, ct);

        if (_onProfileChanged is not null)
        {
            await _onProfileChanged(profile, ct);
        }
    }

    /// <summary>Self-test from the local API: a short slip, so staff can confirm the printer works.</summary>
    public async Task PrintTestAsync(CancellationToken ct)
    {
        if (_profile?.Printer is not { } profile || _printer is null)
        {
            return;
        }

        var slip = new StringBuilder()
            .AppendLine("Retail25 terminal agent")
            .AppendLine(CultureInfo.InvariantCulture, $"Station {_profile.StationCode}")
            .AppendLine(CultureInfo.InvariantCulture, $"Agent {AgentVersion.Current}")
            .AppendLine(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .AppendLine()
            .AppendLine("Printer test succeeded.")
            .AppendLine()
            .AppendLine()
            .ToString();

        using var stream = new MemoryStream();
        var setup = EscapeSequence.Parse(profile.SetupCommand);
        stream.Write(setup, 0, setup.Length);

        var body = Encoding.ASCII.GetBytes(slip);
        stream.Write(body, 0, body.Length);

        var cutter = EscapeSequence.Parse(profile.CutterCommand);
        stream.Write(cutter, 0, cutter.Length);

        await _deviceGate.WaitAsync(ct);

        try
        {
            await _printer.WriteAsync(stream.ToArray(), ct);
        }
        finally
        {
            _deviceGate.Release();
        }
    }

    private void CloseDevices()
    {
        _printer?.Dispose();
        _scale?.Dispose();
        _poleDisplay?.Dispose();

        _printer = null;
        _scale = null;
        _poleDisplay = null;
    }

    public void Dispose()
    {
        CloseDevices();
        _deviceGate.Dispose();
    }
}
