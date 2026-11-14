using PhotoViewer.Common.Types;
using System.Text.Json.Serialization;

namespace PhotoViewer.Tools;

[JsonSerializable(typeof(ColorPickerConfig))]
public partial class ColorPickerConfigJsonContext : JsonSerializerContext { }

public class ColorPickerConfig() : PhReactive
{

    public bool ShowRgbWithAlpha
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = true;

    public bool ShowHexWithAlpha
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = true;

    public bool ShowHslWithAlpha
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = true;

    public bool ShowHsvWithAlpha
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = true;

    public bool ShowCmykWithAlpha
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = false;

    public bool ShowCIELabWithAlpha
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = false;

}
