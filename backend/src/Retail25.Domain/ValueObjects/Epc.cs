using System.Diagnostics.CodeAnalysis;
using Retail25.Domain.Common;

namespace Retail25.Domain.ValueObjects;

/// <summary>
/// An RFID Electronic Product Code: 24 to 96 uppercase hexadecimal characters, covering
/// SGTIN-96 (24 hex) through to the longest tags in use. One EPC identifies exactly one
/// physical unit — quantity is never encoded in a tag.
/// </summary>
public readonly record struct Epc
{
    public const int MinLength = 24;
    public const int MaxLength = 96;

    public static readonly Error InvalidLength = new(
        "epc.invalid_length",
        "An EPC must be between 24 and 96 hexadecimal characters.");

    public static readonly Error InvalidCharacters = new(
        "epc.invalid_characters",
        "An EPC may contain hexadecimal characters only.");

    private Epc(string value) => Value = value;

    public string Value { get; }

    public static Result<Epc> Create(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Result.Failure<Epc>(InvalidLength);
        }

        var trimmed = candidate.Trim().ToUpperInvariant();

        if (trimmed.Length is < MinLength or > MaxLength)
        {
            return Result.Failure<Epc>(InvalidLength.With("length", trimmed.Length));
        }

        foreach (var character in trimmed)
        {
            if (!Uri.IsHexDigit(character))
            {
                return Result.Failure<Epc>(InvalidCharacters.With("value", trimmed));
            }
        }

        return Result.Success(new Epc(trimmed));
    }

    public static bool TryCreate(string? candidate, [NotNullWhen(true)] out Epc? epc)
    {
        var result = Create(candidate);
        epc = result.IsSuccess ? result.Value : null;
        return result.IsSuccess;
    }

    public override string ToString() => Value;
}
