using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer.Transitions;
using SkiaSharp;
using Svg.Skia;
using System;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{
    private SKImageRef? _mirrorTileSource;

    internal MipmapTileCache? _mirrorSharedTileCache;

    private SKImageRef? _pendingTileSource;

    internal SKSvg? _mirrorSvgDocument;

    private const double REVEAL_MARGIN_RATIO = 0.1;

    private const double REVEAL_EASE_MS = 200;

    private Vector _revealOffset;

    private Vector _revealEaseFrom;
    private double _revealEase = 1;
    private TimeSpan? _revealEaseStart;
    private bool _isRevealEasing;

    internal event TEventHandler<ViewerControl, EventArgs>? RenderStateChanged;

    internal event TEventHandler<ViewerControl, TransitionRequest?>? TransitionStarting;

    public ViewerControl? MirrorSource
    {
        get => GetValue(MirrorSourceProperty);
        set => SetValue(MirrorSourceProperty, value);
    }
    public static readonly StyledProperty<ViewerControl?> MirrorSourceProperty =
        AvaloniaProperty.Register<ViewerControl, ViewerControl?>(nameof(MirrorSource));

    public bool EnableMirrorSync
    {
        get => GetValue(EnableMirrorSyncProperty);
        set => SetValue(EnableMirrorSyncProperty, value);
    }
    public static readonly StyledProperty<bool> EnableMirrorSyncProperty =
        AvaloniaProperty.Register<ViewerControl, bool>(nameof(EnableMirrorSync), true);

    internal Point? MirrorRevealPoint
    {
        get;
        set
        {
            if (field == value) return;

            var isToggled = field.HasValue != value.HasValue;
            field = value;

            var mustPan = MustPanForReveal(value);
            if (!mustPan) return;

            if (isToggled) StartRevealEase();
            InvalidateVisual();
        }
    }

    private void OnRenderStateChanged()
    {
        RenderStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal Point GetViewportCenter()
    {
        var imageCenter = new Point(BitmapSize.Width / 2, BitmapSize.Height / 2);
        var zoomFactor = _zooming.Factor / Dpi;
        if (zoomFactor <= 0 || DestRect.IsEmpty) return imageCenter;

        var viewportCenter = DrawingArea.Center;
        var x = SrcRect.X + (viewportCenter.X - DestRect.X) / zoomFactor;
        var y = SrcRect.Y + (viewportCenter.Y - DestRect.Y) / zoomFactor;

        return new Point(
            Math.Clamp(x, 0, BitmapSize.Width),
            Math.Clamp(y, 0, BitmapSize.Height));
    }

    private void OnMirrorSourceChanged(ViewerControl? oldSource, ViewerControl? newSource)
    {
        if (IsLoaded)
        {
            DetachMirrorSource(oldSource);
            AttachMirrorSource(newSource);
        }

        InvalidateVisual();
    }

    private void AttachMirrorSource(ViewerControl? source)
    {
        if (source is null) return;

        source.RenderStateChanged -= MirrorSource_RenderStateChanged;
        source.RenderStateChanged += MirrorSource_RenderStateChanged;
        source.TransitionStarting -= MirrorSource_TransitionStarting;
        source.TransitionStarting += MirrorSource_TransitionStarting;
    }

    private void DetachMirrorSource(ViewerControl? source)
    {
        source?.RenderStateChanged -= MirrorSource_RenderStateChanged;
        source?.TransitionStarting -= MirrorSource_TransitionStarting;

        CompleteTransition(source?._lock ?? _lock);

        SetSharedTileCache(null);
        DisposeOwnTileCache();
        _pendingTileSource = null;
        _mirrorSvgDocument = null;
    }

    private void MirrorSource_RenderStateChanged(ViewerControl sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            InvalidateVisual();
        }
        else
        {
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    private void MirrorSource_TransitionStarting(ViewerControl sender, TransitionRequest? request)
    {
        var ownRequest = request is null
            ? null
            : new TransitionRequest(request.Effect, request.DurationMs, request.Direction);

        BeginTransition(ownRequest);
    }

    private SKPicture? CaptureMirrorFrame(ViewerControl source)
    {
        SyncFromMirrorSource();

        lock (source._lock)
        {
            var hasImage = source._imgRender is not null || source._imgSource is not null;
            var hasVector = _mirrorSvgDocument is not null;
            if (!hasImage && !hasVector) return null;
            if (DrawingArea.Width < 1 || DrawingArea.Height < 1) return null;

            using var renderer = new PhotoRenderer(this, source, EnableMirrorSync);
            return renderer.RecordFrame();
        }
    }

    private void SyncFromMirrorSource()
    {
        var source = MirrorSource;
        if (source is null) return;

        DrawingArea = Bounds.Deflate(Padding);
        var showTransforms = EnableMirrorSync;

        SKSvg? svgDocument;
        Size bitmapSize;
        lock (source._lock)
        {
            svgDocument = source.IsVectorSource() ? source._svgDocument : null;
            var drawnImage = showTransforms
                ? source._imgRender ?? source._imgSource
                : source._imgSource;
            var image = drawnImage?.Image;
            var hasImage = image is not null && !image.IsDisposed();

            if (svgDocument is not null)
            {
                bitmapSize = source.BitmapSize;
            }
            else if (hasImage)
            {
                bitmapSize = new Size(image!.Width, image.Height);
            }
            else
            {
                bitmapSize = new Size();
            }

            var canTile = svgDocument is null && source._animator is null;
            UpdateMirrorTileCache(source, canTile ? drawnImage : null);
        }

        _mirrorSvgDocument = svgDocument;
        BitmapSize = bitmapSize;

        if (bitmapSize.IsEmpty || DrawingArea.IsEmpty)
        {
            CalculateDrawingRegion();
            return;
        }

        var meta = source.Photo?.Metadata;
        var isPreviewing = source._isPreviewing && meta is not null && meta.Width > 0 && meta.Height > 0;
        var fullSize = isPreviewing ? new Size(meta!.Width, meta.Height) : bitmapSize;
        var previewScale = isPreviewing ? GetPreviewToSourceScale(bitmapSize, meta!) : 1d;

        var canFollowSource = showTransforms && !source.DrawingArea.IsEmpty && !source.BitmapSize.IsEmpty;
        if (!canFollowSource)
        {
            var ownZoom = CalculateZoomFactor(ZoomMode, fullSize.Width, fullSize.Height);
            var imageCenter = new Point(bitmapSize.Width / 2, bitmapSize.Height / 2);

            SetMirrorViewport(ownZoom * previewScale, imageCenter, false);
            return;
        }

        var sourceMode = source.ZoomMode;
        var isSourceZoomFixed = source.IsManualZoom || sourceMode == ZoomMode.LockZoom;
        double fullZoom;

        if (isSourceZoomFixed)
        {
            var sourceFullZoom = source.ZoomFactor / previewScale;
            var sourceFitZoom = source.CalculateZoomFactor(ZoomMode.ScaleToFit, fullSize.Width, fullSize.Height);
            var mirrorFitZoom = CalculateZoomFactor(ZoomMode.ScaleToFit, fullSize.Width, fullSize.Height);

            fullZoom = sourceFitZoom > 0
                ? sourceFullZoom * mirrorFitZoom / sourceFitZoom
                : mirrorFitZoom;
        }
        else
        {
            fullZoom = CalculateZoomFactor(sourceMode, fullSize.Width, fullSize.Height);
        }

        var sourceCenter = source.GetViewportCenter();
        SetMirrorViewport(fullZoom * previewScale, sourceCenter, isSourceZoomFixed);
    }

    private void UpdateMirrorTileCache(ViewerControl source, SKImageRef? tileSource)
    {
        var needsTiles = MipmapTileCache.NeedsTiles(tileSource?.Image);
        if (!needsTiles)
        {
            SetSharedTileCache(null);
            DisposeOwnTileCache();
            _pendingTileSource = null;
            return;
        }

        var sourceCache = source._mipmapCache;
        var isSourceTilingIt = ReferenceEquals(sourceCache?.SourceRef, tileSource);
        if (isSourceTilingIt)
        {
            SetSharedTileCache(sourceCache);
            DisposeOwnTileCache();
            _pendingTileSource = null;
            return;
        }

        SetSharedTileCache(null);

        var hasOwnTiles = ReferenceEquals(_mirrorTileSource, tileSource);
        if (hasOwnTiles) return;

        var isFirstSeen = !ReferenceEquals(_pendingTileSource, tileSource);
        if (isFirstSeen)
        {
            DisposeOwnTileCache();
            _pendingTileSource = tileSource;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
            return;
        }

        _pendingTileSource = null;
        _mirrorTileSource = tileSource;
        _mipmapCache = MipmapTileCache.Create(tileSource, InvalidateVisual);
    }

    private void SetSharedTileCache(MipmapTileCache? cache)
    {
        var isSameCache = ReferenceEquals(_mirrorSharedTileCache, cache);
        if (isSameCache) return;

        _mirrorSharedTileCache?.RemoveViewer();
        cache?.AddViewer();
        _mirrorSharedTileCache = cache;
    }

    private void DisposeOwnTileCache()
    {
        _mipmapCache?.Dispose();
        _mipmapCache = null;
        _mirrorTileSource = null;
    }

    private void SetMirrorViewport(double factor, Point imageCenter, bool isManualZoom)
    {
        var isValidFactor = double.IsFinite(factor) && factor > 0;
        if (isValidFactor)
        {
            _zooming.Factor = Math.Clamp(factor, MinZoom, MaxZoom);
        }

        _zooming.OldFactor = _zooming.Factor;
        _zooming.ZoomedPoint = new();
        _zooming.IsManual = isManualZoom;

        var zoomFactor = _zooming.Factor / Dpi;
        var viewport = DrawingArea.Size / zoomFactor;
        var center = GetRevealedCenter(imageCenter, viewport);
        _logicalSrcPoint = new Point(
            center.X - viewport.Width / 2,
            center.Y - viewport.Height / 2);

        CalculateDrawingRegion();
    }

    private Point GetRevealedCenter(Point center, Size viewport)
    {
        var bitmapSize = BitmapSize;
        var drawnCenter = new Point(
            GetDrawnCenter(center.X, viewport.Width, bitmapSize.Width),
            GetDrawnCenter(center.Y, viewport.Height, bitmapSize.Height));

        var targetOffset = MirrorRevealPoint is { } point
            ? GetRevealOffset(drawnCenter, viewport, point)
            : new Vector();
        _revealOffset = _revealEaseFrom + (targetOffset - _revealEaseFrom) * _revealEase;

        return new Point(
            drawnCenter.X + _revealOffset.X * bitmapSize.Width,
            drawnCenter.Y + _revealOffset.Y * bitmapSize.Height);
    }

    private Vector GetRevealOffset(Point center, Size viewport, Point relativePoint)
    {
        var bitmapSize = BitmapSize;
        var margin = Math.Min(viewport.Width, viewport.Height) * REVEAL_MARGIN_RATIO;
        var shiftX = GetRevealShift(center.X, viewport.Width, bitmapSize.Width, relativePoint.X * bitmapSize.Width, margin);
        var shiftY = GetRevealShift(center.Y, viewport.Height, bitmapSize.Height, relativePoint.Y * bitmapSize.Height, margin);

        return new Vector(shiftX / bitmapSize.Width, shiftY / bitmapSize.Height);
    }

    private bool MustPanForReveal(Point? relativePoint)
    {
        var isPanned = _revealOffset != default;
        if (isPanned) return true;
        if (relativePoint is not { } point) return false;

        var zoomFactor = _zooming.Factor / Dpi;
        var hasView = zoomFactor > 0 && !BitmapSize.IsEmpty && !DrawingArea.IsEmpty;
        if (!hasView) return false;

        var viewport = DrawingArea.Size / zoomFactor;
        var offset = GetRevealOffset(SrcRect.Center, viewport, point);

        return offset != default;
    }

    private static double GetDrawnCenter(double center, double viewportLength, double imageLength)
    {
        var maxStart = imageLength - viewportLength;
        if (maxStart <= 0) return imageLength / 2;

        var start = Math.Clamp(center - viewportLength / 2, 0, maxStart);
        return start + viewportLength / 2;
    }

    private static double GetRevealShift(double center, double viewportLength, double imageLength, double point, double margin)
    {
        var maxStart = imageLength - viewportLength;
        if (maxStart <= 0) return 0;

        var start = center - viewportLength / 2;
        var revealedStart = Math.Clamp(start, point + margin - viewportLength, point - margin);
        revealedStart = Math.Clamp(revealedStart, 0, maxStart);

        return revealedStart - start;
    }

    private void StartRevealEase()
    {
        _revealEaseFrom = _revealOffset;
        _revealEase = 0;
        _revealEaseStart = null;
        if (_isRevealEasing) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            _revealEase = 1;
            return;
        }

        _isRevealEasing = true;
        topLevel.RequestAnimationFrame(OnRevealEaseFrame);
    }

    private void OnRevealEaseFrame(TimeSpan time)
    {
        _revealEaseStart ??= time;

        var progress = Math.Clamp((time - _revealEaseStart.Value).TotalMilliseconds / REVEAL_EASE_MS, 0, 1);
        _revealEase = 1 - Math.Pow(1 - progress, 3);
        InvalidateVisual();

        var isLanded = progress >= 1;
        if (isLanded)
        {
            _isRevealEasing = false;
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            _revealEase = 1;
            _isRevealEasing = false;
            return;
        }

        topLevel.RequestAnimationFrame(OnRevealEaseFrame);
    }

}
