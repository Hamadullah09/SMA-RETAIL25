using System.Text.Json;
using FluentAssertions;
using Retail25.Domain.Sales.Pricing;
using Xunit;

namespace Retail25.Domain.UnitTests.Pricing;

/// <summary>
/// Runs every case in <c>Goldens/pricing</c> through the engine.
/// <para>
/// This is the parity harness the roadmap calls for (doc 04 §8): the money is asserted to the cent
/// against a file that cites the guide page it came from. A failure here is a change in what the
/// customer pays, and should be read that way.
/// </para>
/// </summary>
public sealed class PricingGoldenTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TheoryData<string> GoldenFiles
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var path in Directory.EnumerateFiles(GoldenDirectory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                data.Add(Path.GetFileName(path));
            }

            return data;
        }
    }

    private static string GoldenDirectory => Path.Combine(AppContext.BaseDirectory, "Goldens", "pricing");

    [Theory]
    [MemberData(nameof(GoldenFiles))]
    public void Engine_reproduces_the_golden_totals(string fileName)
    {
        var golden = Load(fileName);
        var (lines, adjustments, context) = GoldenCaseBuilder.Build(golden);

        var result = SalePricingEngine.Calculate(lines, adjustments, context);

        using var scope = new FluentAssertions.Execution.AssertionScope($"{golden.Name} ({golden.Source})");

        result.Subtotal.Should().Be(golden.Expected.Subtotal, "subtotal");
        result.AdjustmentTotal.Should().Be(golden.Expected.AdjustmentTotal, "adjustment total");
        result.DiscountedSubtotal.Should().Be(golden.Expected.DiscountedSubtotal, "discounted subtotal");
        result.AddOnCharge.Should().Be(golden.Expected.AddOnCharge, "add-on charge");
        result.Tax1Total.Should().Be(golden.Expected.Tax1Total, "tax 1");
        result.Tax2Total.Should().Be(golden.Expected.Tax2Total, "tax 2");
        result.GrandTotal.Should().Be(golden.Expected.GrandTotal, "grand total");
        result.LoyaltyPointsEarned.Should().Be(golden.Expected.LoyaltyPointsEarned, "points earned");
        result.LoyaltyPointsRedeemed.Should().Be(golden.Expected.LoyaltyPointsRedeemed, "points redeemed");

        if (golden.Expected.Lines is not { } expectedLines)
        {
            return;
        }

        result.Lines.Should().HaveCount(expectedLines.Count);

        for (var i = 0; i < expectedLines.Count; i++)
        {
            var expected = expectedLines[i];
            var actual = result.Lines[i];

            actual.UnitPrice.Should().Be(expected.UnitPrice, "line {0} unit price", i);
            actual.ChargeableQuantity.Should().Be(expected.ChargeableQuantity, "line {0} chargeable quantity", i);
            actual.LineNet.Should().Be(expected.LineNet, "line {0} net", i);
            actual.Tax1Amount.Should().Be(expected.Tax1Amount, "line {0} tax 1", i);
            actual.Tax2Amount.Should().Be(expected.Tax2Amount, "line {0} tax 2", i);
            actual.PriceOrigin.Should().Be(expected.PriceOrigin, "line {0} price origin", i);
        }
    }

    /// <summary>
    /// The suite is only meaningful if it is actually on disk and copied to the output. A silent
    /// zero-case run would look green and prove nothing.
    /// </summary>
    [Fact]
    public void Golden_suite_is_present()
    {
        Directory.Exists(GoldenDirectory).Should().BeTrue($"golden files should be copied to {GoldenDirectory}");
        Directory.EnumerateFiles(GoldenDirectory, "*.json").Should().HaveCountGreaterThan(10);
    }

    /// <summary>Every case has to cite where its numbers came from, or it is just a snapshot of a bug.</summary>
    [Theory]
    [MemberData(nameof(GoldenFiles))]
    public void Every_case_cites_its_source(string fileName)
    {
        var golden = Load(fileName);

        golden.Source.Should().NotBeNullOrWhiteSpace();
        golden.Description.Should().NotBeNullOrWhiteSpace();
    }

    private static GoldenCase Load(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(GoldenDirectory, fileName));
        return JsonSerializer.Deserialize<GoldenCase>(json, Options)
               ?? throw new InvalidOperationException($"Golden file {fileName} could not be read.");
    }
}
