using Avalonia;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using SkiaSharp;
using System;
using System.Threading.Tasks;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{

    public ImageOrientation PhotoOrientation { get; private set; } = ImageOrientation.Identity;

    private bool HasImageEdits()
    {
        if (_animator is not null || IsVectorSource()) return false;

        return !_loadingOptions.Channels.HasFlag(ColorChannels.RGBA)
            || IsColorInverted
            || !PhotoOrientation.IsIdentity;
    }

    private async Task<SKImage?> BuildEditedFrameAsync(SKImage? frame)
    {
        if (frame.IsDisposed() || !HasImageEdits()) return null;

        var channels = _loadingOptions.Channels;
        var isColorInverted = IsColorInverted;
        var orientation = PhotoOrientation;

        return await Task.Run(() => SkiaCodec.ApplyImageEdits(frame, channels, isColorInverted, orientation));
    }

    private bool RebuildEditedImage()
    {
        var srcImage = _imgSource?.Image;
        if (srcImage.IsDisposed()) return false;

        var edited = SkiaCodec.ApplyImageEdits(srcImage, _loadingOptions.Channels, IsColorInverted, PhotoOrientation);
        SKImageRef.Set(ref _imgRender, edited ?? srcImage, _imgSource);

        _mipmapCache?.Dispose();
        _mipmapCache = null;

        return true;
    }

    public ViewerImageState? CaptureImageState()
    {
        lock (_lock)
        {
            var photo = Photo;
            var lease = (_imgRender ?? _imgSource)?.Acquire();
            if (photo is null || lease is null) return null;

            SKImage? ownedFrame = null;
            if (_animator is not null)
            {
                ownedFrame = SkiaCodec.CopyImage(lease.Image);
                lease.Dispose();
                lease = null;

                if (ownedFrame is null) return null;
            }

            var showsEdits = _animator is null && !IsVectorSource();
            var toneMapping = Core.HdrToneMappingConfig;

            return new ViewerImageState(lease, ownedFrame)
            {
                Photo = photo,
                IsVector = IsVectorSource(),
                IsAnimated = _animator is not null,
                FrameIndex = Math.Max(0, photo.FrameIndex),
                Orientation = showsEdits ? PhotoOrientation : ImageOrientation.Identity,
                IsColorInverted = showsEdits && IsColorInverted,
                Channels = showsEdits ? _loadingOptions.Channels : ColorChannels.RGBA,
                SelectionRect = SourceSelection,
                VisibleRect = SrcRect,
                IsPreview = _isPreviewing,
                HdrToneMapping = EnableHdrRendering && photo.Metadata?.IsHdr == true ? toneMapping with { } : null,
                HasExactColorTag = !photo.HasBakedDisplayProfile,
            };
        }
    }

    private Size GetShownImageSize(Size fallback)
    {
        var img = (_imgRender ?? _imgSource)?.Image;
        return img.IsDisposed() ? fallback : new Size(img.Width, img.Height);
    }
}
