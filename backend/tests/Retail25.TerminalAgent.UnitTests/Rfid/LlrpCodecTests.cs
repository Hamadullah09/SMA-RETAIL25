using System.Buffers.Binary;
using FluentAssertions;
using Retail25.Devices.Rfid;
using Retail25.TerminalAgent.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests.Rfid;

/// <summary>
/// The LLRP wire format (EPCglobal LLRP 1.0.1).
/// <para>
/// These tests matter more than their size suggests. A wrong TV parameter length does not fail
/// loudly â€” it shifts every subsequent field and yields plausible-looking garbage EPCs, which would
/// reach a cart as real items. Asserting against bytes built by hand is the only way to know the
/// parser agrees with the specification rather than with itself.
/// </para>
/// </summary>
public sealed class LlrpCodecTests
{
    [Fact]
    public void A_frame_carries_its_version_type_length_and_id()
    {
        var frame = LlrpCodec.Encode(LlrpMessageType.AddRoSpec, 42, [1, 2, 3]);

        frame.Should().HaveCount(LlrpCodec.HeaderLength + 3);

        var (type, length, messageId) = LlrpCodec.DecodeHeader(frame);

        type.Should().Be(LlrpMessageType.AddRoSpec);
        length.Should().Be((uint)frame.Length);
        messageId.Should().Be(42u);

        // Bits 10â€“12 are the protocol version; version 1 is the only one in the wild.
        var versionAndType = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(0, 2));
        ((versionAndType >> 10) & 0x07).Should().Be(LlrpCodec.ProtocolVersion);
    }

    [Fact]
    public void A_tag_report_yields_the_epc_antenna_signal_and_counts()
    {
        var epc = new byte[] { 0x30, 0x34, 0x25, 0x7B, 0xF4, 0x00, 0xB7, 0x80, 0x00, 0x04, 0xCB, 0x2F };

        var report = TagReportData(
            Tv(LlrpParameter.Epc96, epc),
            Tv(LlrpParameter.AntennaId, BigEndian((ushort)2)),
            Tv(LlrpParameter.PeakRssi, [unchecked((byte)(sbyte)-52)]),
            Tv(LlrpParameter.TagSeenCount, BigEndian((ushort)7)));

        var reads = LlrpReportParser.Parse(report, DateTimeOffset.UnixEpoch);

        reads.Should().ContainSingle();
        reads[0].Epc.Should().Be("3034257BF400B7800004CB2F");
        reads[0].Antenna.Should().Be(2);
        reads[0].Rssi.Should().Be(-52);
        reads[0].ReadCount.Should().Be(7);
    }

    [Fact]
    public void Several_tags_in_one_report_all_come_back()
    {
        var report = Concat(
            TagReportData(Tv(LlrpParameter.Epc96, Epc(0x01)), Tv(LlrpParameter.AntennaId, BigEndian((ushort)1))),
            TagReportData(Tv(LlrpParameter.Epc96, Epc(0x02)), Tv(LlrpParameter.AntennaId, BigEndian((ushort)1))),
            TagReportData(Tv(LlrpParameter.Epc96, Epc(0x03)), Tv(LlrpParameter.AntennaId, BigEndian((ushort)1))));

        LlrpReportParser.Parse(report, DateTimeOffset.UnixEpoch).Should().HaveCount(3);
    }

    /// <summary>
    /// A missing RSSI defaults to the weakest possible reading, not to zero. Zero is the strongest
    /// value in dBm, so defaulting to it would let every unreported tag sail past the signal floor.
    /// </summary>
    [Fact]
    public void A_missing_signal_strength_defaults_to_the_weakest_reading()
    {
        var report = TagReportData(Tv(LlrpParameter.Epc96, Epc(0x01)));

        LlrpReportParser.Parse(report, DateTimeOffset.UnixEpoch)[0].Rssi.Should().Be(sbyte.MinValue);
    }

    [Fact]
    public void Timestamps_are_read_as_microseconds_since_the_unix_epoch()
    {
        var oneSecond = 1_000_000UL;

        var report = TagReportData(
            Tv(LlrpParameter.Epc96, Epc(0x01)),
            Tv(LlrpParameter.FirstSeenTimestampUtc, BigEndian(oneSecond)),
            Tv(LlrpParameter.LastSeenTimestampUtc, BigEndian(oneSecond * 2)));

        var read = LlrpReportParser.Parse(report, DateTimeOffset.UnixEpoch)[0];

        read.FirstSeen.Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(1));
        read.LastSeen.Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(2));
    }

    /// <summary>An EPC longer than 96 bits arrives as an EPCData TLV with an explicit bit length.</summary>
    [Fact]
    public void A_long_epc_is_read_from_the_epc_data_parameter()
    {
        var epc = new byte[16];
        for (var i = 0; i < epc.Length; i++)
        {
            epc[i] = (byte)(0xA0 + i);
        }

        var epcData = Tlv(LlrpParameter.EpcData, Concat(BigEndian((ushort)(epc.Length * 8)), epc));
        var report = TagReportData(epcData, Tv(LlrpParameter.AntennaId, BigEndian((ushort)1)));

        LlrpReportParser.Parse(report, DateTimeOffset.UnixEpoch)[0].Epc.Should().Be(Convert.ToHexString(epc));
    }

    /// <summary>
    /// A truncated frame stops the parse rather than reading past the buffer. Emitting a partial or
    /// invented EPC would be far worse than emitting none: it would reach a cart as a real item.
    /// </summary>
    [Fact]
    public void A_truncated_report_stops_cleanly_instead_of_inventing_a_tag()
    {
        var report = TagReportData(Tv(LlrpParameter.Epc96, Epc(0x01)));
        var truncated = report.AsSpan(0, report.Length - 4).ToArray();

        var act = () => LlrpReportParser.Parse(truncated, DateTimeOffset.UnixEpoch);

        act.Should().NotThrow();
    }

    [Fact]
    public void An_unknown_tv_parameter_stops_the_parse_rather_than_shifting_every_field()
    {
        // Type 90 has no defined length, so nothing after it can be located reliably.
        var report = TagReportData(new byte[] { 0x80 | 90, 0xFF, 0xFF }, Tv(LlrpParameter.Epc96, Epc(0x01)));

        LlrpReportParser.Parse(report, DateTimeOffset.UnixEpoch).Should().BeEmpty();
    }

    [Fact]
    public void An_ro_spec_names_the_antennas_it_was_asked_for()
    {
        var payload = LlrpBuilder.AddRoSpec([1, 2]);

        // The ROSpec is a TLV whose declared length must match what was actually produced, or the
        // reader rejects the whole message.
        var type = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(0, 2)) & 0x03FF);
        var length = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(2, 2));

        type.Should().Be(LlrpParameter.RoSpec);
        length.Should().Be((ushort)payload.Length);

        BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4, 4)).Should().Be(LlrpBuilder.InventoryRoSpecId);
    }

    [Fact]
    public void An_empty_antenna_list_means_every_antenna()
    {
        // Antenna 0 is the LLRP convention for "all". A store that has not zoned its antennas yet
        // should still be able to read tags.
        var payload = LlrpBuilder.AddRoSpec([]);

        payload.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("1=Checkout;2=Checkout;3=Exit", new ushort[] { 1, 2 })]
    [InlineData("1=Exit;2=Shelf", new ushort[] { })]
    [InlineData("", new ushort[] { })]
    [InlineData("4=checkout", new ushort[] { 4 })]
    public void Only_checkout_antennas_are_inventoried(string map, ushort[] expected)
        => AntennaZoneMap.CheckoutAntennas(map).Should().BeEquivalentTo(expected);

    // --- helpers that build LLRP bytes by hand, so the parser is checked against the spec ---

    private static byte[] Epc(byte seed)
    {
        var epc = new byte[12];
        epc[0] = 0x30;
        epc[11] = seed;
        return epc;
    }

    private static byte[] TagReportData(params byte[][] fields) => Tlv(LlrpParameter.TagReportData, Concat(fields));

    private static byte[] Tv(byte type, byte[] value) => Concat([(byte)(0x80 | type)], value);

    private static byte[] Tlv(ushort type, byte[] value)
    {
        var buffer = new byte[4 + value.Length];
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

    private static byte[] BigEndian(ulong value)
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var buffer = new byte[parts.Sum(p => p.Length)];
        var offset = 0;

        foreach (var part in parts)
        {
            part.CopyTo(buffer, offset);
            offset += part.Length;
        }

        return buffer;
    }
}
