using RetailPlus.Core.Model;

namespace RetailPlus.Rfid.D2184;

/// <summary>
/// A D2184 (R2000-based) UHF reader driven over the serial interface protocol V3.1: inventory on up to four antennas
/// and every setting of the vendor's "Reader Settings" and "RF Parameter Setting" pages.
/// </summary>
public sealed class D2184Device(RfidReader settings) : IRfidDevice
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InventoryTimeout = TimeSpan.FromSeconds(10);
    private readonly D2184Connection _link = new(settings);
    private int _workAntenna = -1;

    public bool IsConnected => _link.IsConnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _workAntenna = -1;
        await _link.ConnectAsync(cancellationToken);
    }

    public Task DisconnectAsync() => _link.CloseAsync();

    public ValueTask DisposeAsync() => _link.DisposeAsync();

    // Inventory -------------------------------------------------------------------------------------------------

    public async Task<InventoryRoundResult> InventoryAsync(IReadOnlyList<int> antennas, int dwellRounds, Action<TagRead> onTag, CancellationToken cancellationToken)
    {
        if (antennas.Count == 0) return new InventoryRoundResult(0, [], null);
        int tags = 0;
        var missing = new List<int>();
        void Intermediate(ReaderFrame f)
        {
            if (TryParseTag(f.Data, DateTime.Now) is { } tag)
            {
                tags++;
                onTag(tag);
            }
            else if (f.Data.Length == 2 && f.Data[1] == D2184Error.AntennaMissing)
            {
                missing.Add(f.Data[0] + 1);
            }
        }
        static bool IsFinal(ReaderFrame f) => f.Data.Length is 1 or 7;

        ReaderFrame final;
        byte rounds = (byte)Math.Clamp(dwellRounds, 1, 254);
        if (antennas.Count == 1)
        {
            // One antenna: select it, then real-time inventory (the most efficient command for many tags).
            await SelectAntennaAsync(antennas[0], cancellationToken);
            final = await _link.RequestAsync(D2184Command.RealTimeInventory, [rounds], InventoryTimeout, cancellationToken, IsFinal, Intermediate);
        }
        else
        {
            // Several antennas: let the reader switch between them itself (A, stay, B, stay, C, stay, D, stay, interval, repeat).
            var data = new byte[10];
            for (int i = 0; i < 4; i++)
            {
                data[i * 2] = i < antennas.Count ? (byte)(antennas[i] - 1) : (byte)0x04;
                data[i * 2 + 1] = rounds;
            }
            data[8] = 0;
            data[9] = 1;
            _workAntenna = -1;
            final = await _link.RequestAsync(D2184Command.FastSwitchInventory, data, InventoryTimeout, cancellationToken, IsFinal, Intermediate);
        }

        string? error = null;
        if (final.Data.Length == 1 && final.Data[0] != D2184Error.Success)
        {
            // "No tag" style answers are normal when the counter is empty.
            if (final.Data[0] != 0x36 && final.Data[0] != 0x31) error = D2184Error.Describe(final.Data[0]);
            if (final.Data[0] == D2184Error.AntennaMissing && antennas.Count == 1) missing.Add(antennas[0]);
        }
        return new InventoryRoundResult(tags, missing, error);
    }

    /// <summary>
    /// Decodes a tag frame of cmd 0x89/0x8A: FreqAnt (frequency index in the high 6 bits, antenna in the low 2),
    /// PC (2 bytes, EPC length in words in its top 5 bits), EPC, RSSI.
    /// </summary>
    internal static TagRead? TryParseTag(byte[] data, DateTime seenAt)
    {
        if (data.Length < 4 || data.Length % 2 != 0) return null;
        int epcLength = data.Length - 4;
        int pcWords = data[1] >> 3;
        if (pcWords * 2 > epcLength) return null;
        var epc = Convert.ToHexString(data, 3, epcLength);
        return new TagRead((data[0] & 0x03) + 1, epc, Convert.ToHexString(data, 1, 2), Rssi.ToDbm(data[^1]), data[0] >> 2, seenAt);
    }

    private async Task SelectAntennaAsync(int antenna, CancellationToken cancellationToken)
    {
        if (_workAntenna == antenna) return;
        await ExpectSuccessAsync(D2184Command.SetWorkAntenna, [(byte)(antenna - 1)], cancellationToken);
        _workAntenna = antenna;
    }

    // Commands --------------------------------------------------------------------------------------------------

    private Task<ReaderFrame> AskAsync(byte command, CancellationToken cancellationToken, params byte[] data) =>
        _link.RequestAsync(command, data, CommandTimeout, cancellationToken);

    private async Task ExpectSuccessAsync(byte command, byte[] data, CancellationToken cancellationToken)
    {
        var reply = await _link.RequestAsync(command, data, CommandTimeout, cancellationToken);
        if (reply.Data.Length < 1) throw new RfidException($"Empty reply to command 0x{command:X2}.");
        if (reply.Data[0] != D2184Error.Success) throw new RfidException(D2184Error.Describe(reply.Data[0]));
    }

    public async Task<ReaderInfo> ReadInfoAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        async Task<byte[]?> Try(string what, byte command)
        {
            try
            {
                return (await AskAsync(command, cancellationToken)).Data;
            }
            catch (RfidException ex)
            {
                problems.Add($"{what}: {ex.Message}");
                return null;
            }
        }

        var firmware = await Try("firmware", D2184Command.GetFirmwareVersion);
        var temperature = await Try("temperature", D2184Command.GetReaderTemperature);
        var power = await Try("output power", D2184Command.GetOutputPower);
        var region = await Try("frequency", D2184Command.GetFrequencyRegion);
        var profile = await Try("link profile", D2184Command.GetLinkProfile);
        var drm = await Try("DRM", D2184Command.GetDrmMode);
        var detector = await Try("antenna detection", D2184Command.GetAntennaDetector);
        var fastTid = await Try("fast TID", D2184Command.GetFastTid);
        var gpio = await Try("GPIO", D2184Command.ReadGpio);

        var info = new ReaderInfo
        {
            Firmware = firmware is { Length: 2 } ? $"{firmware[0]}.{firmware[1]:00}" : null,
            TemperatureC = temperature is { Length: 2 } ? (temperature[0] == 0 ? -temperature[1] : temperature[1]) : null,
            PowerDbm = power switch
            {
                { Length: 1 } => [power[0], power[0], power[0], power[0]],
                { Length: >= 4 } => [power[0], power[1], power[2], power[3]],
                _ => null,
            },
            LinkProfile = profile is { Length: 1 } && profile[0] is >= 0xD0 and <= 0xD3 ? (RfidLinkProfile)(profile[0] - 0xD0) : null,
            DrmEnabled = drm is { Length: 1 } ? drm[0] == 1 : null,
            AntennaDetectorDb = detector is { Length: 1 } ? detector[0] : null,
            FastTid = fastTid is { Length: 1 } ? fastTid[0] == D2184Command.FastTidOn : null,
            Gpio1High = gpio is { Length: 2 } ? gpio[0] == 1 : null,
            Gpio2High = gpio is { Length: 2 } ? gpio[1] == 1 : null,
            Problems = problems,
        };
        if (region is { Length: 3 } && region[0] is >= 1 and <= 3)
            info = info with { Region = (RfidFrequencyRegion)region[0], StartFrequency = region[1], EndFrequency = region[2] };
        else if (region is { Length: 6 } && region[0] == 4)
            info = info with { Region = RfidFrequencyRegion.Custom, CustomSpacing = region[1], CustomChannels = region[2], CustomStartKHz = (region[3] << 16) | (region[4] << 8) | region[5] };
        return info;
    }

    public async Task<IReadOnlyList<string>> ApplySettingsAsync(RfidReader target, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        async Task Step(string what, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (RfidException ex)
            {
                problems.Add($"{what}: {ex.Message}");
            }
        }

        var power = new byte[4];
        for (int port = 1; port <= 4; port++)
            power[port - 1] = (byte)Math.Clamp(target.Antennas.FirstOrDefault(a => a.Port == port)?.PowerDbm ?? 30, 0, 33);
        await Step("Output power", () => ExpectSuccessAsync(D2184Command.SetOutputPower, power, cancellationToken));

        await Step("Frequency", () =>
        {
            if (target.Region == RfidFrequencyRegion.Custom)
            {
                int khz = target.CustomStartKHz;
                return ExpectSuccessAsync(D2184Command.SetFrequencyRegion,
                    [4, (byte)Math.Clamp(target.CustomSpacing, 1, 255), (byte)Math.Clamp(target.CustomChannels, 1, 255), (byte)(khz >> 16), (byte)(khz >> 8), (byte)khz],
                    cancellationToken);
            }
            var (min, max) = FrequencyTable.Range(target.Region);
            int start = Math.Clamp(target.StartFrequency, min, max), end = Math.Clamp(target.EndFrequency, start, max);
            return ExpectSuccessAsync(D2184Command.SetFrequencyRegion, [(byte)target.Region, (byte)start, (byte)end], cancellationToken);
        });
        await Step("Buzzer", () => ExpectSuccessAsync(D2184Command.SetBeeperMode, [(byte)target.BeeperMode], cancellationToken));
        await Step("DRM", () => ExpectSuccessAsync(D2184Command.SetDrmMode, [target.DrmEnabled ? (byte)1 : (byte)0], cancellationToken));
        await Step("Antenna detection", () => ExpectSuccessAsync(D2184Command.SetAntennaDetector, [(byte)Math.Clamp(target.AntennaDetectorDb, 0, 255)], cancellationToken));
        await Step("Fast TID", () => ExpectSuccessAsync(D2184Command.SetAndSaveFastTid, [target.FastTid ? D2184Command.FastTidOn : (byte)0], cancellationToken));

        // Changing the link profile resets the reader, so only do it when it differs.
        await Step("Link profile", async () =>
        {
            var current = await AskAsync(D2184Command.GetLinkProfile, cancellationToken);
            byte wanted = (byte)(0xD0 + (int)target.LinkProfile);
            if (current.Data is [var value] && value == wanted) return;
            await ExpectSuccessAsync(D2184Command.SetLinkProfile, [wanted], cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            await ConnectAsync(cancellationToken);
        });
        _workAntenna = -1;
        return problems;
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await _link.SendAsync(D2184Command.Reset, [], cancellationToken);
        _workAntenna = -1;
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await ConnectAsync(cancellationToken);
    }

    public async Task<int> MeasureReturnLossAsync(int antenna, int frequencyIndex, CancellationToken cancellationToken)
    {
        await SelectAntennaAsync(antenna, cancellationToken);
        var reply = await AskAsync(D2184Command.GetReturnLoss, cancellationToken, (byte)Math.Clamp(frequencyIndex, 0, FrequencyTable.MaxIndex));
        if (reply.Data.Length != 1 || reply.Data[0] == 0xEE) throw new RfidException(D2184Error.Describe(0xEE));
        return reply.Data[0];
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        var reply = await AskAsync(D2184Command.GetFirmwareVersion, cancellationToken);
        if (reply.Data.Length != 2) throw new RfidException("Unexpected answer to the keep-alive.");
    }

    public Task WriteGpioAsync(int gpio, bool high, CancellationToken cancellationToken)
    {
        if (gpio is not (3 or 4)) throw new ArgumentOutOfRangeException(nameof(gpio), "Only GPIO 3 and 4 are outputs.");
        return ExpectSuccessAsync(D2184Command.WriteGpio, [(byte)gpio, high ? (byte)1 : (byte)0], cancellationToken);
    }
}
