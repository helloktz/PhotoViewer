using SkiaSharp;
using System;
using System.Collections.Generic;

namespace PhotoViewer.Common.Printing;

public enum PrintOrientation
{
    Auto,
    Portrait,
    Landscape,
}

public enum PrintFitMode
{
    Fit,
    Fill,
    ActualSize,
}

public enum PrintMarginPreset
{
    None,
    Narrow,
    Normal,
    Wide,
}

public enum PrintColorMode
{
    Color,
    Grayscale,
}

public enum PrintDuplex
{
    None,
    LongEdge,
    ShortEdge,
}

public enum PrintPageScope
{
    Current,
    All,
    Range,
}

public enum PrintRegion
{
    WholeImage,
    Selection,
    VisibleArea,
}

public enum PrinterState
{
    Unknown,
    Ready,
    Busy,
    Offline,
    Paused,
    Error,
}

public readonly record struct PrintMargins(float Left, float Top, float Right, float Bottom)
{

    public static PrintMargins Zero { get; } = new(0, 0, 0, 0);

    public bool IsZero => Left <= 0 && Top <= 0 && Right <= 0 && Bottom <= 0;

    public PrintMargins Max(PrintMargins other)
    {
        return new(Math.Max(Left, other.Left), Math.Max(Top, other.Top),
            Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    public PrintMargins ToLandscape()
    {
        var horizontal = Math.Max(Top, Bottom);
        var vertical = Math.Max(Left, Right);
        return new(horizontal, vertical, horizontal, vertical);
    }
}

public sealed record PrinterInfo(string Id, string DisplayName)
{

    public bool IsDefault { get; init; }

    public bool IsVirtual { get; init; }

    public string? Location { get; init; }

    public string? OutputFileExtension { get; init; }
}

public sealed record PrinterStatus(PrinterState State, string? Message = null);

public sealed record PaperInfo(string Id, string DisplayName, SKSize SizePt)
{

    public PrintMargins HardwareMarginsPt { get; init; } = PrintMargins.Zero;

    public bool IsPrintableAreaMeasured { get; init; } = true;
}

public sealed record PrinterCapabilities
{

    public IReadOnlyList<PaperInfo> Papers { get; init; } = [];

    public string? DefaultPaperId { get; init; }

    public bool SupportsColor { get; init; } = true;

    public bool SupportsDuplex { get; init; }

    public bool SupportsCollate { get; init; }

    public int MaxCopies { get; init; } = 999;

    public IReadOnlyList<int> ResolutionsDpi { get; init; } = [];

    public int DefaultDpi { get; init; } = 300;

    public bool HasPropertiesDialog { get; init; }
}

public sealed class PrintJobSettings
{

    public required PrinterInfo Printer { get; set; }

    public required PaperInfo Paper { get; set; }

    public bool IsLandscape { get; set; }

    public int Copies { get; set; } = 1;

    public bool Collate { get; set; } = true;

    public PrintDuplex Duplex { get; set; } = PrintDuplex.None;

    public PrintColorMode ColorMode { get; set; } = PrintColorMode.Color;

    public int Dpi { get; set; } = 300;

    public string? OutputPath { get; set; }

    public byte[]? PlatformState { get; set; }
}

public readonly record struct PrintProgress(int PageIndex, int PageCount);
