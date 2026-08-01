using FluentAssertions;
using Retail25.Contracts.Terminals;
using Retail25.Domain.Terminals;
using Retail25.TerminalAgent.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests.Rfid;

/// <summary>
/// The R2000-family UHF serial wire format (D2184B and relatives).
/// <para>
/// Frame bytes here are built by hand from the protocol spec and cross-checked against the vendor's
/// own <c>MessageTran</c>/<c>ReaderMethod</c> reference source, for the same reason the LLRP tests do
/// it that way: a wrong offset or a misjudged frame-type boundary would not fail loudly, it would
/// silently produce a plausible-looking wrong EPC that could reach a cart as a real item.
/// </para>
/// </summary>
public sealed class UhfSerialCodecTests
{
    [Fact]
    public void A_checksum_is_the_negated_sum_of_every_other_byte()
    {
        // 0xA0 + 0x03 + 0xFF + 0x70 = 530, mod 256 = 0x12; two's-complement negation = 0xEE.
        var checksum = UhfSerialCodec.Checksum([0xA0, 0x03, 0xFF, 0x70]);

        checksum.Should().Be(0xEE);
    }

    [Fact]
    public void A_checksum_makes_the_whole_frame_sum_to_zero()
    {
        var frame = UhfSerialCodec.Encode(UhfSerialCodec.PublicAddress, 0x74, [0x01]);

        byte sum = 0;
        foreach (var b in frame)
        {
            sum = unchecked((byte)(sum + b));
        }

        sum.Should().Be(0);
    }

    [Fact]
    public void An_encoded_frame_carries_head_length_address_command_and_data()
    {
        var frame = UhfSerialCodec.Encode(0xFF, UhfSerialCommand.SetWorkAntenna, [0x02]);

        // Head(1) Len(1) Address(1) Cmd(1) Data(1) Check(1) = 6 bytes; Len = data.Length + 3 = 4.
        frame.Should().HaveCount(6);
        frame[0].Should().Be(UhfSerialCodec.FrameHead);
        frame[1].Should().Be(4);
        frame[2].Should().Be(0xFF);
        frame[3].Should().Be(UhfSerialCommand.SetWorkAntenna);
        frame[4].Should().Be(0x02);
    }

    [Fact]
    public void A_frame_split_across_two_reads_is_reassembled()
    {
        var frame = UhfSerialCodec.Encode(0xFF, UhfSerialCommand.GetFirmwareVersion, []);
        var reassembler = new UhfSerialCodec.FrameReassembler();

        reassembler.Push(frame.AsSpan(0, 2)).Should().BeEmpty();
        var frames = reassembler.Push(frame.AsSpan(2)).ToList();

        frames.Should().ContainSingle();
        frames[0].Cmd.Should().Be(UhfSerialCommand.GetFirmwareVersion);
        frames[0].Address.Should().Be(0xFF);
    }

    [Fact]
    public void Two_frames_delivered_in_one_read_both_come_back()
    {
        var first = UhfSerialCodec.Encode(0xFF, UhfSerialCommand.SetWorkAntenna, [0x00]);
        var second = UhfSerialCodec.Encode(0xFF, UhfSerialCommand.SetWorkAntenna, [0x01]);
        var combined = first.Concat(second).ToArray();

        var frames = new UhfSerialCodec.FrameReassembler().Push(combined).ToList();

        frames.Should().HaveCount(2);
        frames[0].Data[0].Should().Be(0x00);
        frames[1].Data[0].Should().Be(0x01);
    }

    /// <summary>
    /// Junk before a frame — a partial trailing byte from a previous read, line noise — must not stop
    /// the reassembler from finding the real frame that follows it.
    /// </summary>
    [Fact]
    public void Garbage_before_the_sync_byte_is_skipped()
    {
        var frame = UhfSerialCodec.Encode(0xFF, UhfSerialCommand.GetFirmwareVersion, []);
        var withGarbage = new byte[] { 0x11, 0x22, 0x33 }.Concat(frame).ToArray();

        var frames = new UhfSerialCodec.FrameReassembler().Push(withGarbage).ToList();

        frames.Should().ContainSingle();
        frames[0].Cmd.Should().Be(UhfSerialCommand.GetFirmwareVersion);
    }

    /// <summary>A frame whose trailing byte fails the checksum is dropped rather than trusted.</summary>
    [Fact]
    public void A_frame_with_a_corrupted_checksum_is_dropped()
    {
        var frame = UhfSerialCodec.Encode(0xFF, UhfSerialCommand.GetFirmwareVersion, []);
        frame[^1] ^= 0xFF;

        var frames = new UhfSerialCodec.FrameReassembler().Push(frame).ToList();

        frames.Should().BeEmpty();
    }

    [Fact]
    public void A_one_byte_inventory_response_is_an_error()
        => ClassifyByDataLength(1).Should().Be(InventoryFrameKind.Error);

    [Fact]
    public void A_seven_byte_inventory_response_is_the_round_summary()
        => ClassifyByDataLength(7).Should().Be(InventoryFrameKind.RoundComplete);

    [Fact]
    public void A_longer_inventory_response_is_a_tag()
        => ClassifyByDataLength(17).Should().Be(InventoryFrameKind.Tag);

    private static InventoryFrameKind ClassifyByDataLength(int dataLength)
        => InventoryFrameParser.Classify(new UhfSerialFrame(0xFF, UhfSerialCommand.RealTimeInventory, new byte[dataLength]));

    /// <summary>FreqAnt(1) PC(2) EPC(12, a 96-bit SGTIN) RSSI(1) — a realistic single-tag frame.</summary>
    [Fact]
    public void A_tag_frame_yields_the_epc_antenna_and_signal_strength()
    {
        // FreqAnt: frequency index 5 in the high 6 bits, antenna 2 (0-based) in the low 2 bits.
        var freqAnt = (byte)((5 << 2) | 0x02);
        var pc = new byte[] { 0x30, 0x00 };
        var epc = new byte[] { 0x30, 0x34, 0x25, 0x7B, 0xF4, 0x00, 0xB7, 0x80, 0x00, 0x04, 0xCB, 0x2F };
        var rawRssi = (byte)98; // 98 - 129 = -31 dBm, per the correspondence table.

        var data = new byte[] { freqAnt }.Concat(pc).Concat(epc).Concat([rawRssi]).ToArray();
        var frame = new UhfSerialFrame(0xFF, UhfSerialCommand.RealTimeInventory, data);

        var tag = InventoryFrameParser.ParseTag(frame);

        tag.Epc.Should().Be("3034257BF400B7800004CB2F");
        tag.RawAntenna.Should().Be(2);
        tag.RssiDbm.Should().Be(-31);
    }

    /// <summary>
    /// An unpopulated RSSI byte means "not measured", not "impossibly far away".
    /// <para>
    /// Observed on real hardware: an R2000-family reader in real-time inventory mode leaves this
    /// field empty, and the vendor's own demo shows every tag at −128 dBm while reporting a genuine
    /// −89/−46 range in its summary panel. Decoding that as a number puts it below every sensible
    /// proximity threshold, so the gate would discard 100% of reads — and the symptom at the till is
    /// a reader that connects, reports healthy, and never sees a single tag.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void An_unpopulated_signal_strength_reads_as_unknown_rather_than_impossibly_weak(byte rawRssi)
    {
        var freqAnt = (byte)((5 << 2) | 0x01);
        var pc = new byte[] { 0x30, 0x00 };
        var epc = new byte[] { 0xE2, 0x80, 0x69, 0x15, 0x00, 0x00, 0x60, 0x0B, 0x40, 0xA7, 0x1D, 0x95 };

        var data = new byte[] { freqAnt }.Concat(pc).Concat(epc).Concat([rawRssi]).ToArray();
        var frame = new UhfSerialFrame(0xFF, UhfSerialCommand.RealTimeInventory, data);

        var tag = InventoryFrameParser.ParseTag(frame);

        // One of the EPCs the reader on the bench actually returned.
        tag.Epc.Should().Be("E28069150000600B40A71D95");
        tag.RssiDbm.Should().Be(TagRead.UnknownRssi);
    }

    /// <summary>
    /// The corollary: a tag whose signal was never measured must still reach the cart.
    /// </summary>
    [Fact]
    public void A_reader_that_reports_no_signal_strength_is_not_filtered_out()
    {
        var profile = ReaderProfile.CreateDefault(Guid.NewGuid());
        profile.RssiThresholdDbm = -70;
        profile.MinimumReadCount = 2;

        profile.Accepts(antenna: 1, rssiDbm: TagRead.UnknownRssi, readCount: 2)
            .Should().BeTrue("a reader that declines to measure must not have every read rejected");

        // The other two conditions still do their work.
        profile.Accepts(antenna: 1, rssiDbm: TagRead.UnknownRssi, readCount: 1)
            .Should().BeFalse("one stray read is still not enough");

        profile.Accepts(antenna: 1, rssiDbm: -90, readCount: 2)
            .Should().BeFalse("a measured, genuinely weak read is still refused");
    }

    [Fact]
    public void A_round_complete_frame_never_gets_mistaken_for_a_tag()
    {
        // AntId(1) ReadRate(2) TotalRead(4) = 7 bytes.
        var frame = new UhfSerialFrame(0xFF, UhfSerialCommand.RealTimeInventory, new byte[7]);

        InventoryFrameParser.Classify(frame).Should().Be(InventoryFrameKind.RoundComplete);
    }

    [Fact]
    public void An_error_frame_exposes_its_error_code()
    {
        var frame = new UhfSerialFrame(0xFF, UhfSerialCommand.RealTimeInventory, [0x22]);

        InventoryFrameParser.Classify(frame).Should().Be(InventoryFrameKind.Error);
        InventoryFrameParser.ReadErrorCode(frame).Should().Be(0x22);
    }
}
