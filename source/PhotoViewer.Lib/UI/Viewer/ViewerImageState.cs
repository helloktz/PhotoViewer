using Avalonia;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using SkiaSharp;
using System;
using System.Threading;

namespace PhotoViewer.UI.Viewer;

public enum ViewerImageRegion
{
    WholeImage,
    Selection,
    VisibleArea,
}

public sealed class ViewerImageState : IDisposable
{
    private SKImageRef.ImageLease? _lease;
    private SKImage? _ownedImage;

    public required Photo Photo { get; init; }

    public bool IsVector { get; init; }

    public bool IsAnimated { get; init; }

    public int FrameIndex { get; init; }

    public ImageOrientation Orientation { get; init; } = ImageOrientation.Identity;

    public bool IsColorInverted { get; init; }

    public ColorChannels Channels { get; init; } = ColorChannels.RGBA;

    public Rect SelectionRect { get; init; }

    public Rect VisibleRect { get; init; }

    public bool IsPreview { get; init; }

    public HdrToneMappingOptions? HdrToneMapping { get; init; }

    public bool HasExactColorTag { get; init; } = true;

    public SKImage? Image => _ownedImage ?? _lease?.Image;

    internal ViewerImageState(SKImageRef.ImageLease? lease, SKImage? ownedImage)
    {
        _lease = lease;
        _ownedImage = ownedImage;
    }

    public SKRectI GetRegionRect(ViewerImageRegion region)
    {
        var img = Image;
        if (img.IsDisposed()) return SKRectI.Empty;

        var bounds = new Rect(0, 0, img.Width, img.Height);
        var rect = region switch
        {
            ViewerImageRegion.Selection => SelectionRect.Normalize(),
            ViewerImageRegion.VisibleArea => VisibleRect.Normalize(),
            _ => bounds,
        };

        var clipped = rect.GetIntersection(bounds);
        return clipped.IsEmpty ? SKRectI.Empty : clipped.ToSKRectI();
    }

    public SKBitmap? CopyPixels(ViewerImageRegion region = ViewerImageRegion.WholeImage)
    {
        var img = Image;
        if (img.IsDisposed()) return null;

        var rect = GetRegionRect(region);
        if (rect.IsEmpty) return null;

        var info = new SKImageInfo(rect.Width, rect.Height, img.ColorType, img.AlphaType, img.ColorSpace);
        var bmp = new SKBitmap(info);

        if (!img.ReadPixels(info, bmp.GetPixels(), bmp.RowBytes, rect.Left, rect.Top))
        {
            bmp.Dispose();
            return null;
        }

        return bmp;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _lease, null)?.Dispose();
        Interlocked.Exchange(ref _ownedImage, null)?.Dispose();
    }
}
