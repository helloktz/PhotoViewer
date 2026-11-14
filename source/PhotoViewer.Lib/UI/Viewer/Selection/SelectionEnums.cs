namespace PhotoViewer.UI.Viewer;

public enum SelectionResizerType
{
    TopLeft = 0,
    Top = 1 << 1,
    TopRight = 1 << 2,
    Right = 1 << 3,
    BottomRight = 1 << 4,
    Bottom = 1 << 5,
    BottomLeft = 1 << 6,
    Left = 1 << 7,
}

public enum SelectionAction
{
    None,

    Drawing,

    Resizing,

    Moving,
}
