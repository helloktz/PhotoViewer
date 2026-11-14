using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{

    public bool EnableHdrRendering
    {
        get => GetValue(EnableHdrRenderingProperty);
        set => SetValue(EnableHdrRenderingProperty, value);
    }
    public static readonly StyledProperty<bool> EnableHdrRenderingProperty =
        AvaloniaProperty.Register<ViewerControl, bool>(nameof(EnableHdrRendering));

    public Color GetColorAt(int x, int y)
    {
        SKImageRef.ImageLease? imgLease = null;

        try
        {
            lock (_lock)
            {
                var imageRef = _imgRender ?? _imgSource;
                if (imageRef is null) return Const.COLOR_EMPTY;

                imgLease = imageRef.Acquire();
            }

            var img = imgLease?.Image;
            if (img.IsDisposed()) return Const.COLOR_EMPTY;

            if (x < 0 || x >= img.Width || y < 0 || y >= img.Height)
                return Const.COLOR_EMPTY;

            var info = new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixel = new SKBitmap(info);
            if (!img.ReadPixels(info, pixel.GetPixels(), info.RowBytes, x, y))
                return Const.COLOR_EMPTY;

            var skColor = pixel.GetPixel(0, 0);
            return new Color(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);
        }
        finally
        {
            imgLease?.Dispose();
        }
    }

    private async Task<SKImage?> ApplySkiaColorSpaceAsync(SKImage? srcImage, Photo photo)
    {
        if (srcImage.IsDisposed()) return null;

        var pass = DisplayColorPass.For(photo.Metadata, EnableHdrRendering);

        var prepared = await photo.TakeDisplayFrameAsync(srcImage, pass);
        if (prepared is not null)
        {
            PhotoTrace.Mark("viewer:display-frame", photo.FilePath, "prepared by the cache pass");
            return prepared;
        }

        if (pass.IsNoOp) return null;

        return await Task.Run(() => pass.Apply(srcImage));
    }

    public void BeginLiveHdrToneMapping()
    {
        _liveHdrToneMapping.SetTrue();

        _ = EnsureHdrSourceCapturedAsync();
    }

    public void EndLiveHdrToneMapping()
    {
        _liveHdrToneMapping.SetFalse();
        lock (_lock)
        {
            SKImageRef.Set(ref _imgHdrSource, null);
        }
    }

    public bool CanReapplyHdrToneMapping()
    {

        lock (_lock)
        {
            if (_animator is not null || IsVectorSource()) return false;
            if (Photo is not { State: PhotoState.Loaded }) return false;
            if (Photo.Metadata?.IsHdr != true) return false;

            return Photo.Metadata.HdrTransferFn != HdrTransferFunction.GainMap;
        }
    }

    public void ReapplyHdrToneMapping()
    {
        _hdrDirty = true;
        if (Interlocked.CompareExchange(ref _hdrActive, 1, 0) != 0) return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = HdrToneMapPumpAsync();
        }
        else
        {
            Dispatcher.UIThread.Post(() => _ = HdrToneMapPumpAsync());
        }
    }

    private async Task HdrToneMapPumpAsync()
    {
        try
        {
            while (_hdrDirty)
            {
                _hdrDirty = false;
                await DoOneHdrToneMapPassAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(HdrToneMapPumpAsync)}: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _hdrActive, 0);
        }

        if (_hdrDirty && Interlocked.CompareExchange(ref _hdrActive, 1, 0) == 0)
        {
            _ = HdrToneMapPumpAsync();
        }
    }

    private async Task DoOneHdrToneMapPassAsync()
    {

        for (var attempt = 0; attempt < 2; attempt++)
        {
            SKImageRef.ImageLease? lease;
            Photo? photoAtStart;
            HdrTransferFunction transferFn;
            double contentPeakNits;
            bool applyProfile;
            var destProfile = Core.DestColorProfile;

            var channels = _loadingOptions.Channels;
            var isColorInverted = IsColorInverted;
            var orientation = PhotoOrientation;
            bool hasEdits;

            lock (_lock)
            {
                if (_animator is not null || IsVectorSource()) return;
                if (Photo is not { State: PhotoState.Loaded }) return;
                if (Photo.Metadata?.IsHdr != true) return;

                lease = _imgHdrSource?.Acquire();
                photoAtStart = Photo;
                transferFn = Photo.Metadata.HdrTransferFn;
                contentPeakNits = Photo.Metadata.ContentPeakNits;
                applyProfile = CanApplySkiaColorSpace();
                hasEdits = HasImageEdits();
            }

            if (lease is null)
            {
                if (attempt == 0)
                {
                    await EnsureHdrSourceCapturedAsync().ConfigureAwait(false);
                    continue;
                }
                return;
            }

            SKImage? result = null;
            SKImage? edited = null;
            var passthrough = false;
            try
            {
                (result, passthrough) = await Task.Run(() =>
                {
                    var toneMapped = HdrToneMapper.ToneMapToSdr(lease.Image, transferFn,
                        Core.HdrToneMappingConfig, contentPeakNits);

                    if (toneMapped.IsDisposed())
                    {
                        if (applyProfile && SkiaCodec.TryApplyColorSpace(lease.Image, destProfile, out var profiledRaw))
                            return (profiledRaw, false);
                        return ((SKImage?)null, true);
                    }

                    if (applyProfile && SkiaCodec.TryApplyColorSpace(toneMapped, destProfile, out var profiled))
                    {
                        toneMapped.Dispose();
                        return (profiled, false);
                    }
                    return (toneMapped, false);
                }).ConfigureAwait(false);

                if (hasEdits)
                {
                    var shownFrame = passthrough ? lease.Image : result;
                    edited = await Task.Run(() => SkiaCodec.ApplyImageEdits(shownFrame, channels, isColorInverted, orientation))
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                lease.Dispose();
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (!ReferenceEquals(Photo, photoAtStart)
                        || _imgHdrSource is null || _imgHdrSource.Image.IsDisposed())
                    {
                        result?.Dispose();
                        edited?.Dispose();
                        return;
                    }

                    _isFirstDraw.SetTrue();
                    if (passthrough)
                    {
                        SKImageRef.Set(ref _imgSource, _imgHdrSource.Image, _imgHdrSource);
                    }
                    else if (!result.IsDisposed())
                    {
                        SKImageRef.Set(ref _imgSource, result);
                    }
                    else
                    {
                        edited?.Dispose();
                        return;
                    }

                    var isEditUnchanged = channels == _loadingOptions.Channels
                        && isColorInverted == IsColorInverted && orientation == PhotoOrientation;
                    if (!isEditUnchanged)
                    {
                        edited?.Dispose();
                        edited = null;
                    }
                    SKImageRef.Set(ref _imgRender, edited);

                    _mipmapCache?.Dispose();
                    _mipmapCache = null;
                }

                Refresh(false);
            });

            return;
        }
    }

    private async Task EnsureHdrSourceCapturedAsync()
    {
        Photo? photo;
        lock (_lock)
        {
            if (!_liveHdrToneMapping) return;
            if (_imgHdrSource?.Image.IsDisposed() == false) return;
            if (_animator is not null || IsVectorSource()) return;
            if (Photo is not { State: PhotoState.Loaded } || Photo.Metadata?.IsHdr != true) return;
            photo = Photo;
        }

        SKImage? raw = null;
        try
        {
            raw = await photo.DecodeStaticFrameAsync(0).ConfigureAwait(false);
        }
        catch
        {
        }

        if (raw.IsDisposed()) return;

        lock (_lock)
        {
            if (!_liveHdrToneMapping || !ReferenceEquals(Photo, photo)
                || _imgHdrSource?.Image.IsDisposed() == false)
            {
                raw.Dispose();
                return;
            }

            SKImageRef.Set(ref _imgHdrSource, raw);
        }
    }

    private bool CanApplySkiaColorSpace()
    {
        return DisplayColorPass.CanApplyDestProfile(Photo?.Metadata);
    }

    public bool InvertColor(bool requestRerender = true)
    {
        lock (_lock)
        {
            if (_animator is not null || IsVectorSource()) return false;

            var srcImage = (_imgRender ?? _imgSource)?.Image;
            var invertedImage = SkiaCodec.InvertImageColors(srcImage);
            if (invertedImage.IsDisposed()) return false;

            SKImageRef.Set(ref _imgRender, invertedImage);
            _mipmapCache?.Dispose();
            _mipmapCache = null;

            IsColorInverted = !IsColorInverted;
        }

        if (requestRerender)
        {
            Refresh(resetZoom: false);
        }

        return true;
    }

    public bool RotateImage(double degree, bool requestRerender = true)
    {
        lock (_lock)
        {
            if (_animator is not null || IsVectorSource() || degree % 90 != 0) return false;

            var srcImage = (_imgRender ?? _imgSource)?.Image;
            var rotatedImage = SkiaCodec.OrientImage(srcImage, ImageOrientation.Identity.Rotate((int)degree));
            if (rotatedImage.IsDisposed()) return false;

            SKImageRef.Set(ref _imgRender, rotatedImage);
            _mipmapCache?.Dispose();
            _mipmapCache = null;

            PhotoOrientation = PhotoOrientation.Rotate((int)degree);

            BitmapSize = new(rotatedImage.Width, rotatedImage.Height);
        }

        if (requestRerender)
        {
            Refresh();
        }

        return true;
    }

    public bool FlipImage(FlipOptions options, bool requestRerender = true)
    {
        lock (_lock)
        {
            if (_animator is not null || IsVectorSource()) return false;

            var srcImage = (_imgRender ?? _imgSource)?.Image;
            var flippedImage = SkiaCodec.FlipImage(srcImage, options);
            if (flippedImage.IsDisposed()) return false;

            SKImageRef.Set(ref _imgRender, flippedImage);
            _mipmapCache?.Dispose();
            _mipmapCache = null;

            PhotoOrientation = PhotoOrientation.Flip(options);
        }

        if (requestRerender)
        {
            Refresh(resetZoom: false);
        }

        return true;
    }

    public bool FilterColorChannels(ColorChannels colors, bool requestRerender = true)
    {
        lock (_lock)
        {
            if (_animator is not null || IsVectorSource()) return false;
            if (_imgSource?.Image.IsDisposed() != false) return false;

            _loadingOptions.Channels = colors;
            if (!RebuildEditedImage()) return false;
        }

        if (requestRerender)
        {
            Refresh(false);
        }

        return true;
    }

}
