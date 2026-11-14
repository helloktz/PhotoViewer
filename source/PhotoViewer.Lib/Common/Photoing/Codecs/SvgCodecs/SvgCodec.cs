using Avalonia;
using SkiaSharp;
using Svg;
using Svg.Model;
using Svg.Skia;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public static class SvgCodec
{
    internal static SvgParameters LocalResources { get; } = new(null, null, null,
        new SvgDocumentLoadOptions
        {
            ExternalResources = SvgExternalResourcePolicy.SameOrigin,
        });

    private static readonly string[] _svgExtensions = [".svg", ".svgz"];

    public static bool IsSvgFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        var ext = Path.GetExtension(filePath);
        foreach (var svgExt in _svgExtensions)
        {
            if (ext.Equals(svgExt, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "SVG parsing requires reflection for element discovery; rendering is AOT-safe.")]
    public static SKSvg LoadSvg(string filePath)
    {
        return SKSvg.CreateFromFile(filePath, LocalResources);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "SVG parsing requires reflection for element discovery; rendering is AOT-safe.")]
    public static SKSvg LoadSvg(Stream stream)
    {
        return SKSvg.CreateFromStream(stream, LocalResources);
    }

    public static Size GetIntrinsicSize(SKPicture picture)
    {
        var cullRect = picture.CullRect;
        return new Size(
            Math.Max(1, cullRect.Width),
            Math.Max(1, cullRect.Height));
    }

    public static SKImage? RasterizeThumbnail(SKPicture picture, int maxDimension)
    {
        var cullRect = picture.CullRect;
        var w = cullRect.Width;
        var h = cullRect.Height;
        if (w <= 0 || h <= 0) return null;

        var scale = Math.Min((float)maxDimension / w, (float)maxDimension / h);
        scale = Math.Min(scale, 1f);

        var targetW = (int)Math.Ceiling(w * scale);
        var targetH = (int)Math.Ceiling(h * scale);
        if (targetW <= 0 || targetH <= 0) return null;

        var info = new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null) return null;

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (cullRect.Left != 0 || cullRect.Top != 0)
        {
            canvas.Translate(-cullRect.Left * scale, -cullRect.Top * scale);
        }

        canvas.Scale(scale, scale);
        canvas.DrawPicture(picture);

        return surface.Snapshot();
    }

    public static async Task<PhotoMetadata> LoadMetadataAsync(string filePath, CancellationToken token = default)
    {
        var meta = new PhotoMetadata(filePath)
        {
            IsVector = true,
            FrameCount = 1,
            HasAlpha = true,
        };

        await Task.Run(() =>
        {
            try
            {
                using var svgDoc = LoadSvg(filePath);
                var picture = svgDoc.Picture;
                if (picture is not null)
                {
                    var size = GetIntrinsicSize(picture);
                    meta.OriginalWidth = meta.Width = (uint)size.Width;
                    meta.OriginalHeight = meta.Height = (uint)size.Height;
                }

                if (svgDoc.HasAnimations)
                {
                    meta.CanAnimate = true;
                }
            }
            catch { }
        }, token).ConfigureAwait(false);

        return meta;
    }
}
