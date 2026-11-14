
using Avalonia;
using Avalonia.Threading;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer.ZoomAndPan;
using System;
using System.Linq;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{
    private readonly ZoomInfo _zooming = new();
    private bool _enablePanningVelocity = true;

    private Point _logicalSrcPoint = new();

    public event TEventHandler<ViewerControl, ViewerZoomEventArgs>? ZoomChanged;

    public event TEventHandler<ViewerControl, ViewerPanEventArgs>? Panning;

    public ZoomMode ZoomMode
    {
        get => GetValue(ZoomModeProperty);
        set => SetValue(ZoomModeProperty, value);
    }
    public static readonly StyledProperty<ZoomMode> ZoomModeProperty =
        AvaloniaProperty.Register<ViewerControl, ZoomMode>(nameof(ZoomMode), ZoomMode.AutoZoom,
            coerce: (sender, value) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var control = (ViewerControl)sender;
                    control._zooming.Mode = value;
                    control.Refresh();
                });

                return value;
            });

    public double ZoomFactor
    {
        get => _zooming.Factor;
        set => SetZoomFactor(value, true);
    }

    public double MinZoom
    {
        get
        {
            if (ZoomLevels.Length > 0) return ZoomLevels[0];
            return _zooming.Min;
        }
        set => _zooming.Min = Math.Min(Math.Max(0.001f, value), 1000);
    }

    public double MaxZoom
    {
        get
        {
            if (ZoomLevels.Length > 0) return ZoomLevels[^1];
            return _zooming.Max;
        }
        set => _zooming.Max = Math.Min(Math.Max(0.001f, value), 1000);
    }

    public double[] ZoomLevels
    {
        get => GetValue(ZoomLevelsProperty);
        set => SetValue(ZoomLevelsProperty, value);
    }
    public static readonly StyledProperty<double[]> ZoomLevelsProperty =
        AvaloniaProperty.Register<ViewerControl, double[]>(nameof(ZoomLevels), [],
            coerce: (_, value) => (value ?? [])
                .OrderBy(x => x)
                .Where(i => i > 0)
                .Distinct()
                .ToArray());

    public double ZoomSpeed
    {
        get => GetValue(ZoomSpeedProperty);
        set => SetValue(ZoomSpeedProperty, value);
    }
    public static readonly StyledProperty<double> ZoomSpeedProperty =
        AvaloniaProperty.Register<ViewerControl, double>(nameof(ZoomSpeed), 0d,
            coerce: (_, value) => Math.Clamp(value, -ZoomInfo.MAX_ZOOM_SPEED, ZoomInfo.MAX_ZOOM_SPEED));

    public bool IsManualZoom => _zooming.IsManual;

    public double PanSpeed
    {
        get => GetValue(PanSpeedProperty);
        set => SetValue(PanSpeedProperty, value);
    }
    public static readonly StyledProperty<double> PanSpeedProperty =
        AvaloniaProperty.Register<ViewerControl, double>(nameof(PanSpeed), 20d,
            coerce: (_, value) => Math.Max(value, 0));

    public bool IsWindowFitMode
    {
        get => GetValue(IsWindowFitModeProperty);
        set => SetValue(IsWindowFitModeProperty, value);
    }
    public static readonly StyledProperty<bool> IsWindowFitModeProperty =
        AvaloniaProperty.Register<ViewerControl, bool>(nameof(IsWindowFitMode));

    public bool EnableFreePan
    {
        get => GetValue(EnableFreePanProperty);
        set => SetValue(EnableFreePanProperty, value);
    }
    public static readonly StyledProperty<bool> EnableFreePanProperty =
        AvaloniaProperty.Register<ViewerControl, bool>(nameof(EnableFreePan));

    public bool CanUseFreePan => EnableFreePan && !IsWindowFitMode;

    public double PanMargin
    {
        get => GetValue(PanMarginProperty);
        set => SetValue(PanMarginProperty, value);
    }
    public static readonly StyledProperty<double> PanMarginProperty =
        AvaloniaProperty.Register<ViewerControl, double>(nameof(PanMargin), 30);

    public virtual void CalculateDrawingRegion()
    {
        if (DrawingArea.IsEmpty || BitmapSize.IsEmpty)
        {
            SrcRect = new();
            DestRect = new();
            _logicalSrcPoint = new();
            UpdateScrollbars();
            OnRenderStateChanged();
            return;
        }

        var dpi = Dpi;

        var currentZoomFactor = _zooming.Factor / dpi;
        var oldZoomFactor = _zooming.OldFactor / dpi;

        var zoomX = _zooming.ZoomedPoint.X - Padding.Left;
        var zoomY = _zooming.ZoomedPoint.Y - Padding.Top;

        var controlW = DrawingArea.Width;
        var controlH = DrawingArea.Height;

        var scaledImgWidth = BitmapSize.Width * currentZoomFactor;
        var scaledImgHeight = BitmapSize.Height * currentZoomFactor;

        var isZoomingToPoint = currentZoomFactor != oldZoomFactor && _zooming.ZoomedPoint != default;

        var srcX = _logicalSrcPoint.X;
        var srcY = _logicalSrcPoint.Y;
        var srcWidth = SrcRect.Width;
        var srcHeight = SrcRect.Height;

        var destX = DestRect.X;
        var destY = DestRect.Y;
        var destWidth = DestRect.Width;
        var destHeight = DestRect.Height;

        if (scaledImgWidth <= controlW)
        {
            srcX = 0;
            srcWidth = BitmapSize.Width;
            destWidth = scaledImgWidth;

            if (CanUseFreePan)
            {
                if (isZoomingToPoint)
                {
                    var screenZoomX = zoomX + DrawingArea.Left;
                    var imgX = SrcRect.X + (screenZoomX - DestRect.X) / oldZoomFactor;
                    imgX = Math.Clamp(imgX, 0, BitmapSize.Width);
                    destX = screenZoomX - imgX * currentZoomFactor;
                }
                else
                {
                    var maxPanScreenX = (controlW - scaledImgWidth) / 2.0;
                    var panOffsetX = Math.Clamp(_logicalSrcPoint.X * currentZoomFactor, -maxPanScreenX, maxPanScreenX);
                    destX = (controlW - scaledImgWidth) / 2.0 + DrawingArea.Left - panOffsetX;
                }
            }
            else
            {
                destX = (controlW - scaledImgWidth) / 2.0 + DrawingArea.Left;
            }
        }
        else
        {
            srcWidth = controlW / currentZoomFactor;

            if (isZoomingToPoint)
            {
                var screenZoomX = zoomX + DrawingArea.Left;
                var rawImgX = SrcRect.X + (screenZoomX - DestRect.X) / oldZoomFactor;
                var imgX = Math.Clamp(rawImgX, 0, BitmapSize.Width);

                if (!CanUseFreePan && rawImgX != imgX)
                {
                    var edgeScreenX = rawImgX < 0
                        ? DestRect.X
                        : DestRect.X + DestRect.Width;
                    srcX = imgX - (edgeScreenX - DrawingArea.Left) / currentZoomFactor;
                }
                else
                {
                    srcX = imgX - zoomX / currentZoomFactor;
                }
            }

            destX = DrawingArea.Left;
            destWidth = controlW;
        }

        if (scaledImgHeight <= controlH)
        {
            srcY = 0;
            srcHeight = BitmapSize.Height;
            destHeight = scaledImgHeight;

            if (CanUseFreePan)
            {
                if (isZoomingToPoint)
                {
                    var screenZoomY = zoomY + DrawingArea.Top;
                    var imgY = SrcRect.Y + (screenZoomY - DestRect.Y) / oldZoomFactor;
                    imgY = Math.Clamp(imgY, 0, BitmapSize.Height);
                    destY = screenZoomY - imgY * currentZoomFactor;
                }
                else
                {
                    var maxPanScreenY = (controlH - scaledImgHeight) / 2.0;
                    var panOffsetY = Math.Clamp(_logicalSrcPoint.Y * currentZoomFactor, -maxPanScreenY, maxPanScreenY);
                    destY = (controlH - scaledImgHeight) / 2.0 + DrawingArea.Top - panOffsetY;
                }
            }
            else
            {
                destY = (controlH - scaledImgHeight) / 2.0 + DrawingArea.Top;
            }
        }
        else
        {
            srcHeight = controlH / currentZoomFactor;

            if (isZoomingToPoint)
            {
                var screenZoomY = zoomY + DrawingArea.Top;
                var rawImgY = SrcRect.Y + (screenZoomY - DestRect.Y) / oldZoomFactor;
                var imgY = Math.Clamp(rawImgY, 0, BitmapSize.Height);

                if (!CanUseFreePan && rawImgY != imgY)
                {
                    var edgeScreenY = rawImgY < 0
                        ? DestRect.Y
                        : DestRect.Y + DestRect.Height;
                    srcY = imgY - (edgeScreenY - DrawingArea.Top) / currentZoomFactor;
                }
                else
                {
                    srcY = imgY - zoomY / currentZoomFactor;
                }
            }

            destY = DrawingArea.Top;
            destHeight = controlH;
        }

        var panMargin = IsWindowFitMode ? 0 : PanMargin;
        var panMarginSrc = DpiScale(panMargin) / currentZoomFactor;
        var skipClampWhileZooming = isZoomingToPoint && CanUseFreePan;

        var wasWidthFitting = BitmapSize.Width * oldZoomFactor <= controlW;
        var wasHeightFitting = BitmapSize.Height * oldZoomFactor <= controlH;

        if (scaledImgWidth > controlW && !skipClampWhileZooming)
        {
            var baseMarginX = isZoomingToPoint && wasWidthFitting ? 0 : panMarginSrc;

            var effectiveLeftMarginX = baseMarginX;
            var effectiveRightMarginX = baseMarginX;
            if (CanUseFreePan)
            {
                var prevLeftGap = Math.Max(0, DestRect.X - DrawingArea.Left) / currentZoomFactor;
                var prevRightGap = Math.Max(0, DrawingArea.Left + controlW - (DestRect.X + DestRect.Width)) / currentZoomFactor;
                effectiveLeftMarginX = Math.Max(baseMarginX, prevLeftGap);
                effectiveRightMarginX = Math.Max(baseMarginX, prevRightGap);
            }

            if (srcX < -effectiveLeftMarginX)
            {
                srcX = -effectiveLeftMarginX;
            }
            else if (srcX + srcWidth > BitmapSize.Width + effectiveRightMarginX)
            {
                srcX = BitmapSize.Width - srcWidth + effectiveRightMarginX;
            }
        }

        if (scaledImgHeight > controlH && !skipClampWhileZooming)
        {
            var baseMarginY = isZoomingToPoint && wasHeightFitting ? 0 : panMarginSrc;

            var effectiveTopMarginY = baseMarginY;
            var effectiveBottomMarginY = baseMarginY;
            if (CanUseFreePan)
            {
                var prevTopGap = Math.Max(0, DestRect.Y - DrawingArea.Top) / currentZoomFactor;
                var prevBottomGap = Math.Max(0, DrawingArea.Top + controlH - (DestRect.Y + DestRect.Height)) / currentZoomFactor;
                effectiveTopMarginY = Math.Max(baseMarginY, prevTopGap);
                effectiveBottomMarginY = Math.Max(baseMarginY, prevBottomGap);
            }

            if (srcY + srcHeight > BitmapSize.Height + effectiveBottomMarginY)
            {
                srcY = BitmapSize.Height - srcHeight + effectiveBottomMarginY;
            }

            if (srcY < -effectiveTopMarginY)
            {
                srcY = -effectiveTopMarginY;
            }
        }

        double logicalX;
        if (scaledImgWidth > controlW)
        {
            logicalX = srcX;
        }
        else if (CanUseFreePan)
        {
            var halfGapX = (controlW - scaledImgWidth) / 2.0;
            logicalX = Math.Clamp(
                (halfGapX + DrawingArea.Left - destX) / currentZoomFactor,
                -halfGapX / currentZoomFactor,
                halfGapX / currentZoomFactor);
        }
        else
        {
            logicalX = 0;
        }

        double logicalY;
        if (scaledImgHeight > controlH)
        {
            logicalY = srcY;
        }
        else if (CanUseFreePan)
        {
            var halfGapY = (controlH - scaledImgHeight) / 2.0;
            logicalY = Math.Clamp(
                (halfGapY + DrawingArea.Top - destY) / currentZoomFactor,
                -halfGapY / currentZoomFactor,
                halfGapY / currentZoomFactor);
        }
        else
        {
            logicalY = 0;
        }

        _logicalSrcPoint = new(logicalX, logicalY);

        if (_zooming.Factor == 1)
        {
            if (scaledImgWidth > controlW) srcX = Math.Round(srcX, MidpointRounding.AwayFromZero);
            if (scaledImgHeight > controlH) srcY = Math.Round(srcY, MidpointRounding.AwayFromZero);
        }

        if (srcX < 0)
        {
            var overPan = -srcX * currentZoomFactor;
            destX += overPan;
            destWidth -= overPan;
            srcWidth += srcX;
            srcX = 0;
        }

        if (srcX + srcWidth > BitmapSize.Width)
        {
            var excess = srcX + srcWidth - BitmapSize.Width;
            var excessScreen = excess * currentZoomFactor;
            destWidth -= excessScreen;
            srcWidth = BitmapSize.Width - srcX;
        }

        if (srcY < 0)
        {
            var overPan = -srcY * currentZoomFactor;
            destY += overPan;
            destHeight -= overPan;
            srcHeight += srcY;
            srcY = 0;
        }

        if (srcY + srcHeight > BitmapSize.Height)
        {
            var excess = srcY + srcHeight - BitmapSize.Height;
            var excessScreen = excess * currentZoomFactor;
            destHeight -= excessScreen;
            srcHeight = BitmapSize.Height - srcY;
        }

        var snappedDestX = SnapToDevicePixel(destX, dpi);
        var snappedDestY = SnapToDevicePixel(destY, dpi);

        SrcRect = new(srcX, srcY, srcWidth, srcHeight);
        DestRect = new(snappedDestX, snappedDestY, destWidth, destHeight);

        _zooming.OldFactor = _zooming.Factor;

        UpdateScrollbars();

        OnRenderStateChanged();
    }

    private static double SnapToDevicePixel(double logicalValue, double dpi)
    {
        if (dpi <= 0) return logicalValue;

        var devicePixel = Math.Round(logicalValue * dpi, MidpointRounding.AwayFromZero);
        return devicePixel / dpi;
    }

    public void SetZoomFactor(double zoomValue, bool isManualZoom)
    {
        if (!isManualZoom)
        {
            SrcRect = new();
            _logicalSrcPoint = new();
            _zooming.ZoomedPoint = new();
        }

        _zooming.Factor = Math.Min(MaxZoom, Math.Max(zoomValue, MinZoom));
        _zooming.IsManual = isManualZoom;

        CalculateDrawingRegion();
        InvalidateVisual();

        ZoomChanged?.Invoke(this, new ViewerZoomEventArgs()
        {
            ZoomFactor = _zooming.Factor,
            IsManualZoom = _zooming.IsManual,
            IsZoomModeChange = false,
            IsPreviewingImage = _isPreviewing,
            ChangeSource = ZoomChangeSource.Unknown,
        });
    }

    public void SetZoomMode(ZoomMode? mode = null, bool isManualZoom = false, bool zoomedByResizing = false)
    {
        var effectiveMode = mode ?? _zooming.Mode;

        if (effectiveMode != ZoomMode.LockZoom)
        {
            _logicalSrcPoint = default;
        }
        _zooming.ZoomedPoint = new();

        _zooming.Mode = effectiveMode;
        _zooming.Factor = CalculateZoomFactor(_zooming.Mode, BitmapSize.Width, BitmapSize.Height);
        _zooming.IsManual = isManualZoom;

        CalculateDrawingRegion();

        ZoomChanged?.Invoke(this, new ViewerZoomEventArgs()
        {
            ZoomFactor = _zooming.Factor,
            IsManualZoom = _zooming.IsManual,
            IsZoomModeChange = mode != _zooming.Mode,
            IsPreviewingImage = _isPreviewing,
            ChangeSource = zoomedByResizing ? ZoomChangeSource.SizeChanged : ZoomChangeSource.ZoomMode,
        });
    }

    internal void RefitForFrameSize(Size newSize)
    {
        if (newSize.IsEmpty || newSize == BitmapSize) return;

        BitmapSize = newSize;

        _logicalSrcPoint = default;
        _zooming.ZoomedPoint = new();

        if (!_zooming.IsManual)
        {
            _zooming.Factor = CalculateZoomFactor(_zooming.Mode, newSize.Width, newSize.Height);
        }
        _zooming.OldFactor = _zooming.Factor;

        CalculateDrawingRegion();

        if (!SourceSelection.IsEmpty) SetSourceSelection(SourceSelection);

        ZoomChanged?.Invoke(this, new ViewerZoomEventArgs()
        {
            ZoomFactor = _zooming.Factor,
            IsManualZoom = _zooming.IsManual,
            IsZoomModeChange = false,
            IsPreviewingImage = _isPreviewing,
            ChangeSource = ZoomChangeSource.FrameChanged,
        });
    }

    public double CalculateZoomFactor(ZoomMode zoomMode, double srcWidth, double srcHeight)
    {
        return CalculateZoomFactor(zoomMode, srcWidth, srcHeight, DrawingArea.Width, DrawingArea.Height);
    }

    public double CalculateZoomFactor(ZoomMode zoomMode, double srcWidth, double srcHeight, double viewportW, double viewportH)
    {
        if (srcWidth == 0 || srcHeight == 0 || viewportW == 0 || viewportH == 0) return _zooming.Factor;

        var widthScale = viewportW / srcWidth * Dpi;
        var heightScale = viewportH / srcHeight * Dpi;
        double zoomFactor;

        if (zoomMode == ZoomMode.ScaleToWidth)
        {
            zoomFactor = widthScale;
        }
        else if (zoomMode == ZoomMode.ScaleToHeight)
        {
            zoomFactor = heightScale;
        }
        else if (zoomMode == ZoomMode.ScaleToFit)
        {
            zoomFactor = Math.Min(widthScale, heightScale);
        }
        else if (zoomMode == ZoomMode.ScaleToFill)
        {
            zoomFactor = Math.Max(widthScale, heightScale);
        }
        else if (zoomMode == ZoomMode.LockZoom)
        {
            zoomFactor = _zooming.Factor;
        }
        else
        {
            if (widthScale >= 1 && heightScale >= 1)
            {
                zoomFactor = 1;
            }
            else
            {
                zoomFactor = Math.Min(widthScale, heightScale);
            }
        }

        return zoomFactor;
    }

    public bool ZoomIn(Point? point = null, bool requestRerender = true)
    {
        return ZoomByDeltaToPoint(Const.MOUSE_WHEEL_SCROLL_DELTA, point, requestRerender);
    }

    public bool ZoomOut(Point? point = null, bool requestRerender = true)
    {
        return ZoomByDeltaToPoint(-Const.MOUSE_WHEEL_SCROLL_DELTA, point, requestRerender);
    }

    public bool ZoomToPoint(float factor, Point? point = null, bool requestRerender = true)
    {
        if (factor >= _zooming.Max || factor <= _zooming.Min) return false;

        var newZoomFactor = (double)factor;
        if (newZoomFactor == _zooming.Factor) return false;

        var location = point ?? _zooming.ZoomedPoint;
        if (location == default || !Bounds.Contains(location))
        {
            location = DrawingArea.Center;
        }

        _zooming.OldFactor = _zooming.Factor;
        _zooming.Factor = Math.Min(MaxZoom, Math.Max(newZoomFactor, MinZoom));
        _zooming.IsManual = true;
        _zooming.ZoomedPoint = location;

        CalculateDrawingRegion();

        if (requestRerender) InvalidateVisual();

        ZoomChanged?.Invoke(this, new ViewerZoomEventArgs()
        {
            ZoomFactor = _zooming.Factor,
            IsManualZoom = _zooming.IsManual,
            IsZoomModeChange = false,
            IsPreviewingImage = _isPreviewing,
            ChangeSource = ZoomChangeSource.Unknown,
        });

        return true;
    }

    public bool ZoomByDeltaToPoint(double delta, Point? point = null, bool requestRerender = true)
    {
        var newZoomFactor = _zooming.Factor;
        var isZoomingByMouseWheel = Math.Abs(delta) == Const.MOUSE_WHEEL_SCROLL_DELTA;

        if (ZoomLevels.Length > 0 && isZoomingByMouseWheel)
        {
            var minZoomLevel = ZoomLevels[0];
            var maxZoomLevel = ZoomLevels[^1];

            if (delta > 0)
            {
                newZoomFactor = ZoomLevels.FirstOrDefault(i => i > _zooming.Factor);
            }
            else if (delta < 0)
            {
                newZoomFactor = ZoomLevels.LastOrDefault(i => i < _zooming.Factor);
            }
            if (newZoomFactor == 0) return false;

            newZoomFactor = Math.Min(Math.Max(minZoomLevel, newZoomFactor), maxZoomLevel);

        }
        else
        {
            var speed = delta / (ZoomInfo.MAX_ZOOM_SPEED - ZoomSpeed + 1);

            if (delta > 0)
            {
                newZoomFactor = _zooming.Factor * (1f + speed);
            }
            else if (delta < 0)
            {
                newZoomFactor = _zooming.Factor / (1f - speed);
            }

            newZoomFactor = Math.Min(Math.Max(MinZoom, newZoomFactor), MaxZoom);
        }

        if (newZoomFactor == _zooming.Factor) return false;

        var location = point ?? new Point(-1, -1);

        if (!Bounds.Contains(location))
        {
            location = DrawingArea.Center;
        }

        _zooming.OldFactor = _zooming.Factor;
        _zooming.Factor = newZoomFactor;
        _zooming.IsManual = true;
        _zooming.ZoomedPoint = new(location.X, location.Y);

        CalculateDrawingRegion();

        if (requestRerender) InvalidateVisual();

        ZoomChanged?.Invoke(this, new ViewerZoomEventArgs()
        {
            ZoomFactor = _zooming.Factor,
            IsManualZoom = _zooming.IsManual,
            IsZoomModeChange = false,
            IsPreviewingImage = _isPreviewing,
            ChangeSource = ZoomChangeSource.Unknown,
        });

        return true;
    }

    public bool PanTo(double hDistance, double vDistance, Point? pointerPosition, bool requestRerender = true)
    {
        if (hDistance == 0 && vDistance == 0) return false;

        hDistance *= Dpi;
        vDistance *= Dpi;
        var oldSrcRect = SrcRect;

        if (hDistance != 0)
        {
            var newX = _logicalSrcPoint.X + hDistance / _zooming.Factor;
            _logicalSrcPoint = _logicalSrcPoint.WithX(newX);
        }

        if (vDistance != 0)
        {
            var newY = _logicalSrcPoint.Y + vDistance / _zooming.Factor;
            _logicalSrcPoint = _logicalSrcPoint.WithY(newY);
        }

        if (pointerPosition is not null)
        {
            _zooming.ZoomedPoint = pointerPosition.Value;
        }

        Panning?.Invoke(this, new ViewerPanEventArgs(oldSrcRect, SrcRect));

        CalculateDrawingRegion();

        if (requestRerender) InvalidateVisual();

        return true;
    }

    public void PanLeft(double distance = 0, bool requestRerender = true)
    {
        distance *= PanSpeed;
        distance = Math.Max(distance, 0);

        _ = PanTo(-distance, 0, null, requestRerender);
    }

    public void PanRight(double distance = 0, bool requestRerender = true)
    {
        distance *= PanSpeed;
        distance = Math.Max(distance, 0);

        _ = PanTo(distance, 0, null, requestRerender);
    }

    public void PanUp(double distance = 0, bool requestRerender = true)
    {
        distance *= PanSpeed;
        distance = Math.Max(distance, 0);

        _ = PanTo(0, -distance, null, requestRerender);
    }

    public void PanDown(double distance = 0, bool requestRerender = true)
    {
        distance *= PanSpeed;
        distance = Math.Max(distance, 0);

        _ = PanTo(0, distance, null, requestRerender);
    }

}
