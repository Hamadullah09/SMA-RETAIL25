using System.Text.RegularExpressions;
using Retail25.Domain.Common;

namespace Retail25.Domain.ValueObjects;

/// <summary>
/// Validation policy for stock codes. Supplied from configuration so a business can tighten or
/// relax its numbering scheme without a code change — the legacy guidance (Appendix B: letters and
/// digits, avoid punctuation) is the seeded default, not a compiled-in rule.
/// </summary>
/// <param name="MaxLength">Maximum permitted length.</param>
/// <param name="AllowedPattern">Regular expression the whole code must match.</param>
/// <param name="Uppercase">Whether codes are normalised to upper case for comparison.</param>
public sealed record StockCodePolicy(int MaxLength, string AllowedPattern, bool Uppercase)
{
    /// <summary>
    /// The policy seeded on first run: alphanumeric, up to 24 characters, case-insensitive.
    /// An administrator may change it; nothing in the engine assumes these values.
    /// </summary>
    public static readonly StockCodePolicy Default = new(24, "^[A-Za-z0-9]+$", true);
}

/// <summary>
/// The merchant's own identifier for an item — the code typed at the till, printed as Code 39,
/// and used as the 5-digit item identifier inside a Type 2 random-weight barcode.
/// </summary>
public readonly record struct StockCode
{
    public static readonly Error Empty = new("stock_code.empty", "A stock code is required.");
    public static readonly Error TooLong = new("stock_code.too_long", "The stock code exceeds the configured maximum length.");
    public static readonly Error InvalidFormat = new("stock_code.invalid_format", "The stock code contains characters the configured policy does not allow.");

    private StockCode(string value) => Value = value;

    public string Value { get; }

    public static Result<StockCode> Create(string? candidate, StockCodePolicy? policy = null)
    {
        policy ??= StockCodePolicy.Default;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Result.Failure<StockCode>(Empty);
        }

        var normalised = candidate.Trim();
        if (policy.Uppercase)
        {
            normalised = normalised.ToUpperInvariant();
        }

        if (normalised.Length > policy.MaxLength)
        {
            return Result.Failure<StockCode>(TooLong.With("maxLength", policy.MaxLength).With("value", normalised));
        }

        // A one-second timeout guards against a pathological administrator-supplied pattern.
        if (!Regex.IsMatch(normalised, policy.AllowedPattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            return Result.Failure<StockCode>(InvalidFormat.With("value", normalised).With("pattern", policy.AllowedPattern));
        }

        return Result.Success(new StockCode(normalised));
    }

    public override string ToString() => Value;
}
