using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using SkiaSharp;
using System;

namespace PhotoViewer.Common.Windows;

public sealed class PrintPreviewControl : PhControl
{
    private const double PAPER_PADDING = 32;

    private Bitmap? _page;
    private SKSize _pageSizePt = new(595, 842);

    public PrintPreviewControl()
    {
        ClipToBounds = true;
    }

    public Rect GetPaperRect(SKSize pageSizePt)
    {
        var area = new Rect(Bounds.Size).Deflate(PAPER_PADDING);
        if (area.Width <= 0 || area.Height <= 0 || pageSizePt.Width <= 0 || pageSizePt.Height <= 0) return default;

        var scale = Math.Min(area.Width / pageSizePt.Width, area.Height / pageSizePt.Height);
        var w = pageSizePt.Width * scale;
        var h = pageSizePt.Height * scale;

        return new Rect(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);
    }

    public void SetPage(Bitmap? page, SKSize pageSizePt)
    {
        var old = _page;
        _page = page;
        _pageSizePt = pageSizePt;

        InvalidateVisual();
        old?.Dispose();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        InvalidateVisual();
    }

    public static Color GetDeskColor()
    {
        var isDark = Core.Theme.Settings.IsDarkMode;
        return AppThemeColors.BgBrush.Color.NoAlpha().WithBrightness(isDark ? 0.06f : -0.06f);
    }

    public override void Render(DrawingContext c)
    {
        base.Render(c);

        var paper = GetPaperRect(_pageSizePt);
        if (paper.Width <= 0 || paper.Height <= 0) return;

        var isDark = Core.Theme.Settings.IsDarkMode;
        var shadows = new BoxShadows(
            new BoxShadow { OffsetY = 1, Blur = 3, Color = Color.FromArgb((byte)(isDark ? 90 : 40), 0, 0, 0) },
            [new BoxShadow { OffsetY = 6, Blur = 20, Color = Color.FromArgb((byte)(isDark ? 110 : 45), 0, 0, 0) }]);

        c.DrawRectangle(Brushes.White, null, new RoundedRect(paper), shadows);
        if (_page is not null)
        {
            using (c.PushRenderOptions(new() { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            {
                c.DrawImage(_page, new Rect(_page.Size), paper);
            }
        }
    }
}
