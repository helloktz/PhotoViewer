using PhotoViewer.Common.Types.JsonTypeConverters;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types;

public sealed class PluginTrustInfo
{

    public bool Enabled { get; set; }

    public string Hash { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonHashSetToStringConverter))]
    public HashSet<string>? DisabledDecodeExtensions { get; set; }

    [JsonConverter(typeof(JsonHashSetToStringConverter))]
    public HashSet<string>? DisabledEncodeExtensions { get; set; }
}
