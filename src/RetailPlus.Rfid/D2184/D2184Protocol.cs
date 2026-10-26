using System.Globalization;

namespace RetailPlus.Rfid.D2184;

/// <summary>Command codes of the serial interface protocol V3.1 (as used by the D2184 vendor library).</summary>
public static class D2184Command
{
    public const byte ReadGpio = 0x60;
    public const byte WriteGpio = 0x61;
    public const byte SetAntennaDetector = 0x62;
    public const byte GetAntennaDetector = 0x63;
    public const byte SetTemporaryOutputPower = 0x66;
    public const byte SetLinkProfile = 0x69;
    public const byte GetLinkProfile = 0x6A;
    public const byte Reset = 0x70;
    public const byte GetFirmwareVersion = 0x72;
    public const byte SetWorkAntenna = 0x74;
    public const byte GetWorkAntenna = 0x75;
    public const byte SetOutputPower = 0x76;
    public const byte GetOutputPower = 0x77;
    public const byte SetFrequencyRegion = 0x78;
    public const byte GetFrequencyRegion = 0x79;
    public const byte SetBeeperMode = 0x7A;
    public const byte GetReaderTemperature = 0x7B;
    public const byte SetDrmMode = 0x7C;
    public const byte GetDrmMode = 0x7D;
    public const byte GetReturnLoss = 0x7E;
    public const byte RealTimeInventory = 0x89;
    public const byte FastSwitchInventory = 0x8A;
    public const byte SetAndSaveFastTid = 0x8D;
    public const byte GetFastTid = 0x8E;

    /// <summary>The value that switches Impinj fast TID on (0x00 switches it off).</summary>
    public const byte FastTidOn = 0x8D;
}

/// <summary>Error codes from the protocol's error code table.</summary>
public static class D2184Error
{
    public const byte Success = 0x10;
    public const byte AntennaMissing = 0x22;

    private static readonly Dictionary<byte, string> Names = new()
    {
        [0x10] = "command succeeded", [0x11] = "command failed", [0x20] = "CPU reset error", [0x21] = "open CW error",
        [0x22] = "antenna not connected", [0x23] = "write flash error", [0x24] = "read flash error", [0x25] = "set output power error",
        [0x31] = "tag inventory error", [0x32] = "tag read error", [0x33] = "tag write error", [0x34] = "tag lock error",
        [0x35] = "tag kill error", [0x36] = "no tag to operate on", [0x37] = "inventory ok but access failed", [0x38] = "buffer is empty",
        [0x40] = "tag access or password error", [0x41] = "invalid parameter", [0x42] = "word count too long", [0x43] = "memory bank out of range",
        [0x44] = "lock region out of range", [0x45] = "lock type out of range", [0x46] = "invalid reader address", [0x47] = "antenna id out of range",
        [0x48] = "output power out of range", [0x49] = "frequency region out of range", [0x4A] = "baud rate out of range", [0x4B] = "beeper mode out of range",
        [0x4C] = "EPC match too long", [0x4D] = "EPC match length error", [0x4E] = "invalid EPC match mode", [0x4F] = "invalid frequency range",
        [0x50] = "no RN16 from tag", [0x51] = "invalid DRM mode", [0x52] = "PLL cannot lock", [0x53] = "RF chip not responding",
        [0x54] = "output power not reached", [0x55] = "copyright authentication failed", [0x56] = "spectrum regulation error",
        [0x57] = "output power too low", [0xEE] = "return loss measurement failed",
    };

    public static string Describe(byte code) =>
        Names.TryGetValue(code, out var name) ? $"{name} (0x{code:X2})" : $"error 0x{code:X2}";
}

/// <summary>The protocol's frequency parameter table: index 0–6 are 865.0–868.0 MHz, 7–59 are 902.0–928.0 MHz.</summary>
public static class FrequencyTable
{
    public const int MaxIndex = 59;

    public static decimal Mhz(int index) => index <= 6 ? 865.0m + index * 0.5m : 902.0m + (index - 7) * 0.5m;

    public static string Label(int index) => Mhz(index).ToString("0.00", CultureInfo.InvariantCulture) + " MHz";

    /// <summary>The allowed index range of an RF specification.</summary>
    public static (int Min, int Max) Range(Core.Model.RfidFrequencyRegion region) => region switch
    {
        Core.Model.RfidFrequencyRegion.Etsi => (0, 6),
        Core.Model.RfidFrequencyRegion.Chn => (43, 53),
        _ => (7, 59),
    };
}

/// <summary>RSSI conversion: the protocol's table maps 98 → −31 dBm … 31 → −98 dBm, i.e. dBm = value − 129.</summary>
public static class Rssi
{
    public static int ToDbm(byte raw) => raw - 129;
}
