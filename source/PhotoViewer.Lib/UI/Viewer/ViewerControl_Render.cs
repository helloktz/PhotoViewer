using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer.Checkerboard;
using SkiaSharp;
using System;
using System.Threading;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{
    private double _fps;
    private long _lastFpsTime = Environment.TickCount64;
    private TimeSpan _lastAnimationFrameTime = TimeSpan.Zero;

    internal SKImageRef? _imgSource;
    internal SKImageRef? _imgRender;
    private AnimatorImpl? _animator;
    internal MipmapTileCache? _mipmapCache;

    private SKImageRef? _imgHdrSource;
    private InterlockedBool _liveHdrToneMapping = new(false);

    private volatile bool _hdrDirty;
    private int _hdrActive;

    private RenderTargetBitmap? _bmpCheckerboard;
    private readonly CheckerboardInfo _checkerboard = new();

    internal readonly Lock _lock = new();

    public bool EnableDebug
    {
        get => GetValue(EnableDebugProperty);
        set => SetValue(EnableDebugProperty, value);
    }
    public static readonly StyledProperty<bool> EnableDebugProperty =
        AvaloniaProperty.Register<ViewerControl, bool>(nameof(EnableDebug));

    public bool EnableImagePreview { get; set; } = true;

    public Rect SrcRect { get; internal set; } = new();

    public Rect DestRect { get; internal set; } = new();

    public CheckerboardType CheckerboardMode
    {
        get => GetValue(CheckerboardModeProperty);
        set => SetValue(CheckerboardModeProperty, value);
    }
    public static readonly StyledProperty<CheckerboardType> CheckerboardModeProperty =
        AvaloniaProperty.Register<ViewerControl, CheckerboardType>(nameof(CheckerboardMode), CheckerboardType.None,
            coerce: (sender, value) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var control = (ViewerControl)sender;
                    control.DisposeCheckerboard();
                    control._checkerboard.Mode = value;
                    control.InvalidateVisual();
                });

                return value;
            });

    public ImageInterpolation InterpolationScaleDown
    {
        get => GetValue(InterpolationScaleDownProperty);
        set => SetValue(InterpolationScaleDownProperty, value);
    }
    public static readonly StyledProperty<ImageInterpolation> InterpolationScaleDownProperty =
        AvaloniaProperty.Register<ViewerControl, ImageInterpolation>(nameof(InterpolationScaleDown), ImageInterpolation.CubicMitchell, coerce: (sender, value) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var control = (ViewerControl)sender;
                control.InvalidateVisual();
            });

            return value;
        });

    public ImageInterpolation InterpolationScaleUp
    {
        get => GetValue(InterpolationScaleUpProperty);
        set => SetValue(InterpolationScaleUpProperty, value);
    }
    public static readonly StyledProperty<ImageInterpolation> InterpolationScaleUpProperty =
        AvaloniaProperty.Register<ViewerControl, ImageInterpolation>(nameof(InterpolationScaleUp), ImageInterpolation.CubicMitchell, coerce: (sender, value) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var control = (ViewerControl)sender;
                control.InvalidateVisual();
            });

            return value;
        });

    public ImageInterpolation CurrentInterpolation
    {
        get
        {
            if (ZoomFactor < 1f) return InterpolationScaleDown;
            if (ZoomFactor > 1f) return InterpolationScaleUp;

            return ImageInterpolation.Nearest;
        }
    }

    protected void DisposeCheckerboard()
    {
        _bmpCheckerboard?.Dispose();
        _bmpCheckerboard = null;
    }

    public override void Render(DrawingContext c)
    {
        if (MirrorSource is not null) SyncFromMirrorSource();

        base.Render(c);

        using (c.PushClip(DrawingArea))
        {
            OnDrawCheckerboard(c);
            OnDrawImage(c);
            OnDrawSelection(c);
        }

        OnDrawDebugInfo(c);
    }

    protected virtual void OnDrawDebugInfo(DrawingContext c)
    {
        if (!EnableDebug) return;

        var cacheIndexes = Core.Photos.CachedIndexSnapshot;
        var cacheIndexStr = cacheIndexes.Length > 0
            ? string.Join(", ", cacheIndexes)
            : "(none)";

        var text = $"""
            DPI = {Dpi}, FPS = {_fps}
            Control Bounds = {Bounds}
            DrawingArea = {DrawingArea}
            Image size = {BitmapSize}
            Use MipMap = {_mipmapCache != null}
            _isPreviewing = {_isPreviewing}
            _srcRect = {SrcRect}
            _destRect = {DestRect}
            _zoomFactor = {_zooming.Factor}
            _zoomedPoint = {_zooming.ZoomedPoint}

            SourceRect = {_selection.SourceRect}
            ClientSelection = {ClientSelection}

            Cache: {Core.Photos.CachedCount} photos, {Core.Photos.CachedMemoryMb} MB
            Cached indexes = [{cacheIndexStr}]
            """;

        var textLayout = new TextLayout(text, new Typeface(Const.FONT_CODE), FontSize, Brushes.HotPink);
        var textOrigin = new Point(DrawingArea.X + 20, DrawingArea.Y + 20);
        for (int i = 0; i < textLayout.TextLines.Count; i++)
        {
            var line = textLayout.TextLines[i];
            var bounds = line.GetTextBounds(line.FirstTextSourceIndex, line.Length);
            if (bounds.Count == 0) continue;

            var lineRect = bounds[0].Rectangle;
            lineRect = lineRect.WithX(lineRect.X + textOrigin.X).WithY(lineRect.Y + line.Height * i + textOrigin.Y);

            c.FillRectangle(Colors.Black.A(180).ToBrush(), lineRect);
        }

        textLayout.Draw(c, textOrigin);

        c.DrawRectangle(new Pen(Brushes.LightGreen, 2, DashStyle.Dash, PenLineCap.Round, PenLineJoin.Round), DestRect);

        c.DrawEllipse(Brushes.Red, new Pen(Brushes.White, 2), _zooming.ZoomedPoint, 5, 5);
    }

    protected virtual void OnDrawCheckerboard(DrawingContext c)
    {
        if (CheckerboardMode == CheckerboardType.None) return;

        Rect region;
        if (CheckerboardMode == CheckerboardType.Image)
        {
            region = DestRect;
        }
        else
        {
            region = DrawingArea;
        }

        var cellW = (int)(_checkerboard.Size.Width * Dpi);
        var cellH = (int)(_checkerboard.Size.Height * Dpi);
        var pxSize = new PixelSize(cellW * 2, cellH * 2);
        _bmpCheckerboard ??= new RenderTargetBitmap(pxSize);

        using (var ctx = _bmpCheckerboard.CreateDrawingContext())
        {
            var brushX = new ImmutableSolidColorBrush(Colors.Black);
            var brushO = new ImmutableSolidColorBrush(Colors.White);

            ctx.FillRectangle(brushX, new Rect(0, 0, cellW, cellH));
            ctx.FillRectangle(brushX, new Rect(cellW, cellH, cellW, cellH));

            ctx.FillRectangle(brushO, new Rect(cellW, 0, cellW, cellH));
            ctx.FillRectangle(brushO, new Rect(0, cellH, cellW, cellH));
        }

        var imgBrush = new ImageBrush
        {
            Source = _bmpCheckerboard,
            Stretch = Stretch.Fill,
            TileMode = TileMode.Tile,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            DestinationRect = new(0, 0, cellW, cellH, RelativeUnit.Absolute),
            Opacity = 0.1,
        };

        using (c.PushRenderOptions(new() { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            c.FillRectangle(imgBrush, region);
        }
    }

    protected virtual void OnDrawImage(DrawingContext c)
    {
        var renderer = MirrorSource is { } source
            ? new PhotoRenderer(this, source, EnableMirrorSync)
            : new PhotoRenderer(this, OnDrawnImageFirstTime);

        c.Custom(renderer);
    }

    protected void OnDrawnImageFirstTime(SKImage? img)
    {
        Size? refitSize = null;

        lock (_lock)
        {
            var isCurrent = img is not null && ReferenceEquals(img, _imgSource?.Image);
            var imgSize = isCurrent && !img.IsDisposed() ? $"{img.Width}x{img.Height}" : "0x0";
            PhotoTrace.Mark("render:first-draw", Photo?.FilePath,
                $"{imgSize}, vector={IsVectorSource()}");

            _firstDrawTcs?.TrySetResult();

            if (IsVectorSource() || !isCurrent)
            {
                InvalidateVisual();
                OnRenderStateChanged();
                return;
            }

            if (_imgRender is null)
            {
                SKImageRef.Set(ref _imgRender, img, _imgSource);
                if (HasImageEdits() && RebuildEditedImage())
                {
                    refitSize = GetShownImageSize(BitmapSize);
                }
            }

            CreateMipmapTileCache();

            InvalidateVisual();
            OnRenderStateChanged();
        }

        if (refitSize is { } size) RefitForFrameSize(size);
    }

    protected virtual void OnDrawAnimationSource(double eslapseDelta)
    {
        var panDelta = eslapseDelta * 30;
        var zoomDelta = eslapseDelta * 1000;

        if (AnimationSource.HasFlag(AnimationSources.PanLeft))
        {
            PanLeft(panDelta, requestRerender: false);
        }
        else if (AnimationSource.HasFlag(AnimationSources.PanRight))
        {
            PanRight(panDelta, requestRerender: false);
        }

        if (AnimationSource.HasFlag(AnimationSources.PanUp))
        {
            PanUp(panDelta, requestRerender: false);
        }
        else if (AnimationSource.HasFlag(AnimationSources.PanDown))
        {
            PanDown(panDelta, requestRerender: false);
        }

        if (AnimationSource.HasFlag(AnimationSources.ZoomIn))
        {
            if (_zooming.ZoomedPoint.X == 0 && _zooming.ZoomedPoint.Y == 0)
            {
                _zooming.ZoomedPoint = DestRect.Center;
            }
            _ = ZoomByDeltaToPoint(zoomDelta, _zooming.ZoomedPoint, requestRerender: false);
        }
        else if (AnimationSource.HasFlag(AnimationSources.ZoomOut))
        {
            if (_zooming.ZoomedPoint.X == 0 && _zooming.ZoomedPoint.Y == 0)
            {
                _zooming.ZoomedPoint = DestRect.Center;
            }
            _ = ZoomByDeltaToPoint(-zoomDelta, _zooming.ZoomedPoint, requestRerender: false);
        }

    }

    private void CalculateFps()
    {
        if (!EnableDebug) return;

        var now = Environment.TickCount64;
        var delta = now - _lastFpsTime;

        if (delta > 0)
        {
            _fps = _fps * 0.9 + (1000.0 / delta) * 0.1;
            _fps = Math.Round(_fps, 2);
        }

        _lastFpsTime = now;
    }

    private void CreateMipmapTileCache()
    {
        _mipmapCache?.Dispose();
        _mipmapCache = null;

        if (_animator is not null) return;

        _mipmapCache = MipmapTileCache.Create(_imgRender ?? _imgSource, InvalidateTiledFrame);

        PhotoTrace.Mark("render:mipmap-cache", Photo?.FilePath,
            _mipmapCache is null ? "not created (image too small)" : "created");
    }

    private void InvalidateTiledFrame()
    {
        InvalidateVisual();
        OnRenderStateChanged();
    }

}
