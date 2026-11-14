using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.UI;
using System;
using System.Collections.Generic;

namespace PhotoViewer.Tools;

public sealed class ProjectorPointerControl : PhControl
{
    private const double RADIUS = 30;
    private const double DOT_RATIO = 0.15;

    private const double GLOW_CENTER_OPACITY = 0.6;
    private const double GLOW_EDGE_OPACITY = 0.2;

    private static readonly Color GLOW_CORE_COLOR = Color.FromRgb(0xFF, 0xF5, 0xC2);
    private static readonly Color GLOW_COLOR = Color.FromRgb(0xFF, 0xCC, 0x00);
    private static readonly Color DOT_COLOR = Color.FromRgb(0xFF, 0x3B, 0x30);

    private const double RIPPLE_SCALE = 2.4;
    private const float RIPPLE_WIDTH = 3f;
    private const double EXTENT = (RADIUS * RIPPLE_SCALE + RIPPLE_WIDTH) * 2;

    private const double CLICK_MS = 450;
    private const double FADE_MS = 150;

    private readonly TranslateTransform _offset = new();

    private readonly List<TimeSpan?> _clicks = [];

    private double _opacity;
    private bool _isShown;
    private bool _isAnimating;
    private TimeSpan _lastFrameTime;
    private TimeSpan _frameTime;

    public ProjectorPointerControl()
    {
        IsHitTestVisible = false;
        RenderTransform = _offset;
        Width = EXTENT;
        Height = EXTENT;
    }

    public void MoveTo(Point center)
    {
        _offset.X = center.X - EXTENT / 2;
        _offset.Y = center.Y - EXTENT / 2;

        if (_isShown) return;

        _isShown = true;
        StartAnimation();
    }

    public void Hide()
    {
        if (!_isShown) return;

        _isShown = false;
        StartAnimation();
    }

    public void PlayClick()
    {
        if (!_isShown) return;

        _clicks.Add(null);
        StartAnimation();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        InvalidateVisual();
    }

    public override void Render(DrawingContext c)
    {
        base.Render(c);

        var hasClicks = _clicks.Count > 0;
        var isFadedOut = _opacity <= 0;
        if (isFadedOut && !hasClicks) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        foreach (var start in _clicks)
        {
            if (start is not { } startTime) continue;

            var progress = Math.Clamp((_frameTime - startTime).TotalMilliseconds / CLICK_MS, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            var ringRadius = RADIUS * (1 + (RIPPLE_SCALE - 1) * eased);
            var ringAlpha = (int)(DOT_COLOR.A * (1 - progress) * _opacity);
            c.DrawEllipseEx(center.X, center.Y, (float)ringRadius, DOT_COLOR.WithAlpha(ringAlpha), null, RIPPLE_WIDTH);
        }

        c.DrawEllipse(CreateGlowBrush(_opacity), null, center, RADIUS, RADIUS);

        var dotRadius = RADIUS * DOT_RATIO;
        c.DrawEllipseEx(center.X, center.Y, (float)dotRadius, null, DOT_COLOR.WithAlpha((int)(DOT_COLOR.A * _opacity)), 0);
    }

    private static RadialGradientBrush CreateGlowBrush(double opacity)
    {
        var centerAlpha = (int)(255 * GLOW_CENTER_OPACITY * opacity);
        var edgeAlpha = (int)(255 * GLOW_EDGE_OPACITY * opacity);

        return new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(GLOW_CORE_COLOR.WithAlpha(centerAlpha), 0),
                new GradientStop(GLOW_COLOR.WithAlpha(edgeAlpha), 1),
            },
        };
    }

    private void StartAnimation()
    {
        if (_isAnimating) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            _opacity = _isShown ? 1 : 0;
            _clicks.Clear();
            return;
        }

        _isAnimating = true;
        _lastFrameTime = TimeSpan.Zero;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        var elapsedMs = _lastFrameTime == TimeSpan.Zero ? 0 : (time - _lastFrameTime).TotalMilliseconds;
        _lastFrameTime = time;
        _frameTime = time;

        var targetOpacity = _isShown ? 1d : 0d;
        var fadeStep = elapsedMs / FADE_MS;
        _opacity = targetOpacity > _opacity
            ? Math.Min(targetOpacity, _opacity + fadeStep)
            : Math.Max(targetOpacity, _opacity - fadeStep);

        for (var i = 0; i < _clicks.Count; i++)
        {
            _clicks[i] ??= time;
        }
        _ = _clicks.RemoveAll(start => (time - start!.Value).TotalMilliseconds >= CLICK_MS);

        InvalidateVisual();

        var isFading = _opacity != targetOpacity;
        var hasClicks = _clicks.Count > 0;
        var isSettled = !isFading && !hasClicks;
        if (isSettled)
        {
            _isAnimating = false;
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            _isAnimating = false;
            return;
        }

        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

}
