using System;

namespace PhotoViewer.Common.Photoing;

public enum ColorProfileOption
{
    None,
    Custom,
    CurrentMonitorProfile,

    AdobeRGB1998,
    AppleRGB,
    CoatedFOGRA39,
    ColorMatchRGB,
    sRGB,
    USWebCoatedSWOP,
}

[Flags]
public enum FlipOptions
{
    None = 0,
    Horizontal = 1 << 1,
    Vertical = 1 << 2,
}

public enum RotateOption
{
    Left = 0,
    Right = 1,
}

[Flags]
public enum ColorChannels
{
    R = 1 << 1,
    G = 1 << 2,
    B = 1 << 3,
    A = 1 << 4,

    RGB = R | G | B,
    RGBA = RGB | A,
    RA = R | A,
    GA = G | A,
    BA = B | A,
}

public enum HdrToneMappingMode
{

    None,

    BT2408,

    Reinhard,

    ACES,
}

public enum HdrTransferFunction
{

    None = 0,

    PQ = 1,

    HLG = 2,

    GainMap = 3,

    Linear = 4,

    ScRgb = 5,
}
