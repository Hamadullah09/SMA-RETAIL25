using FluentAssertions;
using Retail25.Domain.Configuration;
using Xunit;

namespace Retail25.Domain.UnitTests.Configuration;

/// <summary>
/// The legacy "next number" settings (guide p.76). These exist so a migrated store keeps its own
/// numbering — customer 4,182 has to be followed by 4,183, because staff and paper records refer to
/// those numbers.
/// </summary>
public sealed class NumberSequenceTests
{
    [Fact]
    public void A_number_is_printed_with_the_store_s_own_prefix_and_padding()
    {
        var sequence = NumberSequence.Create(Guid.NewGuid(), SequenceKind.Invoice, 42, "INV-", 6);

        sequence.Format(42).Should().Be("INV-000042");
    }

    [Fact]
    public void A_sequence_without_padding_prints_a_bare_number()
    {
        var sequence = NumberSequence.Create(Guid.NewGuid(), SequenceKind.Customer, 4183);

        sequence.Format(4183).Should().Be("4183");
    }

    [Fact]
    public void Taking_a_number_advances_the_counter_and_records_the_highest_issued()
    {
        var sequence = NumberSequence.Create(Guid.NewGuid(), SequenceKind.Customer, 4182);

        sequence.Take().Should().Be(4182);
        sequence.Take().Should().Be(4183);

        sequence.NextNumber.Should().Be(4184);
        sequence.HighWaterMark.Should().Be(4183);
    }

    [Fact]
    public void A_counter_can_be_repointed_forward()
    {
        var sequence = NumberSequence.Create(Guid.NewGuid(), SequenceKind.Invoice);

        sequence.SetNext(5000).IsSuccess.Should().BeTrue();
        sequence.NextNumber.Should().Be(5000);
    }

    [Fact]
    public void A_counter_cannot_be_moved_back_onto_a_number_already_issued()
    {
        var sequence = NumberSequence.Create(Guid.NewGuid(), SequenceKind.Invoice, 100);
        sequence.Take();

        var result = sequence.SetNext(100);

        // Duplicate invoice numbers are the kind of mistake that surfaces months later at an audit.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sequence.would_go_backwards");
    }

    [Fact]
    public void A_negative_counter_is_refused()
    {
        var sequence = NumberSequence.Create(Guid.NewGuid(), SequenceKind.Invoice);

        sequence.SetNext(-1).Error.Code.Should().Be("sequence.next_invalid");
    }

    [Fact]
    public void Padding_is_clamped_rather_than_trusted()
    {
        var sequence = NumberSequence.Create(Guid.NewGuid(), SequenceKind.Invoice);

        sequence.SetFormat("  X-  ", 99);

        sequence.Prefix.Should().Be("X-");
        sequence.PadWidth.Should().Be(12);
    }

    [Fact]
    public void A_new_store_gets_a_counter_for_every_kind()
    {
        var defaults = NumberSequence.SeedDefaults(Guid.NewGuid());

        defaults.Select(s => s.Kind).Should().BeEquivalentTo(Enum.GetValues<SequenceKind>());
        defaults.Should().OnlyContain(s => s.NextNumber == 1);
        defaults.Single(s => s.Kind == SequenceKind.Invoice).Format(1).Should().Be("INV-000001");
    }
}
