using System.Text.Json;
using System.Text.Json.Serialization;
using Retail25.Domain.Common;

namespace Retail25.Infrastructure.Caching;

/// <summary>
/// How a cached command response is written and read back.
/// <para>
/// <see cref="Result"/> and <see cref="Result{TValue}"/> have no public constructor — deliberately,
/// because a result is only ever produced through <c>Success</c> or <c>Failure</c> — and
/// <c>System.Text.Json</c> refuses to deserialize a type it cannot construct. Writing one worked,
/// so the idempotency store filled up normally; reading one threw
/// <c>NotSupportedException</c> <i>every time</i>.
/// </para>
/// <para>
/// Which meant idempotent replay had never worked for any command, since every command returns a
/// result. The cost lands on the one moment it exists for: a cashier presses Pay, the screen
/// freezes, they press it again — and instead of the first receipt they were shown a 500, on a sale
/// the server had already taken the money for. The safe half held (the money moved once) and the
/// half that tells anybody so did not.
/// </para>
/// <para>
/// The property names are the ones the default serializer was already writing, so entries stored
/// before this converter existed are still readable rather than stranded for their 24-hour life.
/// </para>
/// </summary>
internal static class CacheSerialization
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ResultJsonConverterFactory() },
    };
}

internal sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(Result)
           || (typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => typeToConvert == typeof(Result)
            ? new ResultConverter()
            : (JsonConverter)Activator.CreateInstance(
                typeof(ResultConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal sealed class ResultConverter : JsonConverter<Result>
{
    public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);

        return ResultJson.IsSuccess(document.RootElement)
            ? Result.Success()
            : Result.Failure(ResultJson.ReadError(document.RootElement, options));
    }

    public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteBoolean("IsSuccess", value.IsSuccess);
        ResultJson.WriteError(writer, value, options);
        writer.WriteEndObject();
    }
}

internal sealed class ResultConverter<TValue> : JsonConverter<Result<TValue>>
{
    public override Result<TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (!ResultJson.IsSuccess(root))
        {
            return Result.Failure<TValue>(ResultJson.ReadError(root, options));
        }

        var value = root.TryGetProperty("Value", out var element)
            ? element.Deserialize<TValue>(options)
            : default;

        return Result.Success(value!);
    }

    public override void Write(Utf8JsonWriter writer, Result<TValue> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteBoolean("IsSuccess", value.IsSuccess);
        ResultJson.WriteError(writer, value, options);

        // Only when it succeeded: reading Value off a failure throws by design, and that throw is
        // what made caching a failure crash the request rather than report it.
        if (value.IsSuccess)
        {
            writer.WritePropertyName("Value");
            JsonSerializer.Serialize(writer, value.Value, options);
        }

        writer.WriteEndObject();
    }
}

internal static class ResultJson
{
    internal static bool IsSuccess(JsonElement root)
        => !root.TryGetProperty("IsSuccess", out var element) || element.GetBoolean();

    internal static Error ReadError(JsonElement root, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("Error", out var element) || element.ValueKind is JsonValueKind.Null)
        {
            // A failure has to carry an error — the Result constructor enforces it — so a stored
            // failure with none is corrupt rather than empty, and saying so beats throwing on a
            // constructor invariant three frames away.
            return new Error("cache.unreadable_error", "A cached failure carried no error.");
        }

        return element.Deserialize<Error>(options)
               ?? new Error("cache.unreadable_error", "A cached failure carried no error.");
    }

    internal static void WriteError(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
    {
        writer.WritePropertyName("Error");
        JsonSerializer.Serialize(writer, value.Error, options);
    }
}
