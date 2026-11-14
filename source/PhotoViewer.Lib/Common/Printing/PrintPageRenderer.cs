using PhotoViewer.Common.Extensions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Printing;

public sealed record PrintRenderOptions
{
    public PrintFitMode Fit { get; init; } = PrintFitMode.Fit;
    public bool AutoRotate { get; init; } = true;
    public PrintColorMode ColorMode { get; init; } = PrintColorMode.Color;

    public float TargetDpi { get; init; } = 300;

    public float ImageDpi { get; init; } = 96;

    public bool IsPreview { get; init; }
}

public static class PrintPageRenderer
{
    private const float CAPTION_FONT_PT = 7.5f;
    private const float EMPTY_CELL_DASH = 4;
    private const float EMPTY_CELL_GAP = 3;

    private const float MIN_RASTER_DPI = 150;
    private const float MAX_PHOTO_RASTER_DPI = 300;

    private static readonly float[] _grayscaleMatrix =
    [
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0, 0, 0, 1, 0,
    ];

    public static async Task DrawPageAsync(SKCanvas canvas, PrintDocumentLayout layout, int pageIndex,
        IPrintImageSource images, PrintRenderOptions options, CancellationToken token)
    {
        if (pageIndex < 0 || pageIndex >= layout.Pages.Count) return;

        var page = layout.Pages[pageIndex];
        var pageRect = SKRect.Create(layout.PageSizePt);

        using (var paper = new SKPaint { Color = SKColors.White })
        {
            canvas.DrawRect(pageRect, paper);
        }

        if (options.IsPreview)
        {
            DrawUnprintableBand(canvas, pageRect, layout.PrintableRectPt);
            DrawEmptyCells(canvas, layout, page);
        }

        using var filter = options.ColorMode == PrintColorMode.Grayscale ? SKColorFilter.CreateColorMatrix(_grayscaleMatrix) : null;
        var clip = canvas.LocalClipBounds;

        foreach (var cell in page.Cells)
        {
            token.ThrowIfCancellationRequested();
            if (!cell.RectPt.IntersectsWith(clip)) continue;

            await DrawCellAsync(canvas, cell, images, options, filter, token).ConfigureAwait(false);
            if (cell.CaptionRectPt is { } captionRect) DrawCaption(canvas, captionRect, images.GetCaption(cell.ItemIndex));
        }
    }

    public static async Task<float> GetCellRasterDpiAsync(PrintCell cell, IPrintImageSource images,
        PrintRenderOptions options, float maxDpi, CancellationToken token)
    {
        var picture = images.GetPicture(cell.ItemIndex);
        if (picture is not null && !picture.IsDisposed()) return maxDpi;

        using var lease = await images.GetImageAsync(cell.ItemIndex, GetRequestLongSide(cell, options), token).ConfigureAwait(false);
        var img = lease?.Image;
        var ceiling = Math.Min(maxDpi, MAX_PHOTO_RASTER_DPI);
        if (img.IsDisposed()) return ceiling;

        var (dest, _) = PrintLayoutEngine.PlaceImage(cell.ImageRectPt, new SKSize(img.Width, img.Height), options.Fit, options.AutoRotate, options.ImageDpi);
        var destLongSidePt = Math.Max(dest.Width, dest.Height);
        if (destLongSidePt <= 0) return ceiling;

        var imageDpi = Math.Max(img.Width, img.Height) / destLongSidePt * PrintUnits.POINTS_PER_INCH;
        return Math.Clamp(imageDpi, Math.Min(MIN_RASTER_DPI, ceiling), ceiling);
    }

    private static int GetRequestLongSide(PrintCell cell, PrintRenderOptions options)
    {
        if (options.Fit == PrintFitMode.ActualSize) return int.MaxValue;

        var longSidePt = Math.Max(cell.ImageRectPt.Width, cell.ImageRectPt.Height);
        return (int)Math.Ceiling(PrintUnits.PtToPx(longSidePt, options.TargetDpi) * (options.Fit == PrintFitMode.Fill ? 1.5f : 1f));
    }

    private static async Task DrawCellAsync(SKCanvas canvas, PrintCell cell, IPrintImageSource images,
        PrintRenderOptions options, SKColorFilter? filter, CancellationToken token)
    {
        using var paint = new SKPaint { ColorFilter = filter };

        var picture = images.GetPicture(cell.ItemIndex);
        if (picture is not null && !picture.IsDisposed())
        {
            var size = picture.CullRect.Size;
            var (dest, rotate) = PrintLayoutEngine.PlaceImage(cell.ImageRectPt, size, options.Fit, options.AutoRotate, 96);

            canvas.Save();
            canvas.ClipRect(cell.ImageRectPt);
            ApplyPlacement(canvas, dest, rotate, size);
            canvas.Translate(-picture.CullRect.Left, -picture.CullRect.Top);
            canvas.DrawPicture(picture, paint);
            canvas.Restore();
            return;
        }

        var longSide = GetRequestLongSide(cell, options);
        using var lease = options.IsPreview
            ? images.TryGetImage(cell.ItemIndex, longSide)
            : await images.GetImageAsync(cell.ItemIndex, longSide, token).ConfigureAwait(false);

        var img = lease?.Image;
        if (img.IsDisposed())
        {
            if (options.IsPreview) DrawPlaceholder(canvas, cell.ImageRectPt);
            return;
        }

        var imageSize = new SKSize(img.Width, img.Height);
        var (imageDest, turn) = PrintLayoutEngine.PlaceImage(cell.ImageRectPt, imageSize, options.Fit, options.AutoRotate, options.ImageDpi);

        canvas.Save();
        canvas.ClipRect(cell.ImageRectPt);
        ApplyPlacement(canvas, imageDest, turn, imageSize);
        canvas.DrawImage(img, SKRect.Create(imageSize), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        canvas.Restore();
    }

    private static void ApplyPlacement(SKCanvas canvas, SKRect dest, bool rotate, SKSize size)
    {
        var drawW = rotate ? dest.Height : dest.Width;
        var drawH = rotate ? dest.Width : dest.Height;

        canvas.Translate(dest.MidX, dest.MidY);
        if (rotate) canvas.RotateDegrees(-90);
        canvas.Translate(-drawW / 2, -drawH / 2);
        canvas.Scale(drawW / size.Width, drawH / size.Height);
    }

    private static void DrawCaption(SKCanvas canvas, SKRect rect, PrintCaption caption)
    {
        if (string.IsNullOrEmpty(caption.Name) || rect.Width <= 0) return;

        using var fonts = new CaptionFonts();
        var runs = fonts.Split(FitCaption(fonts, caption, rect.Width - 4));
        var width = runs.Sum(i => i.Font.MeasureText(i.Text));

        using var paint = new SKPaint { Color = new SKColor(0x40, 0x40, 0x40), IsAntialias = true };
        var x = rect.MidX - width / 2;
        var y = rect.MidY + CAPTION_FONT_PT * 0.35f;

        foreach (var (runText, font) in runs)
        {
            using var path = font.GetTextPath(runText, new SKPoint(x, y));
            canvas.DrawPath(path, paint);
            x += font.MeasureText(runText);
        }
    }

    private static string FitCaption(CaptionFonts fonts, PrintCaption caption, float maxWidth)
    {
        var text = caption.ToString();
        if (string.IsNullOrEmpty(caption.Page) || fonts.Measure(text) <= maxWidth) return Ellipsize(fonts, text, maxWidth);

        var page = $" {caption.Page}";
        var nameWidth = maxWidth - fonts.Measure(page);
        if (nameWidth < fonts.Measure("…")) return Ellipsize(fonts, text, maxWidth);

        return Ellipsize(fonts, caption.Name, nameWidth) + page;
    }

    private static string Ellipsize(CaptionFonts fonts, string text, float maxWidth)
    {
        if (fonts.Measure(text) <= maxWidth) return text;

        var starts = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = starts.Length - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (fonts.Measure(text[..starts[mid]] + "…") <= maxWidth) low = mid;
            else high = mid - 1;
        }

        return low == 0 ? "…" : text[..starts[low]] + "…";
    }

    private sealed class CaptionFonts : IDisposable
    {
        private readonly SKFont _default = new(SKTypeface.Default, CAPTION_FONT_PT);
        private readonly List<(SKTypeface Typeface, SKFont Font)> _fallbacks = [];

        public List<(string Text, SKFont Font)> Split(string text)
        {
            var runs = new List<(string, SKFont)>();
            var run = new StringBuilder();
            SKFont? runFont = null;

            foreach (var rune in text.EnumerateRunes())
            {
                var font = GetFont(rune.Value);
                if (runFont is not null && !ReferenceEquals(font, runFont))
                {
                    runs.Add((run.ToString(), runFont));
                    run.Clear();
                }

                runFont = font;
                run.Append(rune.ToString());
            }

            if (runFont is not null && run.Length > 0) runs.Add((run.ToString(), runFont));
            return runs;
        }

        public float Measure(string text) => Split(text).Sum(i => i.Font.MeasureText(i.Text));

        private SKFont GetFont(int codepoint)
        {
            if (_default.GetGlyph(codepoint) != 0) return _default;

            foreach (var (_, font) in _fallbacks)
            {
                if (font.GetGlyph(codepoint) != 0) return font;
            }

            var typeface = SKFontManager.Default.MatchCharacter(codepoint);
            if (typeface is null) return _default;

            var fallback = new SKFont(typeface, CAPTION_FONT_PT);
            _fallbacks.Add((typeface, fallback));
            return fallback;
        }

        public void Dispose()
        {
            _default.Dispose();
            foreach (var (typeface, font) in _fallbacks)
            {
                font.Dispose();
                typeface.Dispose();
            }
        }
    }

    private static void DrawUnprintableBand(SKCanvas canvas, SKRect page, SKRect printable)
    {
        if (printable.Contains(page) || printable.IsEmpty) return;

        using var paint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 28),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true,
        };

        canvas.Save();
        canvas.ClipRect(page);
        canvas.ClipRect(printable, SKClipOperation.Difference);
        for (var x = page.Left - page.Height; x < page.Right; x += 6)
        {
            canvas.DrawLine(x, page.Bottom, x + page.Height, page.Top, paint);
        }
        canvas.Restore();
    }

    private static void DrawEmptyCells(SKCanvas canvas, PrintDocumentLayout layout, PrintPage page)
    {
        if (page.Cells.Count >= layout.CellRectsPt.Count) return;

        var edges = new HashSet<(float X0, float Y0, float X1, float Y1)>();
        for (var i = page.Cells.Count; i < layout.CellRectsPt.Count; i++)
        {
            var r = layout.CellRectsPt[i];
            var (left, top, right, bottom) = (MathF.Round(r.Left, 2), MathF.Round(r.Top, 2), MathF.Round(r.Right, 2), MathF.Round(r.Bottom, 2));

            edges.Add((left, top, right, top));
            edges.Add((left, bottom, right, bottom));
            edges.Add((left, top, left, bottom));
            edges.Add((right, top, right, bottom));
        }

        using var paint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 60),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.75f,
            IsAntialias = true,
        };

        foreach (var (x0, y0, x1, y1) in edges)
        {
            var start = y0 == y1 ? x0 : y0;
            using var dash = SKPathEffect.CreateDash([EMPTY_CELL_DASH, EMPTY_CELL_GAP], start % (EMPTY_CELL_DASH + EMPTY_CELL_GAP));

            paint.PathEffect = dash;
            canvas.DrawLine(x0, y0, x1, y1, paint);
        }
    }

    private static void DrawPlaceholder(SKCanvas canvas, SKRect rect)
    {
        using var paint = new SKPaint { Color = new SKColor(0xE8, 0xE8, 0xE8) };
        canvas.DrawRect(rect, paint);
    }
}
