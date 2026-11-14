using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public class JsonArrayToZoomFactorConverter : JsonConverter<double[]>
{
    private readonly string _delimiter = ";";

    public override void Write(Utf8JsonWriter writer, double[] arr, JsonSerializerOptions options)
    {
        var numbers = arr.Select(v => v * 100f);
        var str = string.Join(_delimiter, numbers);

        writer.WriteStringValue(str);
    }

    public override double[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        var arr = str
            ?.Split(_delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v =>
            {
                if (double.TryParse(v, out var numberValue))
                {
                    return numberValue / 100f;
                }

                return double.NaN;
            })
            .Where(v => !double.IsNaN(v))
            .ToArray() ?? [];

        return arr;
    }
}
