namespace RetailPlus.Core.Model;

public enum RfidTransport
{
    Tcp,
    Serial,
}

/// <summary>RF specification of the reader (cmd_set_frequency_region).</summary>
public enum RfidFrequencyRegion
{
    Fcc = 1,
    Etsi = 2,
    Chn = 3,
    Custom = 4,
}

/// <summary>Buzzer behaviour (cmd_set_beeper_mode).</summary>
public enum RfidBeeperMode
{
    Quiet = 0,
    AfterInventory = 1,
    EveryTag = 2,
}

/// <summary>Gen2 link profile (cmd_set_rf_link_profile, 0xD0 + n).</summary>
public enum RfidLinkProfile
{
    /// <summary>Tari 25 µs, FM0 40 kHz.</summary>
    Profile0 = 0,
    /// <summary>Tari 25 µs, Miller 4 250 kHz — the vendor's recommended default.</summary>
    Profile1 = 1,
    /// <summary>Tari 25 µs, Miller 4 300 kHz.</summary>
    Profile2 = 2,
    /// <summary>Tari 6.25 µs, FM0 400 kHz.</summary>
    Profile3 = 3,
}

/// <summary>What an antenna is used for. Only point-of-sale antennas send reads to a station's sale.</summary>
public enum RfidAntennaPurpose
{
    PointOfSale,
    TagRegistration,
    Gate,
    Shelf,
}

/// <summary>
/// A UHF RFID reader (D2184 family, serial interface protocol V3.1) on the network or a serial port. The radio
/// settings are kept here and pushed to the reader by an administrator, so a replaced reader is set up again in one
/// click.
/// </summary>
public class RfidReader : IVersioned
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Which RFID hub owns the connection; several hubs can share a large installation.</summary>
    public string HubKey { get; set; } = "main";
    public string? Location { get; set; }
    public bool IsEnabled { get; set; } = true;

    public RfidTransport Transport { get; set; } = RfidTransport.Tcp;
    public string? Host { get; set; } = "192.168.0.178";
    public int Port { get; set; } = 4001;
    public string? SerialPort { get; set; }
    public int BaudRate { get; set; } = 115200;
    /// <summary>RS-485 address; 255 is the public address every reader answers.</summary>
    public int Address { get; set; } = 255;

    // Radio settings
    public RfidFrequencyRegion Region { get; set; } = RfidFrequencyRegion.Fcc;
    /// <summary>Index into the frequency table (0 = 865.00 MHz … 59 = 928.00 MHz).</summary>
    public int StartFrequency { get; set; } = 7;
    public int EndFrequency { get; set; } = 59;
    public int CustomStartKHz { get; set; } = 915000;
    /// <summary>Channel spacing in units of 10 kHz.</summary>
    public int CustomSpacing { get; set; } = 50;
    public int CustomChannels { get; set; } = 1;
    public RfidBeeperMode BeeperMode { get; set; } = RfidBeeperMode.Quiet;
    public RfidLinkProfile LinkProfile { get; set; } = RfidLinkProfile.Profile1;
    public bool DrmEnabled { get; set; }
    /// <summary>Antenna connection detection threshold in dB (return loss); 0 switches detection off.</summary>
    public int AntennaDetectorDb { get; set; }
    public bool FastTid { get; set; }

    // How the hub reads
    /// <summary>Reads weaker than this (dBm) are ignored, so tags further away from the counter do not ring up.</summary>
    public int RssiThresholdDbm { get; set; } = -75;
    /// <summary>The same tag on the same station is reported again only after this many seconds.</summary>
    public int DedupSeconds { get; set; } = 5;
    /// <summary>Inventory rounds per antenna before switching (the Stay of cmd_fast_switch_ant_inventory).</summary>
    public int DwellRounds { get; set; } = 1;

    public string? FirmwareVersion { get; set; }
    public DateTime? SettingsAppliedAt { get; set; }
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<RfidAntenna> Antennas { get; set; } = [];

    public string Endpoint => Transport == RfidTransport.Tcp ? $"{Host}:{Port}" : $"{SerialPort} @ {BaudRate}";
}

/// <summary>One of the four antenna ports of a reader, and the POS station it serves.</summary>
public class RfidAntenna
{
    public int Id { get; set; }
    public int ReaderId { get; set; }
    /// <summary>1 to 4.</summary>
    public int Port { get; set; }
    public string? Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    /// <summary>Transmit power, 0–33 dBm.</summary>
    public int PowerDbm { get; set; } = 30;
    public RfidAntennaPurpose Purpose { get; set; } = RfidAntennaPurpose.PointOfSale;
    /// <summary>The POS station number this antenna belongs to; null when not assigned.</summary>
    public int? StationNumber { get; set; }
}

public enum ItemTagStatus
{
    InStock,
    Sold,
    Lost,
}

/// <summary>An RFID tag (EPC) attached to one physical item, so ringing it up twice or walking out with it can be told.</summary>
public class ItemTag
{
    public int Id { get; set; }
    /// <summary>Upper-case hexadecimal, no spaces.</summary>
    public string Epc { get; set; } = "";
    public int ItemId { get; set; }
    public int? VariantId { get; set; }
    public string? SerialNumber { get; set; }
    public ItemTagStatus Status { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? SoldAt { get; set; }
    public long? SoldOnInvoice { get; set; }
}
