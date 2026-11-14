
using PhotoViewer.Common.Types.JsonTypeConverters;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Photoing;

[JsonSerializable(typeof(HdrToneMappingOptions))]
public partial class HdrToneMappingOptionsJsonContext : JsonSerializerContext { }

public sealed record HdrToneMappingOptions
{

    [JsonConverter(typeof(JsonStringEnumSafeConverter<HdrToneMappingMode>))]
    public HdrToneMappingMode Mode { get; set; } = HdrToneMappingMode.BT2408;

    public double Exposure { get; set; } = 0d;

    public double ReferenceWhiteNits { get; set; } = 203d;

    public double HighlightCompression { get; set; } = 0d;

    public double Saturation { get; set; } = 1d;
}
