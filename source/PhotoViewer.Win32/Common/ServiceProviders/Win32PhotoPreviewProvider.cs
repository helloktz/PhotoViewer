using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using SkiaSharp;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Win32.Common.ServiceProviders;

public class Win32PhotoPreviewProvider : PhotoPreviewProvider
{

    public override async Task<SKImage?> TryGetCachedThumbnailAsync(string filePath, int size,
        CancellationToken token = default)
    {
        if (!Core.Config.EnableGalleryShellThumbnail) return null;

        return await Task.Run(
            () => Win32ShellThumbnailApi.GetThumbnail(filePath, size, size, true), token)
            .ConfigureAwait(false);
    }

    public override async Task<SKImage?> GetPreviewAsync(PhotoMetadata meta, double? minHeight,
        CancellationToken token = default, bool skipPlatformCache = false)
    {
        if (!Core.Config.EnableGalleryShellThumbnail)
        {
            return await base.GetPreviewAsync(meta, minHeight, token, skipPlatformCache);
        }

        var size = (int)(minHeight ?? double.MinValue);
        SKImage? imgPreview = null;
        var needPreprocess = false;

        if (!skipPlatformCache)
        {
            var imgShellCached = await Task.Run(() => Win32ShellThumbnailApi.GetThumbnail(meta.FilePath, size, size, true))
                .ConfigureAwait(false);
            PhotoTrace.Mark("preview:shell-cache", null, $"{meta.FilePath} -> {Describe(imgShellCached)}");
            _ = KeepLarger(ref imgPreview, imgShellCached);
        }

        var isLargeEnough = IsPreviewLargeEnough(imgPreview, meta, size);
        var isPluginFormat = IsPluginOwnedFormat(meta);
        if (!isLargeEnough && !isPluginFormat)
        {
            var imgDecoded = await Task.Run(() => SkiaCodec.LoadThumbnail(meta.FilePath, size), token)
                .ConfigureAwait(false);
            PhotoTrace.Mark("preview:skia", null, $"{meta.FilePath} -> {Describe(imgDecoded)}");
            var useDecoded = KeepLarger(ref imgPreview, imgDecoded);
            if (useDecoded) needPreprocess = true;
        }

        isLargeEnough = IsPreviewLargeEnough(imgPreview, meta, size);
        if (!isLargeEnough)
        {
            var imgShell = await Task.Run(() => Win32ShellThumbnailApi.GetThumbnail(meta.FilePath, size, size, false))
                .ConfigureAwait(false);
            PhotoTrace.Mark("preview:shell-disk", null, $"{meta.FilePath} -> {Describe(imgShell)}");
            var useShell = KeepLarger(ref imgPreview, imgShell);
            if (useShell) needPreprocess = false;
        }

        isLargeEnough = IsPreviewLargeEnough(imgPreview, meta, size);
        if (!isLargeEnough)
        {
            using var thumbM = meta.GetEmbeddedPreview();
            if (thumbM is not null && thumbM.Height >= minHeight)
            {
                var imgEmbedded = SkiaCodec.FromMagick(thumbM, meta.SkiaColorSpace);
                var useEmbedded = KeepLarger(ref imgPreview, imgEmbedded);
                if (useEmbedded) needPreprocess = true;
            }
        }

        if (needPreprocess && TryProcessImage(imgPreview, meta, out var imgProcessed))
        {
            imgPreview?.Dispose();
            imgPreview = imgProcessed;
        }

        if (imgPreview.IsDisposed()) imgPreview = null;
        return imgPreview;
    }

}
