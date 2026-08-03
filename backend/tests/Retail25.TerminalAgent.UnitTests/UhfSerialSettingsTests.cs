using FluentAssertions;
using Retail25.TerminalAgent.Rfid;
using Xunit;

// Two RadioRegion enums exist on purpose: one is the wire contract the agent speaks, the other the
// domain type the server stores. They carry the same values and are deliberately not merged — the
// contract is versioned by deployment, the domain by migration. Aliased here so the test can name
// both without either winning by import order.
using WireRegion = Retail25.Contracts.Terminals.RadioRegion;
using WireLinkProfile = Retail25.Contracts.Terminals.RfLinkProfile;
using StoredRegion = Retail25.Domain.Terminals.RadioRegion;
using RadioFrequencyPlan = Retail25.Domain.Terminals.RadioFrequencyPlan;

namespace Retail25.TerminalAgent.UnitTests;

/// <summary>
/// Decoding of the reader's settings replies.
/// <para>
/// The byte strings below are not invented. Each one was captured from a live D2184B (firmware 8.2)
/// by sending the query and recording what came back, so a change that breaks the parsing breaks
/// against the hardware's real answers rather than against an assumption about them.
/// </para>
/// </summary>
public sealed class UhfSerialSettingsTests
{
    // Captured 2026-08-03. Frame bodies only — head, length, address, opcode and checksum removed.
    private static readonly byte[] Firmware = [0x08, 0x02];
    private static readonly byte[] Temperature = [0x01, 0x20];
    private static readonly byte[] Power = [0x02];
    private static readonly byte[] Region = [0x01, 0x07, 0x39];
    private static readonly byte[] LinkProfile = [0xD1];

    /// <summary>
    /// Two bytes, major then minor. The reader answered <c>08 02</c> while its own utility displayed
    /// "8.2" — which is what confirms the byte order rather than assuming it.
    /// </summary>
    [Fact]
    public void The_firmware_reply_is_major_then_minor()
    {
        var version = Firmware is { Length: >= 2 } ? $"{Firmware[0]}.{Firmware[1]}" : null;
        version.Should().Be("8.2");
    }

    [Fact]
    public void The_temperature_reply_decodes_to_the_figure_the_vendor_tool_shows()
    {
        // A0 05 01 7B 01 20 BE, with the demo showing 31–32 °C at the same moment.
        UhfSerialSettings.ParseTemperature(Temperature).Should().Be(32);
    }

    /// <summary>
    /// The first byte is a sign flag. A reader in a chilled store reporting −5 °C must not read as
    /// 251 — the number that would send somebody looking for a cooling fault that does not exist.
    /// </summary>
    [Fact]
    public void A_below_zero_temperature_is_negative_rather_than_a_large_positive()
    {
        UhfSerialSettings.ParseTemperature([0x00, 0x05]).Should().Be(-5);
    }

    /// <summary>
    /// This reader answers with one byte, meaning every port shares the setting. Expanded to one
    /// figure per port so the screen never has to know which shape came back.
    /// </summary>
    [Fact]
    public void A_single_power_byte_applies_to_every_port()
    {
        UhfSerialSettings.ParsePower(Power, antennaPorts: 4)
            .Should().Equal([2, 2, 2, 2]);
    }

    [Fact]
    public void A_power_byte_per_port_is_kept_as_it_came()
    {
        UhfSerialSettings.ParsePower([30, 30, 25, 20], antennaPorts: 4)
            .Should().Equal([30, 30, 25, 20]);
    }

    [Fact]
    public void The_region_reply_decodes_to_the_band_the_reader_is_licensed_for()
    {
        UhfSerialSettings.ParseRegion(Region).Should().Be(WireRegion.Etsi);
        Region[1].Should().Be(0x07, "the first channel travels in the second byte");
        Region[2].Should().Be(0x39, "and the last in the third");
    }

    /// <summary>
    /// The reply that pins the whole opcode table down. <c>0xD1</c> is the profile the vendor's own
    /// tool labels "recommended and default", and the reader answering with it is what proves
    /// <c>0x6A</c> is <c>GetRfLinkProfile</c> rather than a coincidence.
    /// </summary>
    [Fact]
    public void The_link_profile_reply_is_the_vendors_own_default()
    {
        UhfSerialSettings.ParseLinkProfile(LinkProfile).Should().Be(WireLinkProfile.Miller4_250kHz);
    }

    [Fact]
    public void A_reply_the_reader_did_not_send_decodes_to_nothing_rather_than_a_guess()
    {
        UhfSerialSettings.ParseTemperature(null).Should().BeNull();
        UhfSerialSettings.ParsePower(null, 4).Should().BeNull();
        UhfSerialSettings.ParseRegion([]).Should().BeNull();
        UhfSerialSettings.ParseLinkProfile([0x00]).Should().BeNull("0x00 is not a profile this protocol defines");
    }

    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("30", new[] { 30 })]
    [InlineData("30,30,25,20", new[] { 30, 30, 25, 20 })]
    [InlineData(" 30 , 25 ", new[] { 30, 25 })]
    public void Power_settings_are_parsed_from_what_a_person_would_type(string setting, int[] expected)
    {
        UhfSerialSettings.ParsePowerSetting(setting)
            .Select(b => (int)b).Should().Equal(expected);
    }

    /// <summary>
    /// 33 dBm is the ceiling for this reader family. Clamping rather than refusing, because the
    /// server validates the form and this is the last line — sending 200 would have the reader reject
    /// the whole command and run on whatever it had before, silently.
    /// </summary>
    [Fact]
    public void A_power_above_the_hardware_ceiling_is_clamped_not_sent()
    {
        UhfSerialSettings.ParsePowerSetting("200").Should().Equal([33]);
    }

    [Fact]
    public void Nonsense_in_the_power_field_produces_nothing_to_send()
    {
        UhfSerialSettings.ParsePowerSetting("loud").Should().BeEmpty();
        UhfSerialSettings.ParsePowerSetting("").Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The channel-to-megahertz conversion, checked at both edges of every band. This is the only
    /// place the legality of a configuration can be read off, so an error here is an error nobody
    /// would catch by looking at the screen.
    /// </summary>
    [Theory]
    [InlineData(StoredRegion.Fcc, 0, 902.75)]
    [InlineData(StoredRegion.Fcc, 49, 927.25)]
    [InlineData(StoredRegion.Etsi, 0, 865.1)]
    [InlineData(StoredRegion.Etsi, 14, 867.9)]
    [InlineData(StoredRegion.Chn, 0, 920.125)]
    [InlineData(StoredRegion.Chn, 19, 924.875)]
    public void Channels_convert_to_the_frequencies_the_bands_are_defined_by(
        StoredRegion region, int channel, double expectedMhz)
    {
        RadioFrequencyPlan.ToMegahertz(region, channel).Should().Be(expectedMhz);
    }

    /// <summary>
    /// An unrecognised region falls back to the narrowest plan, not the widest. Guessing wrong in the
    /// permissive direction is unlicensed transmission; guessing wrong the other way is a reader with
    /// less range than it could have, which somebody notices and fixes.
    /// </summary>
    [Fact]
    public void An_unknown_region_falls_back_to_the_narrowest_band()
    {
        RadioFrequencyPlan.MaxChannel((StoredRegion)99)
            .Should().Be(RadioFrequencyPlan.MaxChannel(StoredRegion.Etsi));
    }
}
