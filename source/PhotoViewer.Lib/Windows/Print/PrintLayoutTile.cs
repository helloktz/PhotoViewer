using Avalonia;
using Avalonia.Media;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Printing;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace PhotoViewer.Common.Windows;

public sealed class PrintLayoutTile : PhControl
{
    private const double BOX_SIZE = 44;

    private const double CELL_GAP = 2;

    private SKSize _pageSizePt = new(595, 842);
    private IReadOnlyList<SKRect> _cellRectsPt = [];

    public PrintLayout Layout { get; }

    public PrintLayoutTile(PrintLayout layout)
    {
        Layout = layout;
        IsHitTestVisible = false;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
    }

    public void SetPage(SKSize pageSizePt, IReadOnlyList<SKRect> cellRectsPt)
    {
        _pageSizePt = pageSizePt;
        _cellRectsPt = cellRectsPt;

        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _ = base.MeasureOverride(availableSize);

        return new Size(BOX_SIZE, BOX_SIZE);
    }

    public override void Render(DrawingContext c)
    {
        base.Render(c);

        var foreground = Resx.GetBrushColor(ResxId.IG_ThemeForegroundBrush, Core.Theme.InvertedBaseColor);
        var accent = Core.AccentColor;
        var border = Resx.GetBrushColor(ResxId.IG_BorderControlBrush, foreground.WithAlpha(90));

        var scale = Math.Min(Bounds.Width, Bounds.Height) / Math.Max(_pageSizePt.Width, _pageSizePt.Height);
        var pageW = _pageSizePt.Width * scale;
        var pageH = _pageSizePt.Height * scale;
        var page = new Rect((Bounds.Width - pageW) / 2, (Bounds.Height - pageH) / 2, pageW, pageH).Deflate(0.5);

        c.DrawRectangleEx(page, 2, border, Colors.White);

        var scaleX = page.Width / _pageSizePt.Width;
        var scaleY = page.Height / _pageSizePt.Height;
        foreach (var cell in _cellRectsPt)
        {
            var w = cell.Width * scaleX;
            var h = cell.Height * scaleY;
            var gapX = Math.Min(CELL_GAP, w - 1) / 2;
            var gapY = Math.Min(CELL_GAP, h - 1) / 2;

            var rect = new Rect(
                page.X + cell.Left * scaleX + Math.Max(0, gapX),
                page.Y + cell.Top * scaleY + Math.Max(0, gapY),
                Math.Max(1, w - 2 * Math.Max(0, gapX)),
                Math.Max(1, h - 2 * Math.Max(0, gapY)));

            c.DrawRectangleEx(rect, 0, null, accent.WithAlpha(150));
        }
    }
}
