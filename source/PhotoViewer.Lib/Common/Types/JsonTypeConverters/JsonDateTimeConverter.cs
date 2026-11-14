using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types.JsonTypeConverters;

public class JsonDateTimeConverter : JsonConverter<DateTime>
{
    public override void Write(Utf8JsonWriter writer, DateTime date, JsonSerializerOptions options)
    {
        writer.WriteStringValue(date.ToString(Const.DATETIME_FORMAT));
    }

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateTime.ParseExact(reader.GetString()!, Const.DATETIME_FORMAT, null);
    }
}
