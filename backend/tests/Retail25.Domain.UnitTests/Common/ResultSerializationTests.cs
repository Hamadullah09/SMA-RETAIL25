using System.Text.Json;
using FluentAssertions;
using Retail25.Domain.Common;
using Xunit;

namespace Retail25.Domain.UnitTests.Common;

/// <summary>
/// A result has to survive being written down and read back.
/// <para>
/// The idempotency store keeps a command's response so that pressing Pay twice returns the first
/// receipt instead of taking the money again. Storing worked; reading threw
/// <c>NotSupportedException</c> every time, because a result has no public constructor and
/// <c>System.Text.Json</c> will not construct what it cannot see a constructor for. Every command
/// returns a result, so no replay had ever succeeded — and the cashier best placed to notice was
/// shown a 500 on a sale that had already gone through.
/// </para>
/// <para>
/// These use <b>default</b> serializer options on purpose. The first fix passed custom options at
/// each store's call site and missed one of the three; the converter now travels with the type, so
/// what is being asserted here is that a store cannot get this wrong by forgetting anything.
/// </para>
/// </summary>
public sealed class ResultSerializationTests
{
    private sealed record Receipt(long TransactionId, decimal GrandTotal, string? Reference);

    [Fact]
    public void A_successful_result_survives_a_round_trip()
    {
        var original = Result.Success(new Receipt(4182, 19152.00m, "CHQ-1"));

        var restored = JsonSerializer.Deserialize<Result<Receipt>>(JsonSerializer.Serialize(original));

        restored.Should().NotBeNull();
        restored!.IsSuccess.Should().BeTrue();
        restored.Value.Should().Be(new Receipt(4182, 19152.00m, "CHQ-1"));
    }

    /// <summary>Money is the reason this exists, so it is asserted as money and not as a double.</summary>
    [Fact]
    public void A_total_comes_back_to_the_penny()
    {
        var original = Result.Success(new Receipt(1, 0.07m, null));

        var restored = JsonSerializer.Deserialize<Result<Receipt>>(JsonSerializer.Serialize(original));

        restored!.Value.GrandTotal.Should().Be(0.07m);
    }

    [Fact]
    public void A_failed_result_survives_with_its_code()
    {
        var original = Result.Failure<Receipt>(new Error("drawer.not_open", "Open a drawer before taking cash."));

        var restored = JsonSerializer.Deserialize<Result<Receipt>>(JsonSerializer.Serialize(original));

        restored!.IsFailure.Should().BeTrue();
        restored.Error.Code.Should().Be("drawer.not_open");
    }

    [Fact]
    public void A_non_generic_result_survives_both_ways()
    {
        var success = JsonSerializer.Deserialize<Result>(JsonSerializer.Serialize(Result.Success()));
        var failure = JsonSerializer.Deserialize<Result>(
            JsonSerializer.Serialize(Result.Failure(new Error("cart.empty", "The cart has no lines."))));

        success!.IsSuccess.Should().BeTrue();
        failure!.Error.Code.Should().Be("cart.empty");
    }

    /// <summary>
    /// Reading <c>Value</c> off a failure throws by design. Writing one must therefore not reach for
    /// it — that throw is what turned caching a failure into a 500 that hid the real business error.
    /// </summary>
    [Fact]
    public void Writing_a_failure_does_not_reach_for_its_value()
    {
        var act = () => JsonSerializer.Serialize(Result.Failure<Receipt>(new Error("stock.insufficient", "Not enough.")));

        act.Should().NotThrow();
    }

    /// <summary>
    /// Entries written before the converter existed carry the names the default serializer used.
    /// They live 24 hours, so a deploy must not turn the ones already in the store into failures.
    /// </summary>
    [Fact]
    public void An_entry_written_by_the_old_serializer_is_still_readable()
    {
        const string legacy =
            """{"IsSuccess":true,"IsFailure":false,"Error":{"Code":"","Message":"","Arguments":null},"Value":{"TransactionId":13,"GrandTotal":19152.00,"Reference":null}}""";

        var restored = JsonSerializer.Deserialize<Result<Receipt>>(legacy);

        restored!.IsSuccess.Should().BeTrue();
        restored.Value.TransactionId.Should().Be(13);
        restored.Value.GrandTotal.Should().Be(19152.00m);
    }
}
