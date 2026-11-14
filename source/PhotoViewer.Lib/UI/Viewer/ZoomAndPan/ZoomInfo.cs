
using Avalonia;

namespace PhotoViewer.UI.Viewer.ZoomAndPan;

internal class ZoomInfo
{
    public const double MAX_ZOOM_SPEED = 500;

    public ZoomMode Mode { get; set; } = ZoomMode.AutoZoom;

    public double Speed { get; set; } = 0f;

    public double Min { get; set; } = 0.01d;

    public double Max { get; set; } = 100d;

    public double[] Levels { get; set; } = [];

    public double Factor { get; set; } = 1f;

    public double OldFactor { get; set; } = 1f;

    public bool IsManual { get; set; } = false;

    public Point ZoomedPoint { get; set; } = new();

}
