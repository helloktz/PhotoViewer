using Avalonia;

namespace PhotoViewer.UI.Viewer;

internal class SelectionInfo
{

    public bool Enabled { get; set; } = false;

    public Rect SourceRect { get; set; } = default;

    public Rect SourceRectBeforeMoved { get; set; } = default;

    public bool IsHovered { get; set; } = false;

    public SelectionResizer? HoveredResizer { get; set; } = null;

    public SelectionResizer? SelectedResizer { get; set; } = null;

    public Point? PointerDownPoint { get; set; } = null;

    public Point? PointerMovePoint { get; set; } = null;

    public bool IsLeftButtonPressed { get; set; } = false;

}
