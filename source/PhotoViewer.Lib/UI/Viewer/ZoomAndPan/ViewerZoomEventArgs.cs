using System;

namespace PhotoViewer.UI.Viewer.ZoomAndPan;

public class ViewerZoomEventArgs : EventArgs
{

    public double ZoomFactor { get; set; } = 0f;

    public bool IsManualZoom { get; set; } = false;

    public bool IsZoomModeChange { get; set; } = false;

    public bool IsPreviewingImage { get; set; } = false;

    public ZoomChangeSource ChangeSource { get; set; } = ZoomChangeSource.Unknown;

    public ViewerZoomEventArgs() { }
}
