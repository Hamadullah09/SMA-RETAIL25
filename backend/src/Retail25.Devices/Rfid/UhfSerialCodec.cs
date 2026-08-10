using System.Buffers.Binary;
using Retail25.Contracts.Terminals;

namespace Retail25.Devices.Rfid;

/// <summary>
/// Command codes for the R2000-family "UHF RFID Reader Serial Interface Protocol" (v3.1) â€” the wire
/// protocol a D2184B and its relatives (and its Windows demo, "UHFDemo") speak, over RS-232, RS-485
/// or TCP alike. Values verified against the vendor's own reference implementation
/// (<c>Reader/ReaderMethod.cs</c>) rather than transcribed from the PDF alone.
/// </summary>
internal static class UhfSerialCommand
{
    public const byte Reset = 0x70;
    public const byte SetUartBaudrate = 0x71;
    public const byte GetFirmwareVersion = 0x72;
    public const byte SetReaderAddress = 0x73;
    public const byte SetWorkAntenna = 0x74;
    public const byte GetWorkAntenna = 0x75;
    public const byte SetOutputPower = 0x76;
    public const byte GetOutputPower = 0x77;
    public const byte SetFrequencyRegion = 0x78;
    public const byte GetFrequencyRegion = 0x79;
    public const byte SetBeeperMode = 0x7A;
    public const byte GetReaderTemperature = 0x7B;
    public const byte GetRfPortReturnLoss = 0x7E;

    public const byte ReadGpioValue = 0x60;
    public const byte WriteGpioValue = 0x61;
    public const byte SetAntConnectionDetector = 0x62;
    public const byte GetAntConnectionDetector = 0x63;
    public const byte SetReaderIdentifier = 0x67;
    public const byte GetReaderIdentifier = 0x68;
    public const byte SetRfLinkProfile = 0x69;
    public const byte GetRfLinkProfile = 0x6A;

    /// <summary>Streams each tag as it is seen, then a round-summary frame (Â§2.2.8, <c>0x89</c>).</summary>
    public const byte RealTimeInventory = 0x89;

    /// <summary>Impinj Monza fast-TID read. Non-standard, and slower on tags that do not support it.</summary>
    public const byte SetImpinjFastTid = 0x8C;
    public const byte GetImpinjFastTid = 0x8E;

    /// <summary>
    /// Every opcode above was confirmed against a live D2184B (firmware 8.2) rather than transcribed:
    /// each query was sent and its reply decoded. <c>GetRfLinkProfile</c> answering <c>0xD1</c> â€” the
    /// profile the vendor's own demo labels "recommended and default" â€” is what pins the numbering
    /// down, because a wrong opcode would have answered with a different shape or not at all.
    /// </summary>
    public const string VerifiedAgainst = "D2184B firmware 8.2, 2026-08-03";
}

/// <summary>
/// A decoded frame: <c>Head(0xA0) Len Address Cmd Data... Check</c>, in both directions.
/// <see cref="Data"/> excludes the four header/trailer bytes.
/// </summary>
internal readonly record struct UhfSerialFrame(byte Address, byte Cmd, byte[] Data);

/// <summary>
/// Wire format for the UHF serial protocol.
/// <para>
/// Frame: <c>Head(1)=0xA0 Len(1) Address(1) Cmd(1) Data(N) Check(1)</c>. <c>Len</c> excludes itself
/// and <c>Head</c> â€” it counts everything from <c>Address</c> to <c>Check</c> inclusive, so
/// <c>Len = N + 3</c> and the full frame is <c>Len + 2</c> bytes. The checksum is the two's-complement
/// negation of the sum of every byte except itself, exactly as the vendor's own
/// <c>MessageTran.CheckSum</c> computes it.
/// </para>
/// <para>
/// Address <c>0xFF</c> is the reader's public address â€” every unit answers to it regardless of what
/// address it has actually been configured with (Â§1.2.1) â€” so a single-reader-per-till deployment
/// never needs to know or set the physical unit's address.
/// </para>
/// </summary>
internal static class UhfSerialCodec
{
    public const byte FrameHead = 0xA0;
    public const byte PublicAddress = 0xFF;

    /// <summary>Builds a command frame ready to write to the wire.</summary>
    public static byte[] Encode(byte address, byte cmd, ReadOnlySpan<byte> data)
    {
        var frame = new byte[data.Length + 5];
        frame[0] = FrameHead;
        frame[1] = (byte)(data.Length + 3);
        frame[2] = address;
        frame[3] = cmd;
        data.CopyTo(frame.AsSpan(4));
        frame[^1] = Checksum(frame.AsSpan(0, frame.Length - 1));

        return frame;
    }

    /// <summary>Sum of every byte in <paramref name="bytes"/>, negated (two's complement), masked to a byte.</summary>
    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        byte sum = 0;
        foreach (var b in bytes)
        {
            sum = unchecked((byte)(sum + b));
        }

        return unchecked((byte)(-sum));
    }

    /// <summary>
    /// Pulls complete frames out of a byte stream that may split or coalesce frames arbitrarily,
    /// mirroring the vendor's own <c>ReaderMethod.RunReceiveDataCallback</c> resync logic: scan for
    /// <c>0xA0</c>, and once found, wait for <c>Len + 2</c> bytes total before decoding.
    /// </summary>
    public sealed class FrameReassembler
    {
        private byte[] _buffer = [];

        /// <summary>Appends freshly read bytes and yields every complete, checksum-valid frame now available.</summary>
        public IEnumerable<UhfSerialFrame> Push(ReadOnlySpan<byte> received)
        {
            var combined = new byte[_buffer.Length + received.Length];
            _buffer.CopyTo(combined.AsSpan());
            received.CopyTo(combined.AsSpan(_buffer.Length));

            var offset = 0;
            var frames = new List<UhfSerialFrame>();

            while (offset < combined.Length)
            {
                if (combined[offset] != FrameHead)
                {
                    offset++;
                    continue;
                }

                if (offset + 1 >= combined.Length)
                {
                    // Have the head but not yet the length byte â€” wait for more.
                    break;
                }

                var len = combined[offset + 1];
                var frameLength = len + 2;

                if (offset + frameLength > combined.Length)
                {
                    // The rest of this frame has not arrived yet.
                    break;
                }

                var frameBytes = combined.AsSpan(offset, frameLength);
                if (TryDecode(frameBytes, out var frame))
                {
                    frames.Add(frame);
                }

                offset += frameLength;
            }

            _buffer = combined[offset..];
            return frames;
        }

        /// <summary>Decodes one already-delimited frame, verifying its checksum.</summary>
        private static bool TryDecode(ReadOnlySpan<byte> frame, out UhfSerialFrame decoded)
        {
            decoded = default;

            if (frame.Length < 5 || frame[0] != FrameHead)
            {
                return false;
            }

            var expected = Checksum(frame[..^1]);
            if (expected != frame[^1])
            {
                return false;
            }

            var address = frame[2];
            var cmd = frame[3];
            var data = frame[4..^1].ToArray();

            decoded = new UhfSerialFrame(address, cmd, data);
            return true;
        }
    }
}

/// <summary>What a <see cref="UhfSerialCommand.RealTimeInventory"/> response frame turned out to be.</summary>
internal enum InventoryFrameKind
{
    /// <summary>One tag, seen once, right now.</summary>
    Tag,

    /// <summary>The round finished â€” antenna id, read rate and total reads (Â§2.2.8).</summary>
    RoundComplete,

    /// <summary>The reader could not complete the round (e.g. antenna disconnected).</summary>
    Error,
}

/// <summary>A single tag observed in one <see cref="UhfSerialCommand.RealTimeInventory"/> data frame.</summary>
internal readonly record struct InventoryTagFrame(byte RawAntenna, string Epc, int RssiDbm);

/// <summary>
/// Interprets a <see cref="UhfSerialCommand.RealTimeInventory"/> (<c>0x89</c>) response frame.
/// <para>
/// The protocol has no explicit frame-type discriminator; a client tells a tag frame apart from a
/// round-summary or an error frame purely by <c>Data</c> length (Â§2.2.8): a tag frame is
/// <c>FreqAnt(1) PC(2) EPC(N) RSSI(1)</c>, a round-summary is exactly 7 bytes
/// (<c>AntId(1) ReadRate(2) TotalRead(4)</c>), and an error is exactly 1 byte. A real EPC is always at
/// least 12 bytes (96-bit minimum), so a tag frame's data length is never mistakable for either.
/// </para>
/// </summary>
internal static class InventoryFrameParser
{
    private const int SummaryDataLength = 7;
    private const int ErrorDataLength = 1;

    /// <summary>
    /// At or below this, the RSSI byte carries no measurement (see <see cref="TagRead.UnknownRssi"/>).
    /// </summary>
    private const byte UnmeasuredRssiByte = 1;

    /// <summary>Fixed overhead of a tag data frame: FreqAnt(1) + PC(2) + RSSI(1).</summary>
    private const int TagFrameOverhead = 4;

    public static InventoryFrameKind Classify(UhfSerialFrame frame) => frame.Data.Length switch
    {
        ErrorDataLength => InventoryFrameKind.Error,
        SummaryDataLength => InventoryFrameKind.RoundComplete,
        _ => InventoryFrameKind.Tag,
    };

    public static byte ReadErrorCode(UhfSerialFrame frame) => frame.Data[0];

    /// <summary>
    /// Parses a tag data frame. Antenna is the low 2 bits of FreqAnt (Â§2.2.8 note); RSSI converts to
    /// dBm as <c>raw - 129</c> (derived from the correspondence table â€” e.g. raw 98 â‡’ -31 dBm, raw 31
    /// â‡’ -98 dBm).
    /// </summary>
    public static InventoryTagFrame ParseTag(UhfSerialFrame frame)
    {
        var data = frame.Data;
        var freqAnt = data[0];
        var epcLength = data.Length - TagFrameOverhead;
        var epc = data.AsSpan(3, epcLength);
        var rawRssi = data[^1];

        return new InventoryTagFrame(
            RawAntenna: (byte)(freqAnt & 0x03),
            Epc: Convert.ToHexString(epc),

            // A raw byte of 0 or 1 decodes to âˆ’129/âˆ’128 dBm, which is below the noise floor of any
            // real antenna â€” it means the reader did not measure, not that the tag is impossibly far
            // away. Real-time inventory mode leaves this field empty on R2000-family readers, so
            // mapping it to a number would put the proximity gate on a hair trigger that rejects
            // everything.
            RssiDbm: rawRssi <= UnmeasuredRssiByte ? TagRead.UnknownRssi : rawRssi - 129);
    }
}
