using Avalonia;
using Avalonia.Input;

namespace PhotoViewer.UI.Viewer;

public class SelectionResizer(SelectionResizerType position, Rect indicatorRegion, Rect hitRegion)
{

    public SelectionResizerType Type { get; set; } = position;

    public Rect HitRegion { get; set; } = hitRegion;

    public Rect IndicatorRegion { get; set; } = indicatorRegion;

    public Cursor GetCursor()
    {
        return Type switch
        {
            SelectionResizerType.TopLeft => new Cursor(StandardCursorType.TopLeftCorner),
            SelectionResizerType.Top => new Cursor(StandardCursorType.SizeNorthSouth),
            SelectionResizerType.TopRight => new Cursor(StandardCursorType.TopRightCorner),
            SelectionResizerType.Right => new Cursor(StandardCursorType.SizeWestEast),
            SelectionResizerType.BottomRight => new Cursor(StandardCursorType.BottomRightCorner),
            SelectionResizerType.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
            SelectionResizerType.BottomLeft => new Cursor(StandardCursorType.BottomLeftCorner),
            SelectionResizerType.Left => new Cursor(StandardCursorType.SizeWestEast),
            _ => Cursor.Default,
        };
    }

}
