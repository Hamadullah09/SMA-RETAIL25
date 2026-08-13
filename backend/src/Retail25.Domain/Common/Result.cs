namespace Retail25.Domain.Common;

/// <summary>
/// A domain rule violation. <see cref="Code"/> is a stable, machine-readable key that the API
/// surfaces in RFC 7807 responses and the UI translates — error text is never hardcoded English
/// in a business rule.
/// </summary>
/// <param name="Code">Stable key, e.g. <c>stock.insufficient</c>.</param>
/// <param name="Message">Developer-facing fallback description.</param>
/// <param name="Arguments">Values for message interpolation in the presentation layer.</param>
public sealed record Error(string Code, string Message, IReadOnlyDictionary<string, object?>? Arguments = null)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public Error With(string key, object? value)
    {
        var args = Arguments is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(Arguments, StringComparer.Ordinal);
        args[key] = value;
        return this with { Arguments = args };
    }
}

/// <summary>
/// Explicit success/failure without exceptions for expected outcomes. Exceptions remain for
/// genuinely exceptional conditions (I/O faults, programming errors).
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(ResultJsonConverterFactory))]
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    /// <summary>Returns the first failure in <paramref name="results"/>, or success.</summary>
    public static Result FirstFailureOrSuccess(params Result[] results)
    {
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Success();
    }
}

// Declared again rather than inherited: System.Text.Json reads this attribute off the exact type it
// is converting, and does not walk up to the base class to find one.
[System.Text.Json.Serialization.JsonConverter(typeof(ResultJsonConverterFactory))]
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
