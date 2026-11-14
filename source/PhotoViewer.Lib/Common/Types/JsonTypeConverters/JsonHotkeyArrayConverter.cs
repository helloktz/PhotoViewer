using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public class JsonHotkeyArrayConverter : JsonConverter<Hotkey[]>
{
    public override void Write(Utf8JsonWriter writer, Hotkey[]? arr, JsonSerializerOptions options)
    {
        if (arr is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var hotkey in arr)
        {
            if (hotkey is null) continue;
            writer.WriteStringValue(hotkey.InvariantKeyString);
        }
        writer.WriteEndArray();
    }

    public override Hotkey[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hotkeys = new List<Hotkey>();

        if (reader.TokenType != JsonTokenType.StartArray) return [.. hotkeys];

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                continue;
            }

            var hotkey = Hotkey.ParseFrom(reader.GetString());
            if (hotkey is not null) hotkeys.Add(hotkey);
        }

        return [.. hotkeys];
    }
}
