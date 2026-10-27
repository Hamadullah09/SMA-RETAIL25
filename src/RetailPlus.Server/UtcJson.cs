using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace RetailPlus.Server;

/// <summary>
/// Date-times whose name ends in "Utc" are written with a Z, so a browser reads them as UTC. The database gives every
/// date-time back without a kind, and the others (the store's local times, as the tills record them) must stay without one.
/// </summary>
internal static class UtcJson
{
    public static void MarkUtc(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object) return;
        foreach (var property in type.Properties)
        {
            if (!property.Name.EndsWith("Utc", StringComparison.OrdinalIgnoreCase)) continue;
            if (property.PropertyType == typeof(DateTime)) property.CustomConverter = Utc.Instance;
            else if (property.PropertyType == typeof(DateTime?)) property.CustomConverter = NullableUtc.Instance;
        }
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static DateTime Read(ref Utf8JsonReader reader) =>
        AsUtc(DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));

    private static void Write(Utf8JsonWriter writer, DateTime value) =>
        writer.WriteStringValue(AsUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));

    private sealed class Utc : JsonConverter<DateTime>
    {
        public static readonly Utc Instance = new();

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => UtcJson.Read(ref reader);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => UtcJson.Write(writer, value);
    }

    private sealed class NullableUtc : JsonConverter<DateTime?>
    {
        public static readonly NullableUtc Instance = new();

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : UtcJson.Read(ref reader);

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value is { } v) UtcJson.Write(writer, v);
            else writer.WriteNullValue();
        }
    }
}
