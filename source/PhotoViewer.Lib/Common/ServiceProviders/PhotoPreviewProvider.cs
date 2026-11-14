using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Photoing;
using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public class PhotoPreviewProvider : IPhotoPreviewProvider
{

    internal const double MIN_PREVIEW_SIZE_RATIO = 0.5;

    public virtual Task<SKImage?> TryGetCachedThumbnailAsync(string filePath, int size,
        CancellationToken token = default) => Task.FromResult<SKImage?>(null);

    public virtual async Task<SKImage?> GetPreviewAsync(PhotoMetadata meta, double? minHeight,
        CancellationToken token = default, bool skipPlatformCache = false)
    {
        var size = (int)(minHeight ?? double.MinValue);
        var isPluginFormat = IsPluginOwnedFormat(meta);
        SKImage? imgPreview = null;

        if (!isPluginFormat)
        {
            imgPreview = await Task.Run(() => SkiaCodec.LoadThumbnail(meta.FilePath, size), token)
                .ConfigureAwait(false);
        }

        if (imgPreview.IsDisposed())
        {
            using var thumbM = meta.GetEmbeddedPreview();
            if (thumbM is not null && thumbM.Height >= minHeight)
            {
                imgPreview = SkiaCodec.FromMagick(thumbM, meta.SkiaColorSpace);
            }
        }

        if (TryProcessImage(imgPreview, meta, out var imgProcessed))
        {
            imgPreview?.Dispose();
            imgPreview = imgProcessed;
        }

        if (imgPreview.IsDisposed()) imgPreview = null;
        return imgPreview;
    }

    public virtual async Task<SKImage?> GetThumbnailAsync(PhotoMetadata meta, double minHeight,
        CancellationToken token = default, bool skipPlatformCache = false)
    {
        var minSize = (int)minHeight;
        var maxSize = minSize * 2;

        var imgPreview = await GetPreviewAsync(meta, minSize, token, skipPlatformCache);
        var isPreviewLargeEnough = IsPreviewLargeEnough(imgPreview, meta, minSize);
        PhotoTrace.Mark("thumb:preview", null, $"{meta.FilePath} -> {Describe(imgPreview)}, "
            + $"meta={meta.Width}x{meta.Height}, largeEnough={isPreviewLargeEnough}");

        if (!isPreviewLargeEnough)
        {
            var codec = SelectDecodeCodec(meta);
            var isPluginCodec = CodecRegistry.IsPluginCodec(codec);
            PhotoTrace.Mark("thumb:codec", null,
                $"{meta.FilePath} -> {codec?.CodecId ?? "none"} (plugin={isPluginCodec})");

            if (isPluginCodec)
            {
                var imgPlugin = await DecodeViaCodecRegistryAsync(meta, maxSize, token, codec);
                PhotoTrace.Mark("thumb:decode", null, $"{meta.FilePath} -> plugin {Describe(imgPlugin)}");
                _ = KeepLarger(ref imgPreview, imgPlugin);
            }

            else
            {
                using var imgM = await MagickCodec.QuickDecodeAsync(meta.FilePath, maxSize, maxSize, token: token);
                var imgMagick = SkiaCodec.FromMagick(imgM, meta.SkiaColorSpace);
                PhotoTrace.Mark("thumb:decode", null, $"{meta.FilePath} -> magick {Describe(imgMagick)}");

                _ = KeepLarger(ref imgPreview, imgMagick);

                if (imgPreview.IsDisposed())
                {
                    imgPreview = await DecodeViaCodecRegistryAsync(meta, maxSize, token, codec);
                }
            }
        }

        if (minSize > 0 && (imgPreview?.Width > minSize || imgPreview?.Height > minSize))
        {
            var imgScaled = await Task.Run(() => SkiaCodec.ScaleDown(imgPreview, minSize), token)
                .ConfigureAwait(false);
            if (!imgScaled.IsDisposed())
            {
                imgPreview?.Dispose();
                imgPreview = imgScaled;
            }
        }

        if (imgPreview.IsDisposed()) imgPreview = null;
        return imgPreview;
    }

    public static bool IsPreviewLargeEnough(SKImage? img, PhotoMetadata meta, double requestedSize)
    {
        if (img.IsDisposed()) return false;
        if (requestedSize <= 0) return true;

        var wantedSize = requestedSize * MIN_PREVIEW_SIZE_RATIO;
        var srcLongestSide = (double)Math.Max(meta.Width, meta.Height);
        if (srcLongestSide > 0) wantedSize = Math.Min(wantedSize, srcLongestSide);

        var imgLongestSide = (double)Math.Max(img.Width, img.Height);
        return imgLongestSide >= wantedSize;
    }

    protected static bool KeepLarger(ref SKImage? current, SKImage? candidate)
    {
        if (candidate.IsDisposed()) return false;

        var currentSide = GetLongestSide(current);
        var candidateSide = GetLongestSide(candidate);

        if (candidateSide <= currentSide)
        {
            candidate.Dispose();
            return false;
        }

        current?.Dispose();
        current = candidate;
        return true;
    }

    protected static string Describe(SKImage? img)
        => img.IsDisposed() ? "none" : $"{img!.Width}x{img.Height}";

    private static int GetLongestSide(SKImage? img)
    {
        if (img.IsDisposed()) return 0;

        return Math.Max(img.Width, img.Height);
    }

    protected static bool IsPluginOwnedFormat(PhotoMetadata? meta)
    {
        return Core.CodecRegistry.IsDecodingExtensionOwnedByPlugin(meta?.FileExtension);
    }

    protected static ICodec? SelectDecodeCodec(PhotoMetadata meta)
    {
        var context = CreateCodecContext();
        var codec = Core.CodecRegistry.SelectDecodeCodec(meta, context);

        return codec;
    }

    private static CodecSelectionContext CreateCodecContext()
    {
        return new CodecSelectionContext
        {
            EnableVectorRenderer = Core.Config.EnableVectorRenderer,
            IsDestColorProfileSupported = Core.IsDestColorProfileSupported,
        };
    }

    protected static async Task<SKImage?> DecodeViaCodecRegistryAsync(PhotoMetadata meta,
        int maxSize, CancellationToken token, ICodec? selectedCodec = null)
    {
        var context = CreateCodecContext();
        var codec = selectedCodec ?? Core.CodecRegistry.SelectDecodeCodec(meta, context);
        if (codec is null) return null;

        var options = new PhotoReadOptions
        {
            Width = (uint)Math.Max(0, maxSize),
            Height = (uint)Math.Max(0, maxSize),
        };

        using var result = await codec.DecodeAsync(meta, options, context, token).ConfigureAwait(false);

        if (result.SingleFrame is null && result.Animator is not null)
        {
            var animatorFrame = result.Animator.GetRenderedFrameBitmap(0);
            var ownedFrame = CopyImage(animatorFrame);
            return ownedFrame;
        }

        var imgFrame = result.SingleFrame;
        result.SingleFrame = null;
        return imgFrame;
    }

    private static SKImage? CopyImage(SKImage? imgSrc)
    {
        if (imgSrc.IsDisposed()) return null;

        using var pixmap = imgSrc.PeekPixels();
        if (pixmap is null) return null;

        var imgCopy = SKImage.FromPixelCopy(pixmap);
        return imgCopy;
    }

    protected static bool TryProcessImage(SKImage? imgPreview, PhotoMetadata meta, out SKImage? output)
    {
        output = null;
        if (imgPreview.IsDisposed()) return false;

        if (SkiaCodec.TryApplyOrientation(imgPreview, meta.Orientation, out var imgOriented))
        {
            output?.Dispose();
            output = imgOriented;
        }

        if (SkiaCodec.TryApplyColorSpace(output ?? imgPreview, Core.DestColorProfile, out var imgFrameColored))
        {
            output?.Dispose();
            output = imgFrameColored;
        }

        return output is not null;
    }

}
