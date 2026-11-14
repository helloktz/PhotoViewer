using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public class JsonHashSetToArrayConverter : JsonConverter<HashSet<string>>
{
    public override void Write(Utf8JsonWriter writer, HashSet<string> arr, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in arr)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }

    public override HashSet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (reader.TokenType != JsonTokenType.StartArray) return set;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var item = reader.GetString();
            if (!string.IsNullOrWhiteSpace(item)) set.Add(item.Trim());
        }

        return set;
    }
}
