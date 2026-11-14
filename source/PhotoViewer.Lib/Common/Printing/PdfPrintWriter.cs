using SkiaSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Printing;

public sealed record PdfWriteOptions
{
    public string Title { get; init; } = "Photo Viewer";

    public float Dpi { get; init; } = 300;

    public int EncodingQuality { get; init; } = 95;

    public bool PortraitPagesOnly { get; init; }

    public required PrintRenderOptions Render { get; init; }
}

public static class PdfPrintWriter
{

    public static async Task WriteAsync(Stream stream, PrintDocumentLayout layout, IPrintImageSource images,
        PdfWriteOptions options, IProgress<PrintProgress>? progress, CancellationToken token)
    {
        var now = DateTime.Now;
        var metadata = new SKDocumentPdfMetadata
        {
            Title = options.Title,
            Creator = "Photo Viewer",
            Producer = "Photo Viewer",
            Creation = now,
            Modified = now,
            RasterDpi = options.Dpi,
            EncodingQuality = options.EncodingQuality,
        };

        using var doc = SKDocument.CreatePdf(stream, metadata)
            ?? throw new InvalidOperationException("The PDF backend of SkiaSharp is not available.");

        var render = options.Render with { TargetDpi = options.Dpi, IsPreview = false };
        var size = layout.PageSizePt;
        var turn = options.PortraitPagesOnly && layout.IsLandscape;

        for (var i = 0; i < layout.Pages.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new PrintProgress(i, layout.Pages.Count));

            var canvas = doc.BeginPage(turn ? size.Height : size.Width, turn ? size.Width : size.Height);
            try
            {
                if (turn)
                {
                    canvas.Translate(0, size.Width);
                    canvas.RotateDegrees(-90);
                }

                await PrintPageRenderer.DrawPageAsync(canvas, layout, i, images, render, token).ConfigureAwait(false);
            }
            finally
            {
                doc.EndPage();
            }
        }

        doc.Close();
        progress?.Report(new PrintProgress(layout.Pages.Count, layout.Pages.Count));
    }
}
