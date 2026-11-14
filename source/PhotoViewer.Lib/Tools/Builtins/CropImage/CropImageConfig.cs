using Avalonia;
using PhotoViewer.Common.Types;
using PhotoViewer.Common.Types.JsonTypeConverters;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoViewer.Tools;

[JsonSerializable(typeof(CropImageConfig))]
public partial class CropImageConfigJsonContext : JsonSerializerContext { }

public class CropImageConfig() : PhReactive
{

    public bool CloseAfterSaved
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = false;

    public bool AutoCenterSelection
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = true;

    [JsonConverter(typeof(JsonStringEnumSafeConverter<SelectionAspectRatio>))]
    public SelectionAspectRatio AspectRatio
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = SelectionAspectRatio.FreeRatio;

    [JsonConverter(typeof(JsonArrayToIntConverter))]
    public int[] AspectRatioValues
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = [0, 0];

    [JsonConverter(typeof(JsonStringEnumSafeConverter<DefaultSelectionType>))]
    public DefaultSelectionType InitSelectionType
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = DefaultSelectionType.SelectNone;

    [JsonConverter(typeof(JsonArrayToRectConverter))]
    public Rect InitSelectedArea
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = new();

    public static Dictionary<SelectionAspectRatio, int[]> AspectRatioValue => new(9)
    {
        { SelectionAspectRatio.Ratio1_1,    [1, 1] },
        { SelectionAspectRatio.Ratio1_2,    [1, 2] },
        { SelectionAspectRatio.Ratio2_1,    [2, 1] },
        { SelectionAspectRatio.Ratio2_3,    [2, 3] },
        { SelectionAspectRatio.Ratio3_2,    [3, 2] },
        { SelectionAspectRatio.Ratio3_4,    [3, 4] },
        { SelectionAspectRatio.Ratio4_3,    [4, 3] },
        { SelectionAspectRatio.Ratio9_16,   [9, 16] },
        { SelectionAspectRatio.Ratio16_9,   [16, 9] },
    };

}
