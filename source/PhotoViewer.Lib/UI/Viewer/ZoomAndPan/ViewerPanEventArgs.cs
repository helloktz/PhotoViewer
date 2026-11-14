using Avalonia;
using System;

namespace PhotoViewer.UI.Viewer.ZoomAndPan;

public class ViewerPanEventArgs(Rect oldSrcRect, Rect newSrcRect) : EventArgs
{

    public Rect OldSourceRect { get; private set; } = oldSrcRect;

    public Rect NewSourceRect { get; private set; } = newSrcRect;

}
