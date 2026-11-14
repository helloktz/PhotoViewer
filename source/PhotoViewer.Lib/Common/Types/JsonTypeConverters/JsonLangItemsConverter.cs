using PhotoViewer.Common.Localization;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public sealed class JsonLangItemsConverter : JsonConverter<IDictionary<LangId, string>>
{
    public override IDictionary<LangId, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();

        var result = new Dictionary<LangId, string>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();

            var name = reader.GetString();
            reader.Read();

            if (reader.TokenType == JsonTokenType.String
                && Lang.GetKey(name) is { } id)
            {
                result[id] = reader.GetString() ?? string.Empty;
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, IDictionary<LangId, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var kvp in value)
        {
            writer.WritePropertyName(kvp.Key.ToString());
            writer.WriteStringValue(kvp.Value);
        }

        writer.WriteEndObject();
    }
}
