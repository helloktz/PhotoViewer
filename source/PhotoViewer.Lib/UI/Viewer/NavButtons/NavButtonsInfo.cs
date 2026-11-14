using Avalonia;
using Avalonia.Media;

namespace PhotoViewer.UI.Viewer;

internal class NavButtonsInfo
{
    public static readonly Size NAV_BTN_SIZE = new Size(50, 50);

    public static readonly double NAV_BTN_MARGIN = 10;

    public bool IsEnabled { get; set; } = true;

    public string RightArrowSvgPath { get; set; } = string.Empty;

    public string LeftArrowSvgPath { get; set; } = string.Empty;

    public bool IsLeftHovered { get; set; } = false;

    public bool IsRightHovered { get; set; } = false;

    public bool IsLeftPressed { get; set; } = false;

    public bool IsRightPressed { get; set; } = false;

    public Point? PointerDownPoint { get; set; } = null;

    public bool IsDragging { get; set; } = false;

    public IImage? LeftIcon { get; set; } = null;

    public IImage? RightIcon { get; set; } = null;

    public void ResetState()
    {
        IsLeftHovered = false;
        IsRightHovered = false;
        IsLeftPressed = false;
        IsRightPressed = false;
        PointerDownPoint = null;
        IsDragging = false;
    }
}
