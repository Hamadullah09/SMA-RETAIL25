using FluentAssertions;
using Retail25.Devices.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests.Rfid;

/// <summary>
/// What counts as a reader's serial number, and — the part that matters — what does not.
///
/// <para>
/// Everything downstream trusts a serial over an address: the sweep deduplicates on it, and
/// registration reads a familiar serial at a new address as the same reader moving house, assignments
/// and all. That makes a wrong serial far more dangerous than no serial, and the wrong one this
/// protocol hands you is the unwritten identifier field of a reader nobody has programmed.
/// </para>
/// </summary>
public sealed class ReaderIdentifierTests
{
    /// <summary>
    /// The exact reply from a live unit on 14 September 2026: <c>A0 0F 10 68 FF×12 E5</c>. Twelve
    /// bytes of a field that has never been written.
    /// </summary>
    [Fact]
    public void An_identifier_that_was_never_programmed_is_not_a_serial_number()
    {
        var blank = Enumerable.Repeat((byte)0xFF, 12).ToArray();

        ReaderIdentityProbe.FormatIdentifier(blank).Should().BeNull(
            "a shop's readers all come out of the same box at the same time, so accepting this would "
            + "give every one of them the same identity");
    }

    [Fact]
    public void An_all_zero_identifier_is_not_a_serial_number_either()
    {
        ReaderIdentityProbe.FormatIdentifier(new byte[12]).Should().BeNull();
    }

    [Fact]
    public void A_real_identifier_is_reported_as_hex()
    {
        var identifier = new byte[] { 0xA1, 0xB2, 0xC3, 0xD4 };

        ReaderIdentityProbe.FormatIdentifier(identifier).Should().Be("A1B2C3D4");
    }

    /// <summary>
    /// The padding case the trimming exists for: a short identifier in a fixed-width field. It is
    /// still a genuine identifier and must survive.
    /// </summary>
    [Fact]
    public void Zero_padding_is_trimmed_without_losing_the_identifier()
    {
        var padded = new byte[] { 0xA1, 0xB2, 0x00, 0x00, 0x00, 0x00 };

        ReaderIdentityProbe.FormatIdentifier(padded).Should().Be("A1B2");
    }

    /// <summary>
    /// A single 0xFF byte in an otherwise real identifier is data, not blankness. Only a field that
    /// is entirely unwritten is refused.
    /// </summary>
    [Fact]
    public void An_identifier_that_merely_contains_ff_is_still_a_serial_number()
    {
        var identifier = new byte[] { 0xFF, 0x01, 0xFF, 0x02 };

        ReaderIdentityProbe.FormatIdentifier(identifier).Should().Be("FF01FF02");
    }

    [Fact]
    public void A_reader_that_answered_nothing_reports_no_serial()
    {
        ReaderIdentityProbe.FormatIdentifier(null).Should().BeNull();
        ReaderIdentityProbe.FormatIdentifier([]).Should().BeNull();
    }
}
