using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>
/// Reads and writes a UHF reader's own configuration over the serial protocol.
/// <para>
/// Split out from <see cref="UhfSerialRfidReader"/> because it is a different job with a different
/// lifetime: inventorying is a long-lived loop over one connection, while this is a handful of
/// request/response exchanges somebody triggers from a settings screen. Keeping them apart is also
/// what lets the settings run on their own connection, so opening the screen cannot disturb a sale.
/// </para>
/// </summary>
internal static class UhfSerialSettings
{
    /// <summary>
    /// Asks the reader what it is actually doing.
    /// <para>
    /// Each field is fetched independently and a failure to answer leaves that one null rather than
    /// abandoning the rest. Readers differ in what they implement, and the screen is most useful
    /// precisely when something is wrong — which is the worst moment for it to refuse to draw.
    /// </para>
    /// </summary>
    public static async Task<ReaderDiagnostics> ReadAsync(
        UhfSerialControlChannel channel, int antennaPorts, CancellationToken ct)
    {
        var unavailable = new List<string>();

        var firmware = await channel.QueryAsync(UhfSerialCommand.GetFirmwareVersion, [], ct);
        var temperature = await channel.QueryAsync(UhfSerialCommand.GetReaderTemperature, [], ct);
        var power = await channel.QueryAsync(UhfSerialCommand.GetOutputPower, [], ct);
        var region = await channel.QueryAsync(UhfSerialCommand.GetFrequencyRegion, [], ct);
        var link = await channel.QueryAsync(UhfSerialCommand.GetRfLinkProfile, [], ct);
        var antenna = await channel.QueryAsync(UhfSerialCommand.GetWorkAntenna, [], ct);
        var detector = await channel.QueryAsync(UhfSerialCommand.GetAntConnectionDetector, [], ct);
        var fastTid = await channel.QueryAsync(UhfSerialCommand.GetImpinjFastTid, [], ct);
        var gpio = await channel.QueryAsync(UhfSerialCommand.ReadGpioValue, [], ct);

        Note(unavailable, firmware, "firmware version");
        Note(unavailable, temperature, "temperature");
        Note(unavailable, power, "transmit power");
        Note(unavailable, region, "frequency region");
        Note(unavailable, link, "link profile");

        return new ReaderDiagnostics
        {
            // Two bytes, major then minor: 08 02 is 8.2.
            FirmwareVersion = firmware is { Length: >= 2 } ? $"{firmware[0]}.{firmware[1]}" : null,

            TemperatureCelsius = ParseTemperature(temperature),
            OutputPowerDbm = ParsePower(power, antennaPorts),
            Region = ParseRegion(region)?.ToString(),
            FrequencyStartIndex = region is { Length: >= 3 } ? region[1] : null,
            FrequencyEndIndex = region is { Length: >= 3 } ? region[2] : null,
            LinkProfile = ParseLinkProfile(link)?.ToString(),

            // Ports are 0-based on the wire and 1-based everywhere a person sees them.
            WorkAntenna = antenna is { Length: >= 1 } ? antenna[0] + 1 : null,

            AntennaReturnLossThresholdDb = detector is { Length: >= 1 } ? detector[0] : null,
            ImpinjFastTid = fastTid is { Length: >= 1 } ? fastTid[0] != 0 : null,
            GpioInputs = gpio?.Select(b => b != 0).ToArray(),
            ReturnLossDb = await MeasureReturnLossAsync(channel, antennaPorts, ct),
            Unavailable = unavailable,
        };
    }

    /// <summary>
    /// Measures each antenna port's return loss — how much of the transmitted power comes straight
    /// back rather than leaving the antenna.
    /// <para>
    /// The one diagnostic that finds a physical fault. A port reading a few dB has a cable off, a
    /// connector wet, or an antenna damaged; a working one reads considerably more. It is also the
    /// number behind an intermittent till, because a marginal port fails only when warm.
    /// </para>
    /// <para>
    /// The reader transmits to measure, so this is slow and is skipped for ports that do not answer.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyDictionary<int, int>?> MeasureReturnLossAsync(
        UhfSerialControlChannel channel, int antennaPorts, CancellationToken ct)
    {
        var measured = new Dictionary<int, int>();

        for (var port = 0; port < antennaPorts; port++)
        {
            var reply = await channel.QueryAsync(UhfSerialCommand.GetRfPortReturnLoss, [(byte)port], ct);

            if (reply is { Length: >= 1 })
            {
                measured[port + 1] = reply[0];
            }
        }

        return measured.Count == 0 ? null : measured;
    }

    /// <summary>
    /// Pushes the profile's settings into the reader, returning what it would not take.
    /// <para>
    /// Order matters in one place: the region has to be set before the frequency range, because the
    /// reader validates the range against whichever region is currently selected and will reject a
    /// perfectly good FCC range while it still believes it is in Europe.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<string>> ApplyAsync(
        UhfSerialControlChannel channel, ReaderProfileContract profile, CancellationToken ct)
    {
        var refused = new List<string>();

        // Region first. See above.
        if (!await channel.CommandAsync(
                UhfSerialCommand.SetFrequencyRegion,
                [(byte)profile.Region, (byte)profile.FrequencyStartIndex, (byte)profile.FrequencyEndIndex],
                ct))
        {
            refused.Add($"frequency region {profile.Region}");
        }

        var powers = ParsePowerSetting(profile.OutputPowerDbm);

        if (powers.Length > 0 && !await channel.CommandAsync(UhfSerialCommand.SetOutputPower, powers, ct))
        {
            refused.Add($"transmit power {profile.OutputPowerDbm} dBm");
        }

        if (!await channel.CommandAsync(UhfSerialCommand.SetRfLinkProfile, [(byte)profile.LinkProfile], ct))
        {
            refused.Add($"link profile {profile.LinkProfile}");
        }

        if (!await channel.CommandAsync(UhfSerialCommand.SetBeeperMode, [(byte)profile.Beeper], ct))
        {
            refused.Add($"beeper {profile.Beeper}");
        }

        if (!await channel.CommandAsync(
                UhfSerialCommand.SetAntConnectionDetector,
                [(byte)Math.Clamp(profile.AntennaReturnLossThresholdDb, 0, 255)],
                ct))
        {
            refused.Add("antenna connection detector");
        }

        // Only sent when switched on. It is an Impinj extension, and a reader that does not know the
        // command would otherwise report a refusal on every single connect for a feature nobody asked
        // for — noise that trains people to ignore the list.
        if (profile.ImpinjFastTid
            && !await channel.CommandAsync(UhfSerialCommand.SetImpinjFastTid, [0x8D, 0x01], ct))
        {
            refused.Add("Impinj fast TID");
        }

        return refused;
    }

    /// <summary>
    /// Parses "30" or "30,30,25,25" into the bytes <c>SetOutputPower</c> expects.
    /// <para>
    /// The protocol takes either one value for every port or one per port, and which the reader
    /// accepts depends on the model — so whichever the profile expresses is what gets sent, and a
    /// reader that wanted the other shape refuses it and says so.
    /// </para>
    /// </summary>
    public static byte[] ParsePowerSetting(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return [];
        }

        var parts = setting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var powers = new List<byte>(parts.Length);

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var dbm))
            {
                return [];
            }

            // 33 dBm is the ceiling across this reader family; 0 is a legitimate "off".
            powers.Add((byte)Math.Clamp(dbm, 0, 33));
        }

        return powers.ToArray();
    }

    /// <summary>
    /// Two bytes: a sign flag then the magnitude. Readers live in ceilings and loading bays, and one
    /// that reports −5 °C should not be shown as 251.
    /// </summary>
    public static int? ParseTemperature(byte[]? reply)
        => reply is { Length: >= 2 } ? (reply[0] == 0 ? -reply[1] : reply[1]) : null;

    /// <summary>
    /// One byte means every port shares a setting; one byte per port means they differ. Expanded to
    /// one figure per port either way, so nothing downstream has to know which shape came back.
    /// </summary>
    public static IReadOnlyList<int>? ParsePower(byte[]? reply, int antennaPorts)
    {
        if (reply is null || reply.Length == 0)
        {
            return null;
        }

        return reply.Length == 1
            ? Enumerable.Repeat((int)reply[0], Math.Max(1, antennaPorts)).ToArray()
            : reply.Select(b => (int)b).ToArray();
    }

    public static RadioRegion? ParseRegion(byte[]? reply)
        => reply is { Length: >= 1 } && Enum.IsDefined(typeof(RadioRegion), (int)reply[0])
            ? (RadioRegion)reply[0]
            : null;

    public static RfLinkProfile? ParseLinkProfile(byte[]? reply)
        => reply is { Length: >= 1 } && Enum.IsDefined(typeof(RfLinkProfile), (int)reply[0])
            ? (RfLinkProfile)reply[0]
            : null;

    private static void Note(List<string> unavailable, byte[]? reply, string what)
    {
        if (reply is null || reply.Length == 0)
        {
            unavailable.Add(what);
        }
    }
}

