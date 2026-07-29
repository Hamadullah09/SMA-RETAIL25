using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Retail25.Contracts.Terminals;

namespace Retail25.TerminalAgent.Rfid;

/// <summary>LLRP message types (EPCglobal LLRP 1.0.1 §16.1). Only the ones an inventory client needs.</summary>
internal static class LlrpMessageType
{
    public const ushort GetReaderCapabilities = 1;
    public const ushort GetReaderCapabilitiesResponse = 11;
    public const ushort SetReaderConfig = 3;
    public const ushort SetReaderConfigResponse = 13;
    public const ushort CloseConnection = 14;
    public const ushort CloseConnectionResponse = 4;
    public const ushort AddRoSpec = 20;
    public const ushort AddRoSpecResponse = 30;
    public const ushort DeleteRoSpec = 21;
    public const ushort DeleteRoSpecResponse = 31;
    public const ushort StartRoSpec = 22;
    public const ushort StartRoSpecResponse = 32;
    public const ushort StopRoSpec = 23;
    public const ushort StopRoSpecResponse = 33;
    public const ushort EnableRoSpec = 24;
    public const ushort EnableRoSpecResponse = 34;
    public const ushort DisableRoSpec = 25;
    public const ushort DisableRoSpecResponse = 35;
    public const ushort RoAccessReport = 61;
    public const ushort Keepalive = 62;
    public const ushort ReaderEventNotification = 63;
    public const ushort KeepaliveAck = 72;
}

/// <summary>A framed LLRP message: header fields plus the parameter payload.</summary>
internal sealed record LlrpMessage(ushort Type, uint MessageId, byte[] Payload);

/// <summary>
/// LLRP wire format.
/// <para>
/// The header is ten bytes: three reserved bits, a three-bit version, a ten-bit message type, a
/// four-byte total length and a four-byte message id. Parameters are then either TLV (type ≥ 128,
/// with an explicit length) or TV (high bit set, fixed length by type). Getting the TV lengths right
/// matters more than it looks: a wrong length does not fail loudly, it silently shifts every
/// subsequent field and produces plausible-looking garbage EPCs.
/// </para>
/// </summary>
internal static class LlrpCodec
{
    public const int HeaderLength = 10;
    public const byte ProtocolVersion = 1;

    /// <summary>Frames a message for the wire.</summary>
    public static byte[] Encode(ushort messageType, uint messageId, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[HeaderLength + payload.Length];

        // Bits: [3 reserved = 0][3 version][10 type]
        var versionAndType = (ushort)((ProtocolVersion << 10) | (messageType & 0x03FF));
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0, 2), versionAndType);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(2, 4), (uint)buffer.Length);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(6, 4), messageId);

        payload.CopyTo(buffer.AsSpan(HeaderLength));
        return buffer;
    }

    /// <summary>Reads the header. Returns the message type and the total frame length.</summary>
    public static (ushort Type, uint Length, uint MessageId) DecodeHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength)
        {
            throw new ArgumentException("An LLRP header is ten bytes.", nameof(header));
        }

        var versionAndType = BinaryPrimitives.ReadUInt16BigEndian(header[..2]);
        var type = (ushort)(versionAndType & 0x03FF);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(2, 4));
        var messageId = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(6, 4));

        return (type, length, messageId);
    }
}

/// <summary>LLRP parameter types used when building an ROSpec and reading a report.</summary>
internal static class LlrpParameter
{
    // TV parameters (high bit set on the first byte), with their fixed value lengths.
    public const byte AntennaId = 1;
    public const byte FirstSeenTimestampUtc = 2;
    public const byte FirstSeenTimestampUptime = 3;
    public const byte LastSeenTimestampUtc = 4;
    public const byte LastSeenTimestampUptime = 5;
    public const byte PeakRssi = 6;
    public const byte ChannelIndex = 7;
    public const byte TagSeenCount = 8;
    public const byte RoSpecId = 9;
    public const byte InventoryParameterSpecId = 10;
    public const byte C1G2Crc = 11;
    public const byte C1G2Pc = 12;
    public const byte Epc96 = 13;
    public const byte SpecIndex = 14;
    public const byte ClientRequestOpSpecResult = 15;
    public const byte AccessSpecId = 16;
    public const byte OpSpecId = 17;
    public const byte C1G2SingulationDetails = 18;
    public const byte C1G2XpcW1 = 19;
    public const byte C1G2XpcW2 = 20;

    // TLV parameters.
    public const ushort RoSpec = 177;
    public const ushort RoBoundarySpec = 178;
    public const ushort RoSpecStartTrigger = 179;
    public const ushort RoSpecStopTrigger = 182;
    public const ushort AiSpec = 183;
    public const ushort AiSpecStopTrigger = 184;
    public const ushort InventoryParameterSpec = 186;
    public const ushort RoReportSpec = 237;
    public const ushort TagReportContentSelector = 238;
    public const ushort TagReportData = 240;
    public const ushort EpcData = 241;
    public const ushort C1G2EpcMemorySelector = 348;

    /// <summary>
    /// Value length for a TV parameter, excluding its one-byte header. Anything unlisted is unknown,
    /// and an unknown TV parameter means the rest of the buffer cannot be trusted.
    /// </summary>
    public static int TvValueLength(byte type) => type switch
    {
        AntennaId => 2,
        FirstSeenTimestampUtc => 8,
        FirstSeenTimestampUptime => 8,
        LastSeenTimestampUtc => 8,
        LastSeenTimestampUptime => 8,
        PeakRssi => 1,
        ChannelIndex => 2,
        TagSeenCount => 2,
        RoSpecId => 4,
        InventoryParameterSpecId => 2,
        C1G2Crc => 2,
        C1G2Pc => 2,
        Epc96 => 12,
        SpecIndex => 2,
        ClientRequestOpSpecResult => 2,
        AccessSpecId => 4,
        OpSpecId => 2,
        C1G2SingulationDetails => 4,
        C1G2XpcW1 => 2,
        C1G2XpcW2 => 2,
        _ => -1,
    };
}

/// <summary>Builds the parameter payloads an inventory client sends.</summary>
internal static class LlrpBuilder
{
    public const uint InventoryRoSpecId = 1;

    /// <summary>
    /// A minimal ROSpec that inventories the requested antennas continuously and reports each tag as
    /// it is seen. Start and stop triggers are null, so the client controls the session explicitly
    /// with START_ROSPEC and STOP_ROSPEC rather than leaving the reader to decide.
    /// </summary>
    public static byte[] AddRoSpec(IReadOnlyList<ushort> antennas)
    {
        // ROSpecStartTrigger: type 0 = Null (started by command).
        var startTrigger = Tlv(LlrpParameter.RoSpecStartTrigger, [0]);

        // ROSpecStopTrigger: type 0 = Null, duration 0.
        var stopTrigger = Tlv(LlrpParameter.RoSpecStopTrigger, [0, 0, 0, 0, 0]);

        var boundary = Tlv(LlrpParameter.RoBoundarySpec, Concat(startTrigger, stopTrigger));

        // AISpecStopTrigger: type 0 = Null, duration 0.
        var aiStop = Tlv(LlrpParameter.AiSpecStopTrigger, [0, 0, 0, 0, 0]);

        // InventoryParameterSpec: id 1, protocol 1 (EPCglobal Class-1 Gen-2).
        var inventory = Tlv(LlrpParameter.InventoryParameterSpec, [0, 1, 1]);

        // Antenna 0 means "all antennas"; anything else is an explicit list.
        var antennaList = antennas.Count == 0 ? new ushort[] { 0 } : antennas.ToArray();
        var antennaBytes = new byte[2 + (antennaList.Length * 2)];
        BinaryPrimitives.WriteUInt16BigEndian(antennaBytes.AsSpan(0, 2), (ushort)antennaList.Length);
        for (var i = 0; i < antennaList.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(antennaBytes.AsSpan(2 + (i * 2), 2), antennaList[i]);
        }

        var aiSpec = Tlv(LlrpParameter.AiSpec, Concat(antennaBytes, aiStop, inventory));

        // Every field the ingest pipeline needs: antenna for zoning, RSSI for the signal floor,
        // seen-count for the read-count floor, and both timestamps for the feed.
        var contentSelector = Tlv(
            LlrpParameter.TagReportContentSelector,
            Concat(
                BigEndian((ushort)0b1111_1111_1100_0000),
                Tlv(LlrpParameter.C1G2EpcMemorySelector, [0b0110_0000])));

        // ROReportTrigger 1 = upon N tag reports; N = 1 so tags stream rather than pooling.
        var reportSpec = Tlv(LlrpParameter.RoReportSpec, Concat([1], BigEndian((ushort)1), contentSelector));

        var roSpecBody = Concat(
            BigEndian(InventoryRoSpecId),
            [0],                        // priority 0 = highest
            [0],                        // current state 0 = Disabled; ENABLE_ROSPEC follows
            boundary,
            aiSpec,
            reportSpec);

        return Tlv(LlrpParameter.RoSpec, roSpecBody);
    }

    /// <summary>ROSpec id only. Shared by ENABLE, START, STOP and DELETE.</summary>
    public static byte[] RoSpecId(uint roSpecId) => BigEndian(roSpecId);

    /// <summary>
    /// SET_READER_CONFIG with ResetToFactoryDefaults set, plus a keepalive every
    /// <paramref name="keepaliveMs"/>. The reset matters because a reader left configured by a
    /// previous session can otherwise keep reporting on someone else's ROSpec.
    /// </summary>
    public static byte[] SetReaderConfig(int keepaliveMs)
    {
        const ushort keepaliveSpecType = 220;

        // KeepaliveSpec: trigger type 1 = periodic, then the period in milliseconds.
        var keepalive = Tlv(keepaliveSpecType, Concat([1], BigEndian((uint)keepaliveMs)));

        // First byte: bit 7 = ResetToFactoryDefaults.
        return Concat([0b1000_0000], keepalive);
    }

    private static byte[] Tlv(ushort type, ReadOnlySpan<byte> value)
    {
        var buffer = new byte[4 + value.Length];

        // Six reserved bits then the ten-bit type.
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0, 2), (ushort)(type & 0x03FF));
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), (ushort)buffer.Length);
        value.CopyTo(buffer.AsSpan(4));

        return buffer;
    }

    private static byte[] BigEndian(ushort value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] BigEndian(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var buffer = new byte[total];
        var offset = 0;

        foreach (var part in parts)
        {
            part.CopyTo(buffer, offset);
            offset += part.Length;
        }

        return buffer;
    }
}

/// <summary>
/// Pulls tag reads out of an RO_ACCESS_REPORT.
/// <para>
/// A report contains zero or more TagReportData parameters, each a bag of TV fields plus an optional
/// EPCData TLV for EPCs longer than 96 bits. Fields the reader chose not to send are simply absent,
/// so every one has a defensible default: a missing RSSI is treated as the weakest possible reading
/// rather than as zero, which would be the strongest and would defeat the signal floor entirely.
/// </para>
/// </summary>
internal static class LlrpReportParser
{
    /// <summary>The epoch LLRP timestamps count microseconds from.</summary>
    private static readonly DateTimeOffset LlrpEpoch = DateTimeOffset.UnixEpoch;

    public static IReadOnlyList<TagRead> Parse(ReadOnlySpan<byte> payload, DateTimeOffset fallbackTimestamp)
    {
        var reads = new List<TagRead>();
        var offset = 0;

        while (offset < payload.Length)
        {
            if (!TryReadParameterHeader(payload, offset, out var type, out var valueOffset, out var totalLength))
            {
                break;
            }

            if (type == LlrpParameter.TagReportData)
            {
                var body = payload.Slice(valueOffset, totalLength - (valueOffset - offset));
                var read = ParseTagReportData(body, fallbackTimestamp);
                if (read is not null)
                {
                    reads.Add(read);
                }
            }

            offset += totalLength;
        }

        return reads;
    }

    private static TagRead? ParseTagReportData(ReadOnlySpan<byte> body, DateTimeOffset fallbackTimestamp)
    {
        string? epc = null;
        ushort antenna = 0;
        int rssi = sbyte.MinValue;
        ushort seenCount = 1;
        DateTimeOffset? firstSeen = null;
        DateTimeOffset? lastSeen = null;

        var offset = 0;

        while (offset < body.Length)
        {
            var first = body[offset];

            if ((first & 0x80) != 0)
            {
                // TV parameter: one header byte, then a length fixed by type.
                var tvType = (byte)(first & 0x7F);
                var length = LlrpParameter.TvValueLength(tvType);

                if (length < 0 || offset + 1 + length > body.Length)
                {
                    // An unrecognised TV parameter makes every later offset a guess. Stop here and
                    // keep what was parsed rather than emitting invented EPCs.
                    break;
                }

                var value = body.Slice(offset + 1, length);

                switch (tvType)
                {
                    case LlrpParameter.Epc96:
                        epc = Convert.ToHexString(value);
                        break;
                    case LlrpParameter.AntennaId:
                        antenna = BinaryPrimitives.ReadUInt16BigEndian(value);
                        break;
                    case LlrpParameter.PeakRssi:
                        rssi = (sbyte)value[0];
                        break;
                    case LlrpParameter.TagSeenCount:
                        seenCount = BinaryPrimitives.ReadUInt16BigEndian(value);
                        break;
                    case LlrpParameter.FirstSeenTimestampUtc:
                        firstSeen = FromMicroseconds(BinaryPrimitives.ReadUInt64BigEndian(value));
                        break;
                    case LlrpParameter.LastSeenTimestampUtc:
                        lastSeen = FromMicroseconds(BinaryPrimitives.ReadUInt64BigEndian(value));
                        break;
                    default:
                        break;
                }

                offset += 1 + length;
                continue;
            }

            if (!TryReadParameterHeader(body, offset, out var tlvType, out var valueOffset, out var totalLength))
            {
                break;
            }

            // EPCData carries a bit length then the EPC itself, for tags longer than 96 bits.
            if (tlvType == LlrpParameter.EpcData && totalLength >= 6)
            {
                var bitLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(valueOffset, 2));
                var byteLength = (bitLength + 7) / 8;
                var available = totalLength - (valueOffset - offset) - 2;

                if (byteLength > 0 && byteLength <= available)
                {
                    epc = Convert.ToHexString(body.Slice(valueOffset + 2, byteLength));
                }
            }

            offset += totalLength;
        }

        if (string.IsNullOrEmpty(epc))
        {
            return null;
        }

        var last = lastSeen ?? firstSeen ?? fallbackTimestamp;
        return new TagRead(epc, antenna, rssi, Math.Max(1, (int)seenCount), firstSeen ?? last, last);
    }

    /// <summary>
    /// Reads a TLV header. Returns false when the declared length is impossible, which is how a
    /// truncated or malformed frame stops the loop instead of running off the end of the buffer.
    /// </summary>
    private static bool TryReadParameterHeader(
        ReadOnlySpan<byte> buffer,
        int offset,
        out ushort type,
        out int valueOffset,
        out int totalLength)
    {
        type = 0;
        valueOffset = 0;
        totalLength = 0;

        if (offset + 4 > buffer.Length)
        {
            return false;
        }

        type = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset, 2)) & 0x03FF);
        totalLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset + 2, 2));
        valueOffset = offset + 4;

        return totalLength >= 4 && offset + totalLength <= buffer.Length;
    }

    private static DateTimeOffset FromMicroseconds(ulong microseconds)
        => LlrpEpoch.AddTicks((long)(microseconds * 10));
}

/// <summary>
/// Parses the antenna map an administrator typed, e.g. <c>1=Checkout;2=Checkout;3=Exit</c>.
/// <para>
/// The agent needs it to decide which antennas to inventory at all: an Exit antenna feeding loss
/// prevention should not be spending airtime on a checkout ROSpec.
/// </para>
/// </summary>
internal static class AntennaZoneMap
{
    public static IReadOnlyList<ushort> CheckoutAntennas(string? map)
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return [];
        }

        var antennas = new List<ushort>();

        foreach (var pair in map.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);

            if (parts.Length == 2
                && ushort.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var antenna)
                && parts[1].Equals("Checkout", StringComparison.OrdinalIgnoreCase))
            {
                antennas.Add(antenna);
            }
        }

        return antennas;
    }

    public static string Describe(IReadOnlyList<ushort> antennas)
        => antennas.Count == 0 ? "all" : string.Join(", ", antennas.Select(a => a.ToString(CultureInfo.InvariantCulture)));

    public static string ToDisplay(byte[] bytes) => Encoding.ASCII.GetString(bytes);
}
