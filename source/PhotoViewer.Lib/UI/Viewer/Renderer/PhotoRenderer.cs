using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer.Transitions;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Threading;

namespace PhotoViewer.UI.Viewer;

public partial class PhotoRenderer : ICustomDrawOperation
{

    protected InterlockedBool _isDisposed = new(false);

    public bool IsDisposed => _isDisposed;

    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed) return;

        if (disposing)
        {
            OnDisposing();
        }

        _isDisposed.SetTrue();
    }

    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~PhotoRenderer()
    {
        Dispose(false);
    }

    private readonly Rect _bounds;
    private readonly Action<SKImage?>? _onDrawFirstTime;
    private readonly Lock _lock;
    private bool _isFirstDraw;

    private readonly SKImageRef? _imgSource;
    private readonly SKImageRef? _imgRender;
    private readonly SKRect _srcRect;
    private readonly SKRect _destRect;
    private readonly SKSamplingOptions _samplingOptions;
    private readonly MipmapTileCache? _tileCache;
    private readonly double _zoomFactor;
    private readonly ViewerControl _viewer;
    private readonly float _dpi;
    private readonly SKRect _drawingArea;

    private readonly bool _isMirror;
    private readonly ViewerControl? _mirror;
    private readonly SKSvg? _mirrorSvgDocument;

    public Rect Bounds => _bounds;

    public PhotoRenderer(ViewerControl viewer, Action<SKImage?>? processFirstDrawFn)
    {
        _lock = viewer._lock;

        lock (_lock)
        {
            _bounds = viewer.Bounds;
            _onDrawFirstTime = processFirstDrawFn;

            _imgSource = viewer._imgSource;
            _imgRender = viewer._imgRender;

            _imgSource?.KeepAlive();
            _imgRender?.KeepAlive();

            _srcRect = viewer.SrcRect.ToSKRect();
            _destRect = viewer.DestRect.ToSKRect();
            _samplingOptions = SkiaCodec.ToSamplingOptions(viewer.CurrentInterpolation);
            _tileCache = viewer._mipmapCache;
            _zoomFactor = viewer.ZoomFactor;
            _viewer = viewer;
            _dpi = (float)viewer.Dpi;
            _drawingArea = viewer.DrawingArea.ToSKRect();

            _isFirstDraw = processFirstDrawFn is not null && viewer._isFirstDraw;
            if (_isFirstDraw)
            {
                viewer._isFirstDraw.SetFalse();
            }
        }
    }

    internal PhotoRenderer(ViewerControl mirror, ViewerControl source, bool showTransforms)
    {
        _lock = source._lock;
        _viewer = source;
        _isMirror = true;
        _mirror = mirror;
        _onDrawFirstTime = null;

        _bounds = mirror.Bounds;
        _srcRect = mirror.SrcRect.ToSKRect();
        _destRect = mirror.DestRect.ToSKRect();
        _samplingOptions = SkiaCodec.ToSamplingOptions(mirror.CurrentInterpolation);
        _tileCache = mirror._mirrorSharedTileCache ?? mirror._mipmapCache;
        _zoomFactor = mirror.ZoomFactor;
        _dpi = (float)mirror.Dpi;
        _drawingArea = mirror.DrawingArea.ToSKRect();
        _mirrorSvgDocument = mirror._mirrorSvgDocument;

        lock (_lock)
        {
            if (_mirrorSvgDocument is not null) return;

            _imgSource = source._imgSource;
            _imgRender = showTransforms ? source._imgRender : null;

            _imgSource?.KeepAlive();
            _imgRender?.KeepAlive();
        }
    }

    protected virtual void OnDisposing()
    {
        ReturnFirstDrawClaim();

        _imgSource?.RequestDispose();
        _imgRender?.RequestDispose();
    }

    private void ReturnFirstDrawClaim()
    {
        lock (_lock)
        {
            if (!_isFirstDraw) return;
            _isFirstDraw = false;

            if (ReferenceEquals(_viewer._imgSource, _imgSource))
            {
                _viewer._isFirstDraw.SetTrue();
            }
        }
    }

    public bool Equals(ICustomDrawOperation? other) => false;
    public bool HitTest(Point p) => true;

    public void Render(ImmediateDrawingContext c)
    {
        var leaseFeature = c.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature is null) return;

        using var lease = leaseFeature.Lease();
        if (lease is null) return;

        lock (_lock)
        {
            var transition = (_mirror ?? _viewer)._transition;
            if (transition is null || transition.IsDisposed)
            {
                DrawContent(lease.SkCanvas, lease.GrContext);
            }
            else
            {
                RenderTransition(lease.SkCanvas, lease.GrContext, transition);
            }
        }
    }

    internal SKPicture RecordFrame()
    {
        lock (_lock)
        {
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(_drawingArea);

            DrawContent(canvas, null);

            return recorder.EndRecording();
        }
    }

    private void DrawContent(SKCanvas canvas, GRContext? grContext)
    {
        SKImageRef.ImageLease? imageLease = null;
        SKImageRef.ImageLease? srcLease = null;

        try
        {
            SKImage? imageRender;

            var svgPicture = GetVectorPicture();
            if (svgPicture is not null && !svgPicture.IsDisposed())
            {
                if (_isFirstDraw) PurgeEarlierGpuResources(grContext);

                RenderVector(canvas, svgPicture);

                if (_isFirstDraw)
                {
                    _isFirstDraw = false;

                    Dispatcher.UIThread.Post(() => _onDrawFirstTime!(null), DispatcherPriority.Send);
                }
            }
            else if (_isFirstDraw)
            {
                srcLease = _imgSource?.Acquire();
                var srcImage = srcLease?.Image;

                if (srcImage.IsDisposed())
                {
                    ReturnFirstDrawClaim();
                    return;
                }

                imageLease = _imgRender?.Acquire();
                imageRender = imageLease?.Image ?? srcImage;
                PurgeEarlierGpuResources(grContext);

                canvas.Save();
                canvas.DrawImage(imageRender, _srcRect, _destRect, _samplingOptions);
                canvas.Restore();

                _isFirstDraw = false;
                Dispatcher.UIThread.Post(() => _onDrawFirstTime!(srcImage), DispatcherPriority.Send);
            }
            else if (_tileCache?.AcquireProxy() is { } proxyLease)
            {
                using (proxyLease)
                {
                    RenderTiled(canvas, proxyLease.Image);
                }
            }
            else
            {
                imageLease = _imgRender?.Acquire() ?? _imgSource?.Acquire();
                imageRender = imageLease?.Image;

                if (imageRender is null || imageRender.IsDisposed()) return;

                canvas.Save();
                canvas.DrawImage(imageRender, _srcRect, _destRect, _samplingOptions);
                canvas.Restore();
            }
        }
        finally
        {
            imageLease?.Dispose();
            srcLease?.Dispose();
        }
    }

    private static void PurgeEarlierGpuResources(GRContext? grContext)
    {
        grContext?.PurgeUnlockedResources(false);
    }

    private SKPicture? GetVectorPicture()
    {
        if (!_isMirror) return _viewer._svgPicture;
        if (_mirrorSvgDocument is null) return null;

        var isSameDocument = ReferenceEquals(_viewer._svgDocument, _mirrorSvgDocument);
        return isSameDocument ? _viewer._svgPicture : null;
    }

    private void RenderTransition(SKCanvas canvas, GRContext? grContext, ViewerTransition transition)
    {
        if (transition.StartTime is null)
        {
            canvas.DrawPicture(transition.FromFrame);
            return;
        }

        var area = _drawingArea;
        var width = (int)Math.Ceiling(area.Width * _dpi);
        var height = (int)Math.Ceiling(area.Height * _dpi);
        var effect = transition.Request.Effect.GetEffect();

        var info = new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        using var fromSurface = effect is null ? null : CreateSurface(grContext, info);
        using var toSurface = fromSurface is null ? null : CreateSurface(grContext, info);

        if (effect is null || fromSurface is null || toSurface is null)
        {
            DrawContent(canvas, grContext);
            return;
        }

        PrepareTransitionCanvas(fromSurface.Canvas, area).DrawPicture(transition.FromFrame);

        var toCanvas = PrepareTransitionCanvas(toSurface.Canvas, area);
        DrawContent(toCanvas, grContext);

        using var fromImage = fromSurface.Snapshot();
        using var toImage = toSurface.Snapshot();

        var toLogical = SKMatrix.CreateScale(1 / _dpi, 1 / _dpi);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        using var fromShader = fromImage.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, sampling, toLogical);
        using var toShader = toImage.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, sampling, toLogical);

        using var uniforms = new SKRuntimeEffectUniforms(effect);
        if (uniforms.Contains("progress")) uniforms["progress"] = transition.Progress;
        if (uniforms.Contains("resolution")) uniforms["resolution"] = new[] { area.Width, area.Height };
        if (uniforms.Contains("navigationDirection")) uniforms["navigationDirection"] = (float)transition.Request.Direction;
        if (uniforms.Contains("ratio")) uniforms["ratio"] = area.Width / area.Height;

        using var children = new SKRuntimeEffectChildren(effect);
        if (children.Contains("fromImage")) children["fromImage"] = fromShader;
        if (children.Contains("toImage")) children["toImage"] = toShader;

        using var shader = effect.ToShader(uniforms, children);
        using var paint = new SKPaint { Shader = shader };

        canvas.Save();
        canvas.Translate(area.Left, area.Top);
        canvas.DrawRect(0, 0, area.Width, area.Height, paint);
        canvas.Restore();
    }

    private static SKSurface? CreateSurface(GRContext? grContext, SKImageInfo info)
    {
        return grContext is null
            ? SKSurface.Create(info)
            : SKSurface.Create(grContext, false, info);
    }

    private SKCanvas PrepareTransitionCanvas(SKCanvas canvas, SKRect area)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(_dpi);
        canvas.Translate(-area.Left, -area.Top);

        return canvas;
    }

    private void RenderTiled(SKCanvas canvas, SKImage proxy)
    {
        var tileCache = _tileCache!;
        var mipLevel = MipmapTileCache.GetMipLevel(_zoomFactor);
        var sourceTileSize = MipmapTileCache.GetSourceTileSize(mipLevel);

        var tileStartX = Math.Max(0, (int)(_srcRect.Left / sourceTileSize));
        var tileStartY = Math.Max(0, (int)(_srcRect.Top / sourceTileSize));
        var tileEndX = (int)Math.Ceiling(_srcRect.Right / sourceTileSize);
        var tileEndY = (int)Math.Ceiling(_srcRect.Bottom / sourceTileSize);

        var scaleX = _destRect.Width / _srcRect.Width;
        var scaleY = _destRect.Height / _srcRect.Height;

        var imageScale = (float)MipmapTileCache.TILE_SIZE / sourceTileSize;

        canvas.Save();

        var proxyScaleX = (float)proxy.Width / tileCache.SourceWidth;
        var proxyScaleY = (float)proxy.Height / tileCache.SourceHeight;
        var proxySrc = new SKRect(
            _srcRect.Left * proxyScaleX,
            _srcRect.Top * proxyScaleY,
            _srcRect.Right * proxyScaleX,
            _srcRect.Bottom * proxyScaleY);
        canvas.DrawImage(proxy, proxySrc, _destRect, _samplingOptions);

        for (var ty = tileStartY; ty < tileEndY; ty++)
        {
            for (var tx = tileStartX; tx < tileEndX; tx++)
            {
                float tileSrcLeft = tx * sourceTileSize;
                float tileSrcTop = ty * sourceTileSize;
                float tileSrcW = Math.Min(sourceTileSize, tileCache.SourceWidth - tileSrcLeft);
                float tileSrcH = Math.Min(sourceTileSize, tileCache.SourceHeight - tileSrcTop);

                var clippedLeft = Math.Max(tileSrcLeft, _srcRect.Left);
                var clippedTop = Math.Max(tileSrcTop, _srcRect.Top);
                var clippedRight = Math.Min(tileSrcLeft + tileSrcW, _srcRect.Right);
                var clippedBottom = Math.Min(tileSrcTop + tileSrcH, _srcRect.Bottom);

                if (clippedLeft >= clippedRight || clippedTop >= clippedBottom) continue;

                using var tileLease = tileCache.GetOrQueueTile(tx, ty, mipLevel);
                var imgTile = tileLease?.Image;
                if (imgTile.IsDisposed()) continue;

                var tileBitmapSrc = new SKRect(
                    (clippedLeft - tileSrcLeft) * imageScale,
                    (clippedTop - tileSrcTop) * imageScale,
                    Math.Min((clippedRight - tileSrcLeft) * imageScale, imgTile.Width),
                    Math.Min((clippedBottom - tileSrcTop) * imageScale, imgTile.Height));

                var tileDest = new SKRect(
                    _destRect.Left + (clippedLeft - _srcRect.Left) * scaleX,
                    _destRect.Top + (clippedTop - _srcRect.Top) * scaleY,
                    _destRect.Left + (clippedRight - _srcRect.Left) * scaleX,
                    _destRect.Top + (clippedBottom - _srcRect.Top) * scaleY);

                canvas.DrawImage(imgTile, tileBitmapSrc, tileDest, _samplingOptions);
            }
        }

        canvas.Restore();
    }

    private void RenderVector(SKCanvas canvas, SKPicture picture)
    {
        if (picture.IsDisposed()) return;

        var transform = ViewerControl.ComputeVectorTransform(_srcRect, _destRect);

        canvas.Save();
        canvas.ClipRect(_destRect);
        canvas.SetMatrix(canvas.TotalMatrix.PreConcat(transform));
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

}
