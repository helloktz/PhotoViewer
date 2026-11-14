using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using System;

namespace PhotoViewer.UI.Viewer;

public class ViewerPointerEventArgs(PointerEventArgs e, PointerPoint p, PixelPoint srcPoint) : EventArgs
{

    public PointerEventArgs Event => e;

    public PointerPoint Point => p;

    public PixelPoint SourcePoint => srcPoint;

}

public class ViewerPointerClickEventArgs(RoutedEventArgs e, MouseClickEvent clickEvent) : EventArgs
{

    public RoutedEventArgs Event => e;

    public MouseClickEvent ClickEvent => clickEvent;
}

public class ViewerMouseWheelEventArgs(PointerWheelEventArgs e, MouseWheelEvent wheelEvent, double delta, Point position) : EventArgs
{

    public PointerWheelEventArgs Event => e;

    public MouseWheelEvent WheelEvent => wheelEvent;

    public double Delta => delta;

    public Point Position => position;
}

public class PhotoFrameChangedEventArgs(bool canAnimate, bool isAnimating, bool isLivePhoto = false) : AnimatorFrameChangedEventArgs
{
    public bool CanAnimate => canAnimate;
    public bool IsAnimating => isAnimating;
    public bool IsLivePhoto => isLivePhoto;

}
