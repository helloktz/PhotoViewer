using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public class JsonHashSetToStringConverter : JsonConverter<HashSet<string>>
{
    private readonly string _delimiter = ";";

    public override void Write(Utf8JsonWriter writer, HashSet<string> arr, JsonSerializerOptions options)
    {
        var str = string.Join(_delimiter, arr);

        writer.WriteStringValue(str);
    }

    public override HashSet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        var arr = str?.Split(_delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new HashSet<string>(arr ?? [], StringComparer.OrdinalIgnoreCase);
    }
}
