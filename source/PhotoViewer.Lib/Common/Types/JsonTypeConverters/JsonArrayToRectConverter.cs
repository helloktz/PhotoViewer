using Avalonia;
using PhotoViewer.Common.Extensions;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public class JsonArrayToRectConverter : JsonConverter<Rect>
{
    private readonly string _delimiter = ";";

    public override void Write(Utf8JsonWriter writer, Rect rect, JsonSerializerOptions options)
    {
        var str = rect.ToStringDelimiter();

        writer.WriteStringValue(str);
    }

    public override Rect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        var arr = str
            ?.Split(_delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v =>
            {
                if (double.TryParse(v, out var numberValue)) return numberValue;
                return double.NaN;
            })
            .Where(v => !double.IsNaN(v))
            .ToArray() ?? [];

        var x = arr.Length > 0 ? arr[0] : 0;
        var y = arr.Length > 1 ? arr[1] : 0;
        var width = arr.Length > 2 ? arr[2] : 0;
        var height = arr.Length > 3 ? arr[3] : 0;

        width = Math.Max(0, width);
        height = Math.Max(0, height);

        var rect = new Rect(x, y, width, height);

        return rect;
    }
}
