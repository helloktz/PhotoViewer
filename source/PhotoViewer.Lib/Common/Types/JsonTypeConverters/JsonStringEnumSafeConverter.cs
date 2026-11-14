using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public sealed class JsonStringEnumSafeConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                if (Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var named))
                    return named;
                break;

            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var raw))
                    return (TEnum)Enum.ToObject(typeof(TEnum), raw);
                break;
        }

        return default;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public override TEnum ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var named))
            return named;

        return default;
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.ToString());
    }
}
