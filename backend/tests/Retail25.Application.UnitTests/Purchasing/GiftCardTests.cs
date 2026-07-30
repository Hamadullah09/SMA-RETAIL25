using FluentAssertions;
using Retail25.Application.Receivables;
using Retail25.Application.UnitTests.Masters;
using Xunit;

namespace Retail25.Application.UnitTests.Purchasing;

/// <summary>Gift card issue and balance inquiry (guide p.7, p.106) — Stage 4's standalone piece; redemption at the till is covered in CompleteSaleTests.</summary>
public sealed class GiftCardTests
{
    [Fact]
    public async Task Issuing_a_gift_card_with_no_serial_generates_one()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await harness.GiftCards.Handle(new IssueGiftCardCommand(50m), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.SerialNumber.Should().HaveLength(12);
        result.Value.OriginalValue.Should().Be(50m);
        result.Value.RemainingValue.Should().Be(50m);
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task A_caller_supplied_serial_is_honoured_and_normalised()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await harness.GiftCards.Handle(new IssueGiftCardCommand(25m, "abc-123"), default);

        result.Value.SerialNumber.Should().Be("ABC-123");
    }

    [Fact]
    public async Task Two_cards_cannot_share_the_same_serial_number()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.GiftCards.Handle(new IssueGiftCardCommand(25m, "DUPE1"), default);

        var result = await harness.GiftCards.Handle(new IssueGiftCardCommand(10m, "dupe1"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gift_card.duplicate_serial");
    }

    [Fact]
    public async Task Issuing_a_zero_value_card_is_refused()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await harness.GiftCards.Handle(new IssueGiftCardCommand(0m), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gift_card.value_must_be_positive");
    }

    [Fact]
    public async Task Balance_inquiry_finds_a_card_by_serial_case_insensitively()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        await harness.GiftCards.Handle(new IssueGiftCardCommand(75m, "FIND-ME"), default);

        var result = await harness.GiftCards.Handle(new GiftCardBalanceQuery("find-me"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.RemainingValue.Should().Be(75m);
    }

    [Fact]
    public async Task Balance_inquiry_for_an_unknown_serial_fails()
    {
        using var harness = await MastersTestHarness.CreateAsync();

        var result = await harness.GiftCards.Handle(new GiftCardBalanceQuery("NOPE"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("gift_card.not_found");
    }
}
