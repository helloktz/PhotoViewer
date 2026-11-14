using System;

namespace PhotoViewer.UI.Viewer;

public enum PhotoSource
{
    None,
    Native,
    VectorRenderer,
}

public enum CheckerboardType
{

    None = 0,

    Client = 1,

    Image = 2,
}

public enum ScrollbarMode
{

    Never = 0,

    AutoHide = 1,

    Always = 2,
}

public enum ZoomMode
{
    AutoZoom,
    LockZoom,
    ScaleToWidth,
    ScaleToHeight,
    ScaleToFit,
    ScaleToFill,
}

public enum ZoomChangeSource
{
    Unknown,
    ZoomMode,
    SizeChanged,

    FrameChanged,
}

[Flags]
public enum AnimationSources
{
    None = 0,

    PanLeft = 1 << 1,
    PanRight = 1 << 2,
    PanUp = 1 << 3,
    PanDown = 1 << 4,

    ZoomIn = 1 << 5,

    ZoomOut = 1 << 6,
}

public enum ImageInterpolation : int
{

    Nearest,

    NearestMipmapNearest,

    NearestMipmapLinear,

    Linear,

    LinearMipmapNearest,

    LinearMipmapLinear,

    CubicMitchell,

    CubicCatmullRom,

}

public class AnimationSourceArgs(AnimationSources source, Action? callbackFn)
{
    public AnimationSources Source => source;
    public Action? CallbackFn => callbackFn;
}
