using System;

namespace PhotoViewer.UI.Viewer;

public enum NavButtonDirection
{
    Left,
    Right,
}

public class NavButtonClickedEventArgs(NavButtonDirection direction) : EventArgs
{

    public NavButtonDirection Direction => direction;
}
