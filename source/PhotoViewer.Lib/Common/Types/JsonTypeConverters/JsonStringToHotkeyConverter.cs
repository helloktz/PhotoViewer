using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public class JsonStringToHotkeyConverter : JsonConverter<Hotkey>
{
    public override void Write(Utf8JsonWriter writer, Hotkey? hotkey, JsonSerializerOptions options)
    {
        writer.WriteStringValue(hotkey?.InvariantKeyString);
    }

    public override Hotkey? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();

        return Hotkey.ParseFrom(str);
    }
}
