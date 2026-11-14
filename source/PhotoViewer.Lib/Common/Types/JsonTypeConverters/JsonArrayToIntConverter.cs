using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public class JsonArrayToIntConverter : JsonConverter<int[]>
{
    private readonly string _delimiter = ";";

    public override void Write(Utf8JsonWriter writer, int[] arr, JsonSerializerOptions options)
    {
        var str = string.Join(_delimiter, arr);

        writer.WriteStringValue(str);
    }

    public override int[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        var arr = str
            ?.Split(_delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v =>
            {
                if (int.TryParse(v, out var numberValue)) return numberValue;
                return int.MinValue;
            })
            .Where(v => v > int.MinValue)
            .ToArray() ?? [];

        return arr;
    }
}
