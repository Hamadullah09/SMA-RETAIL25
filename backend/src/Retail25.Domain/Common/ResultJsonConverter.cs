using System.Text.Json;
using System.Text.Json.Serialization;

namespace Retail25.Domain.Common;

/// <summary>
/// Teaches <c>System.Text.Json</c> how to rebuild a <see cref="Result"/>.
/// <para>
/// A result has no public constructor, deliberately: it is only ever produced through
/// <c>Success</c> or <c>Failure</c>. <c>System.Text.Json</c> cannot construct such a type, so
/// writing one worked and reading one threw <c>NotSupportedException</c> — every time, silently
/// filling the idempotency store with entries nothing could ever read back. Every command returns a
/// result, so idempotent replay had never worked at all. A cashier who pressed Pay twice was shown
/// a 500 for a sale the server had already taken the money for.
/// </para>
/// <para>
/// It lives <b>on the type</b> rather than in the serializer options of whichever store is doing
/// the writing, and that placement is the point. The first fix passed options at each call site and
/// missed one of the three stores, because <c>RedisIdempotencyStore</c> is declared in a file named
/// after the tag debouncer — a mistake CI caught and a reviewer might not. A result now round-trips
/// through any serializer, in any store, including ones not written yet.
/// </para>
/// </summary>
internal sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(Result)
           || (typeToConvert is { IsGenericType: true }
               && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => typeToConvert == typeof(Result)
            ? new ResultConverter()
            : (JsonConverter)Activator.CreateInstance(
                typeof(ResultConverter<>).MakeGenericType(typeToConvert!.GetGenericArguments()[0]))!;
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
        writer.WriteBoolean(ResultJson.IsSuccessName, value.IsSuccess);
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

        var value = ResultJson.TryProperty(root, ResultJson.ValueName, out var element)
            ? element.Deserialize<TValue>(options)
            : default;

        return Result.Success(value!);
    }

    public override void Write(Utf8JsonWriter writer, Result<TValue> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteBoolean(ResultJson.IsSuccessName, value.IsSuccess);
        ResultJson.WriteError(writer, value, options);

        // Only when it succeeded: reading Value off a failure throws by design, and that throw is
        // what made caching a failure crash the request rather than report it.
        if (value.IsSuccess)
        {
            writer.WritePropertyName(ResultJson.ValueName);
            JsonSerializer.Serialize(writer, value.Value, options);
        }

        writer.WriteEndObject();
    }
}

internal static class ResultJson
{
    internal const string IsSuccessName = "IsSuccess";
    internal const string ValueName = "Value";
    private const string ErrorName = "Error";

    /// <summary>
    /// Property lookup that tolerates either casing.
    /// <para>
    /// The stores wrote these names verbatim before this converter existed, and a camel-case naming
    /// policy would rename them from now on. Reading both means entries already sitting in the
    /// store stay readable for the rest of their twenty-four hours instead of turning into a wave
    /// of failures the moment this deploys.
    /// </para>
    /// </summary>
    internal static bool TryProperty(JsonElement root, string name, out JsonElement value)
        => root.TryGetProperty(name, out value)
           || root.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(name), out value);

    internal static bool IsSuccess(JsonElement root)
        => !TryProperty(root, IsSuccessName, out var element) || element.GetBoolean();

    internal static Error ReadError(JsonElement root, JsonSerializerOptions options)
    {
        if (!TryProperty(root, ErrorName, out var element) || element.ValueKind is JsonValueKind.Null)
        {
            // A failure has to carry an error — the constructor enforces it — so a stored failure
            // without one is corrupt rather than empty, and saying so beats throwing on an
            // invariant three frames away.
            return Unreadable;
        }

        return element.Deserialize<Error>(options) ?? Unreadable;
    }

    internal static void WriteError(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(ErrorName);
        JsonSerializer.Serialize(writer, value.Error, options);
    }

    private static Error Unreadable => new("cache.unreadable_error", "A cached failure carried no error.");
}
