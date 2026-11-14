using PhotoViewer.Common.Types.JsonTypeConverters;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types;

public enum PathType
{
    File,
    Dir,
    Unknown,
}

public enum DefaultAppScope
{

    CurrentUser,

    LocalMachine,
}

public enum BackdropStyle
{
    Mica,
    MicaAlt,
    Acrylic,

    None,
}

public enum IgExitCode
{
    Done = 0,
    AdminRequired = 1,
    Error = 2,
    Error_FileNotFound = 3,
}

public enum ImageSaveSource
{
    Undefined,
    SelectedArea,
    Clipboard,
    CurrentFile,
}

public enum ImageOrderBy
{
    Name = 0,
    Random,
    FileSize,
    Extension,
    DateCreated,
    DateAccessed,
    DateModified,
    ExifDateTaken,
    ExifRating,
}

public enum ImageOrderType
{
    Asc = 0,
    Desc = 1,
}

public enum BrowsingMode
{

    Turbo = 0,

    Sequential = 1,
}

public enum ImageInfoOverlayMode
{

    Hidden = 0,

    Always = 1,

    AlwaysAutoHide = 2,

    Borderless = 3,

    BorderlessAutoHide = 4,
}

public enum ImageResamplingMethod : int
{
    Auto = 0,
    Point,
    Box,
    Triangle,
    Hermite,
    Hann,
    Hamming,
    Blackman,
    Gaussian,
    Quadratic,
    Cubic,
    Catrom,
    Mitchell,
    Jinc,
    Sinc,
    SincFast,
    Kaiser,
    Welch,
    Parzen,
    Bohman,
    Bartlett,
    Lagrange,
    Lanczos,
    LanczosSharp,
    Lanczos2,
    Lanczos2Sharp,
    Robidoux,
    RobidouxSharp,
    Cosine,
    Spline,
    LanczosRadius,
    CubicSpline,
    MagicKernelSharp2013,
    MagicKernelSharp2021,
}

public enum OSType
{
    Unknown,
    Windows,
    Mac,
    Linux,
}

public enum LayoutControl
{
    Toolbar,
    Gallery,
}

[JsonConverter(typeof(JsonStringEnumSafeConverter<LayoutPosition>))]
public enum LayoutPosition
{
    Top,
    Bottom,
    Left,
    Right,
}
