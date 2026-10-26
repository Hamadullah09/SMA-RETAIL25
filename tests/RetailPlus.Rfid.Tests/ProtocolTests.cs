using RetailPlus.Core.Rules;
using RetailPlus.Rfid.D2184;

namespace RetailPlus.Rfid.Tests;

public class FrameTests
{
    [Fact]
    public void Reset_frame_matches_the_protocol()
    {
        // A0 03 FF 70, checksum = two's complement of (A0 + 03 + FF + 70) = EE.
        Assert.Equal(new byte[] { 0xA0, 0x03, 0xFF, 0x70, 0xEE }, ReaderFrame.Encode(0xFF, 0x70));
    }

    [Fact]
    public void Checksum_makes_the_frame_sum_to_zero()
    {
        var frame = ReaderFrame.Encode(0x01, D2184Command.SetOutputPower, [30, 30, 25, 20]);
        byte sum = 0;
        foreach (var b in frame) sum += b;
        Assert.Equal(0, sum);
        Assert.Equal(0x07, frame[1]);
    }

    [Fact]
    public void Decoder_joins_frames_split_across_reads()
    {
        var a = ReaderFrame.Encode(0x01, 0x72, [3, 1]);
        var b = ReaderFrame.Encode(0x01, 0x7B, [1, 37]);
        var stream = a.Concat(b).ToArray();
        var decoder = new FrameDecoder();
        var first = decoder.Feed(stream.AsSpan(0, 3));
        var rest = decoder.Feed(stream.AsSpan(3));
        Assert.Empty(first);
        Assert.Equal(2, rest.Count);
        Assert.Equal(0x72, rest[0].Command);
        Assert.Equal(new byte[] { 1, 37 }, rest[1].Data);
    }

    [Fact]
    public void Decoder_skips_noise_and_corrupted_frames()
    {
        var good = ReaderFrame.Encode(0x01, 0x72, [3, 1]);
        var bad = ReaderFrame.Encode(0x01, 0x72, [9, 9]);
        bad[^1] ^= 0xFF;
        var decoder = new FrameDecoder();
        var frames = decoder.Feed([0x00, 0x13, .. bad, 0x55, .. good]);
        var frame = Assert.Single(frames);
        Assert.Equal(new byte[] { 3, 1 }, frame.Data);
        Assert.True(decoder.DroppedBytes > 0);
    }

    [Fact]
    public void Tag_frame_gives_antenna_epc_and_rssi()
    {
        // FreqAnt: frequency 33 in the high 6 bits, antenna 2 (=1) in the low 2; PC 0x3000 = 6 words of EPC.
        byte freqAnt = (33 << 2) | 1;
        var epc = Convert.FromHexString("E2000017221101441890A1B2");
        byte[] data = [freqAnt, 0x30, 0x00, .. epc, 0x50];
        var tag = D2184Device.TryParseTag(data, DateTime.Now);
        Assert.NotNull(tag);
        Assert.Equal(2, tag.Antenna);
        Assert.Equal("E2000017221101441890A1B2", tag.Epc);
        Assert.Equal(33, tag.FrequencyIndex);
        Assert.Equal(-49, tag.RssiDbm);
    }

    [Theory]
    [InlineData(new byte[] { 0x10 })]
    [InlineData(new byte[] { 0, 0, 1, 0, 0, 0, 5 })]
    [InlineData(new byte[] { 1, 0x22 })]
    public void Completion_error_and_missing_antenna_frames_are_not_tags(byte[] data) => Assert.Null(D2184Device.TryParseTag(data, DateTime.Now));

    [Theory]
    [InlineData(0, 865.00)]
    [InlineData(6, 868.00)]
    [InlineData(7, 902.00)]
    [InlineData(33, 915.00)]
    [InlineData(59, 928.00)]
    public void Frequency_table(int index, double mhz) => Assert.Equal((decimal)mhz, FrequencyTable.Mhz(index));

    [Fact]
    public void Rssi_table() => Assert.Equal(-31, Rssi.ToDbm(98));

    [Fact]
    public void Error_codes_are_described() => Assert.Contains("antenna not connected", D2184Error.Describe(0x22), StringComparison.Ordinal);
}

public class EpcTests
{
    [Fact]
    public void Normalize_strips_separators_and_rejects_non_hex()
    {
        Assert.Equal("E200AB", Epc.Normalize("e2 00-ab"));
        Assert.Null(Epc.Normalize("XYZ1"));
        Assert.Null(Epc.Normalize("ABC"));
    }

    [Fact]
    public void Sgtin96_decodes_to_the_gtin()
    {
        // GS1 Tag Data Standard example: urn:epc:tag:sgtin-96:3.0614141.812345.6789
        Assert.True(Epc.TryDecodeSgtin96("3074257BF7194E4000001A85", out var gtin, out var serial));
        Assert.Equal("80614141123458", gtin);
        Assert.Equal(6789, serial);
    }

    [Fact]
    public void Barcode_forms_drop_leading_zeros()
    {
        Assert.Equal(["00012345678905", "0012345678905", "012345678905"], Epc.BarcodeForms("00012345678905"));
    }

    [Fact]
    public void Ascii_tags_hold_the_item_code()
    {
        Assert.True(Epc.TryAscii("534B552D3132330000000000", out var text));
        Assert.Equal("SKU-123", text);
        Assert.False(Epc.TryAscii("E2000017221101441890A1B2", out _));
    }

    [Fact]
    public void Check_digit() => Assert.Equal('5', Epc.CheckDigit("01234567890"));
}
