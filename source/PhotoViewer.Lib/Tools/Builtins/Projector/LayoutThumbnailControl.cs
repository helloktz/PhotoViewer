using Avalonia;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using System;
using System.Globalization;

namespace PhotoViewer.Tools;

public sealed class LayoutThumbnailControl : PhControl
{
    private const double BOX_WIDTH = 120;
    private const double BOX_HEIGHT = 80;

    private const float FRAME_RADIUS = 6;
    private const float CELL_RADIUS = 3;
    private const double FRAME_PADDING = 5;
    private const double CELL_GAP = 4;

    private const int CELL_FILL_ALPHA = 50;

    private const double LABEL_PADDING_X = 10;
    private const double LABEL_PADDING_Y = 4;

    public ProjectorLayout? Layout { get; }

    public int[] ProjectorNumbers { get; }

    public double AspectRatio { get; }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LayoutThumbnailControl, bool>(nameof(IsActive));

    static LayoutThumbnailControl()
    {
        AffectsRender<LayoutThumbnailControl>(IsActiveProperty);
    }

    public LayoutThumbnailControl(ProjectorLayout? layout, int[] projectorNumbers, double aspectRatio)
    {
        Layout = layout;
        ProjectorNumbers = projectorNumbers;
        AspectRatio = double.IsFinite(aspectRatio) && aspectRatio > 0 ? aspectRatio : 16d / 9;
        IsHitTestVisible = false;
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        InvalidateVisual();
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _ = base.MeasureOverride(availableSize);

        var width = BOX_WIDTH;
        var height = width / AspectRatio;
        if (height > BOX_HEIGHT)
        {
            height = BOX_HEIGHT;
            width = height * AspectRatio;
        }

        return new Size(width, height);
    }

    public override void Render(DrawingContext c)
    {
        base.Render(c);

        var accent = Core.AccentColor;
        var foreground = Resx.GetBrushColor(ResxId.IG_ThemeForegroundBrush, Core.Theme.InvertedBaseColor);

        var frame = new Rect(Bounds.Size).Deflate(0.5);
        var normalBorder = Resx.GetBrushColor(ResxId.IG_BorderControlBrush, foreground.WithAlpha(90));
        var frameBorder = IsActive ? accent.WithAlpha(230) : normalBorder;
        var frameFill = Resx.GetBrushColor(ResxId.IG_BackgroundNeutralBrush, foreground.WithAlpha(14));
        c.DrawRectangleEx(frame, FRAME_RADIUS, frameBorder, frameFill, IsActive ? 2f : 1f);

        if (Layout is null)
        {
            DrawDefaultLabel(c, frame, accent, foreground);
            return;
        }

        var area = frame.Deflate(FRAME_PADDING - CELL_GAP / 2);
        for (var cell = 0; cell < Layout.CellCount; cell++)
        {
            var cellRect = Layout.GetCellBounds(area, cell).Deflate(CELL_GAP / 2);
            if (cellRect.Width <= 0 || cellRect.Height <= 0) continue;

            var projectorIndex = Layout.GetProjectorIndex(cell);
            var hasProjector = projectorIndex >= 0 && projectorIndex < ProjectorNumbers.Length;
            if (hasProjector)
            {
                DrawFilledCell(c, cellRect, ProjectorNumbers[projectorIndex], accent, foreground);
            }
            else
            {
                DrawEmptyCell(c, cellRect, foreground);
            }
        }
    }

    private void DrawDefaultLabel(DrawingContext c, Rect frame, Color accent, Color foreground)
    {
        var label = Core.Lang[LangId._Default];
        var fontSize = Const.FONT_SIZE_BODY;
        var labelSize = c.MeasureTextEx(label, FontFamily, fontSize);

        var box = new Rect(
            frame.Center.X - labelSize.Width / 2 - LABEL_PADDING_X,
            frame.Center.Y - labelSize.Height / 2 - LABEL_PADDING_Y,
            labelSize.Width + LABEL_PADDING_X * 2,
            labelSize.Height + LABEL_PADDING_Y * 2);
        c.DrawRectangleEx(box, CELL_RADIUS, null, GetCellFill(accent, foreground));

        c.DrawTextEx(label, FontFamily, fontSize,
            box.X + LABEL_PADDING_X,
            box.Y + LABEL_PADDING_Y,
            foreground);
    }

    private void DrawFilledCell(DrawingContext c, Rect cellRect, int projectorNumber, Color accent, Color foreground)
    {
        c.DrawRectangleEx(cellRect, CELL_RADIUS, null, GetCellFill(accent, foreground));

        var label = projectorNumber.ToString(CultureInfo.InvariantCulture);
        var fontSize = Const.FONT_SIZE_BODY;
        var labelSize = c.MeasureTextEx(label, FontFamily, fontSize);
        if (!IsFitting(labelSize, cellRect))
        {
            fontSize = Const.FONT_SIZE_SMALL;
            labelSize = c.MeasureTextEx(label, FontFamily, fontSize);
            if (!IsFitting(labelSize, cellRect)) return;
        }

        c.DrawTextEx(label, FontFamily, fontSize,
            cellRect.Center.X - labelSize.Width / 2,
            cellRect.Center.Y - labelSize.Height / 2,
            foreground);
    }

    private Color GetCellFill(Color accent, Color foreground)
    {
        if (IsActive) return accent.WithAlpha(ScreenMapControl.TILE_FILL_ALPHA);

        return foreground.WithAlpha(CELL_FILL_ALPHA);
    }

    private static bool IsFitting(Size textSize, Rect rect)
    {
        var isNarrowEnough = textSize.Width + 2 <= rect.Width;
        if (!isNarrowEnough) return false;

        var isShortEnough = textSize.Height <= rect.Height;
        return isShortEnough;
    }

    private static void DrawEmptyCell(DrawingContext c, Rect cellRect, Color foreground)
    {
        var pen = new Pen(foreground.WithAlpha(80).ToBrush(), 1, DashStyle.Dash);
        var outline = new RoundedRect(cellRect.Deflate(0.5), CELL_RADIUS);

        c.DrawRectangle(null, pen, outline);
    }

}
