using PhotoViewer.Common.Photoing;
using SkiaSharp;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public interface IPhotoPreviewProvider
{

    Task<SKImage?> TryGetCachedThumbnailAsync(string filePath, int size,
        CancellationToken token = default);

    Task<SKImage?> GetPreviewAsync(PhotoMetadata meta, double? minHeight,
        CancellationToken token = default, bool skipPlatformCache = false);

    Task<SKImage?> GetThumbnailAsync(PhotoMetadata meta, double minHeight,
        CancellationToken token = default, bool skipPlatformCache = false);

}
