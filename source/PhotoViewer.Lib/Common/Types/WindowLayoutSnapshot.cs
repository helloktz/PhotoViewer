using Avalonia;

namespace PhotoViewer.Common.Types;

public readonly record struct WindowLayoutSnapshot
{

    public bool IsMaximized { get; init; }

    public Rect? Bounds { get; init; }

    public bool ShowToolbar { get; init; }

    public bool ShowGallery { get; init; }

    public bool IsFrameless { get; init; }

    public bool IsWindowFit { get; init; }
}
