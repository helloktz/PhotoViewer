using PhotoViewer.Common.Types.JsonTypeConverters;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types;

[JsonConverter(typeof(JsonStringEnumSafeConverter<MouseClickEvent>))]
public enum MouseClickEvent
{
    LeftClick = 1,
    LeftDoubleClick,
    RightClick,
    WheelClick,

    XButton1Click,
    XButton2Click,
}

[JsonConverter(typeof(JsonStringEnumSafeConverter<MouseWheelEvent>))]
public enum MouseWheelEvent
{
    Scroll = 1,
    CtrlAndScroll,
    ShiftAndScroll,
    AltAndScroll,
}

[JsonConverter(typeof(JsonStringEnumSafeConverter<MouseWheelAction>))]
public enum MouseWheelAction
{
    DoNothing = 0,
    Zoom = 1,
    PanVertically = 2,
    PanHorizontally = 3,
    BrowseImages = 4
}
