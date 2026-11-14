using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using PhotoViewer.Common.Photoing;
using Avalonia.VisualTree;
using PhotoViewer.Common;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Extensions;
using System;
using System.IO;

namespace PhotoViewer.UI.Viewer;

public class NavButtonsOverlay : PhControl
{
    private const double ANIM_DURATION_MS = 100;
    private const double ANIM_SLIDE_DISTANCE = 10;

    private NavButtonsInfo _state = new();
    private ViewerControl? _parentViewer;
    private Window? _parentWindow;

    private bool _swallowActivatingPress = false;
    private Point? _lastPointerPoint = null;

    private double _leftAnimProgress = 0;
    private double _rightAnimProgress = 0;
    private bool _leftAnimTarget = false;
    private bool _rightAnimTarget = false;
    private bool _animRunning = false;
    private TimeSpan _lastFrameTime = TimeSpan.Zero;

    public NavButtonsOverlay()
    {
        Background = Brushes.Transparent;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _parentViewer = this.FindAncestorOfType<ViewerControl>();
        if (_parentViewer is not null)
        {
            _state = _parentViewer._navButtons;
        }

        _parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (_parentWindow is not null)
        {
            _parentWindow.Activated += ParentWindow_Activated;
            _parentWindow.Deactivated += ParentWindow_Deactivated;
        }

        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);

        LoadIcons();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        RemoveHandler(ContextRequestedEvent, OnContextRequested);

        if (_parentWindow is not null)
        {
            _parentWindow.Activated -= ParentWindow_Activated;
            _parentWindow.Deactivated -= ParentWindow_Deactivated;
            _parentWindow = null;
        }

        _animRunning = false;
    }

    private void ParentWindow_Activated(object? sender, EventArgs e)
    {
        if (!IsPointerOver || _lastPointerPoint is not Point pos) return;

        _state.IsLeftHovered = GetLeftHitArea().Contains(pos);
        _state.IsRightHovered = GetRightHitArea().Contains(pos);
        UpdateAnimationTargets();
    }

    private void ParentWindow_Deactivated(object? sender, EventArgs e)
    {
        _swallowActivatingPress = true;
        _state.ResetState();
        UpdateAnimationTargets();
        InvalidateVisual();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        LoadIcons();
        InvalidateVisual();
    }

    protected override void OnIgDpiChanged()
    {
        base.OnIgDpiChanged();
        InvalidateVisual();
    }

    public override void Render(DrawingContext c)
    {
        base.Render(c);

        if (_parentViewer is null) return;
        if (!_state.IsEnabled || _parentViewer.EnableSelection) return;

        DrawButton(c, isLeft: true);
        DrawButton(c, isLeft: false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_parentViewer is null || !_state.IsEnabled || _parentViewer.EnableSelection) return;

        var p = e.GetCurrentPoint(this);
        if (!p.Properties.IsLeftButtonPressed) return;

        if (!IsWindowActive() || _swallowActivatingPress)
        {
            _swallowActivatingPress = false;
            return;
        }

        var pos = p.Position;
        _state.PointerDownPoint = pos;
        _state.IsDragging = false;

        if (GetLeftHitArea().Contains(pos))
        {
            _state.IsRightHovered = false;
            _state.IsRightPressed = false;
            _state.IsLeftPressed = true;
            UpdateAnimationTargets();
            InvalidateVisual();
        }
        else if (GetRightHitArea().Contains(pos))
        {
            _state.IsLeftHovered = false;
            _state.IsLeftPressed = false;
            _state.IsRightPressed = true;
            UpdateAnimationTargets();
            InvalidateVisual();
        }

    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_parentViewer is null || !_state.IsEnabled || _parentViewer.EnableSelection) return;

        var pos = e.GetPosition(this);
        var p = e.GetCurrentPoint(this);

        if (!IsWindowActive())
        {
            _lastPointerPoint = pos;
            _state.ResetState();
            UpdateAnimationTargets();
            return;
        }

        if (_lastPointerPoint != pos) _swallowActivatingPress = false;
        _lastPointerPoint = pos;

        if ((_state.IsLeftPressed || _state.IsRightPressed) && !_state.IsDragging)
        {
            if (_state.PointerDownPoint is Point downPt)
            {
                var dx = pos.X - downPt.X;
                var dy = pos.Y - downPt.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > 5)
                {
                    _state.IsDragging = true;
                    UpdateAnimationTargets();
                }
            }
        }

        if (!p.Properties.IsLeftButtonPressed)
        {
            _state.IsLeftHovered = GetLeftHitArea().Contains(pos);
            _state.IsRightHovered = GetRightHitArea().Contains(pos);
            UpdateAnimationTargets();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var wasLeftPressed = _state.IsLeftPressed;
        var wasRightPressed = _state.IsRightPressed;
        var wasDragging = _state.IsDragging;

        _state.IsLeftPressed = false;
        _state.IsRightPressed = false;
        _state.IsDragging = false;
        _state.PointerDownPoint = null;

        if (_parentViewer is null || !_state.IsEnabled || _parentViewer.EnableSelection)
            return;
        if (!IsWindowActive()) return;

        var pos = e.GetPosition(this);

        if (!wasDragging)
        {
            if (wasLeftPressed && GetLeftHitArea().Contains(pos))
            {
                _parentViewer.OnNavButtonClicked(NavButtonDirection.Left);
            }
            else if (wasRightPressed && GetRightHitArea().Contains(pos))
            {
                _parentViewer.OnNavButtonClicked(NavButtonDirection.Right);
            }
        }

        _state.IsLeftHovered = GetLeftHitArea().Contains(pos);
        _state.IsRightHovered = GetRightHitArea().Contains(pos);
        UpdateAnimationTargets();
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _state.ResetState();
        _lastPointerPoint = null;
        UpdateAnimationTargets();
    }

    private bool IsWindowActive() => _parentWindow?.IsActive ?? true;

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_parentViewer is null || !_state.IsEnabled || _parentViewer.EnableSelection) return;

        if (_state.IsLeftHovered || _state.IsRightHovered
            || _state.IsLeftPressed || _state.IsRightPressed)
        {
            e.Handled = true;
        }
    }

    private void DrawButton(DrawingContext c, bool isLeft)
    {
        var progress = isLeft ? _leftAnimProgress : _rightAnimProgress;
        if (progress <= 0) return;

        var btnRect = isLeft ? GetLeftButtonRect() : GetRightButtonRect();
        var icon = isLeft ? _state.LeftIcon : _state.RightIcon;
        var isPressed = isLeft ? _state.IsLeftPressed : _state.IsRightPressed;

        var slideOffset = ANIM_SLIDE_DISTANCE * (1.0 - progress);
        if (!isLeft) slideOffset = -slideOffset;

        using (c.PushTransform(Matrix.CreateTranslation(slideOffset, 0)))
        {
            using (c.PushOpacity(progress))
            {
                var pressScale = (isPressed && !_state.IsDragging) ? 0.95 : 1.0;
                var btnCenterX = btnRect.X + btnRect.Width / 2;
                var btnCenterY = btnRect.Y + btnRect.Height / 2;
                using var _scale = c.PushTransform(
                    Matrix.CreateTranslation(-btnCenterX, -btnCenterY)
                    * Matrix.CreateScale(pressScale, pressScale)
                    * Matrix.CreateTranslation(btnCenterX, btnCenterY));

                c.DrawEllipseEx(btnRect, null, Core.Theme.BaseColor.WithAlpha(120));

                if (isPressed && !_state.IsDragging)
                {
                    c.DrawEllipseEx(btnRect, Core.AccentColor.WithAlpha(180), Core.AccentColor.WithAlpha(100), 1.5f);
                }
                else
                {
                    c.DrawEllipseEx(btnRect, Core.AccentColor.WithAlpha(120), Core.AccentColor.WithAlpha(100), 1f);
                }

                if (icon is not null)
                {
                    var iconSize = new Size(btnRect.Width * 0.5, btnRect.Height * 0.5);
                    var iconRect = new Rect(
                        btnRect.X + (btnRect.Width - iconSize.Width) / 2,
                        btnRect.Y + (btnRect.Height - iconSize.Height) / 2,
                        iconSize.Width,
                        iconSize.Height);

                    c.DrawImage(icon, iconRect);
                }
            }
        }
    }

    private void UpdateAnimationTargets()
    {
        var leftVisible = (_state.IsLeftHovered || _state.IsLeftPressed) && !_state.IsDragging;
        var rightVisible = (_state.IsRightHovered || _state.IsRightPressed) && !_state.IsDragging;

        _leftAnimTarget = leftVisible;
        _rightAnimTarget = rightVisible;

        EnsureAnimationRunning();
    }

    private void EnsureAnimationRunning()
    {
        var leftDone = _leftAnimTarget ? _leftAnimProgress >= 1.0 : _leftAnimProgress <= 0.0;
        var rightDone = _rightAnimTarget ? _rightAnimProgress >= 1.0 : _rightAnimProgress <= 0.0;

        if (leftDone && rightDone)
        {
            _animRunning = false;
            return;
        }

        if (!_animRunning)
        {
            _animRunning = true;
            _lastFrameTime = TimeSpan.Zero;
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnAnimationFrame);
        }
    }

    private void OnAnimationFrame(TimeSpan ts)
    {
        if (!_animRunning) return;

        if (_lastFrameTime != TimeSpan.Zero)
        {
            var elapsedMs = (ts - _lastFrameTime).TotalMilliseconds;
            var step = elapsedMs / ANIM_DURATION_MS;

            _leftAnimProgress = _leftAnimTarget
                ? Math.Min(1.0, _leftAnimProgress + step)
                : Math.Max(0.0, _leftAnimProgress - step);

            _rightAnimProgress = _rightAnimTarget
                ? Math.Min(1.0, _rightAnimProgress + step)
                : Math.Max(0.0, _rightAnimProgress - step);

            InvalidateVisual();
        }
        _lastFrameTime = ts;

        var leftDone = _leftAnimTarget ? _leftAnimProgress >= 1.0 : _leftAnimProgress <= 0.0;
        var rightDone = _rightAnimTarget ? _rightAnimProgress >= 1.0 : _rightAnimProgress <= 0.0;

        if (leftDone && rightDone)
        {
            _animRunning = false;
            return;
        }

        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnAnimationFrame);
    }

    private Rect GetLeftHitArea()
    {
        var hitWidth = NavButtonsInfo.NAV_BTN_SIZE.Width + NavButtonsInfo.NAV_BTN_MARGIN;
        return new Rect(0, 0, hitWidth, Bounds.Height);
    }

    private Rect GetRightHitArea()
    {
        var hitWidth = NavButtonsInfo.NAV_BTN_SIZE.Width + NavButtonsInfo.NAV_BTN_MARGIN;
        return new Rect(Bounds.Width - hitWidth, 0, hitWidth, Bounds.Height);
    }

    private Rect GetLeftButtonRect()
    {
        var size = NavButtonsInfo.NAV_BTN_SIZE;
        return new Rect(NavButtonsInfo.NAV_BTN_MARGIN, (Bounds.Height - size.Height) / 2, size.Width, size.Height);
    }

    private Rect GetRightButtonRect()
    {
        var size = NavButtonsInfo.NAV_BTN_SIZE;
        return new Rect(Bounds.Width - NavButtonsInfo.NAV_BTN_MARGIN - size.Width, (Bounds.Height - size.Height) / 2, size.Width, size.Height);
    }

    private void LoadIcons()
    {
        _state.LeftArrowSvgPath = Core.Theme.GetIconPath(IgThemeIcon.ViewPreviousImage);
        _state.RightArrowSvgPath = Core.Theme.GetIconPath(IgThemeIcon.ViewNextImage);
        _state.LeftIcon = LoadSvgIcon(_state.LeftArrowSvgPath);
        _state.RightIcon = LoadSvgIcon(_state.RightArrowSvgPath);
    }

    private static SvgImage? LoadSvgIcon(string svgPath)
    {
        if (string.IsNullOrEmpty(svgPath) || !File.Exists(svgPath)) return null;

        try
        {
            var svgSource = SvgSource.Load(svgPath, null, SvgCodec.LocalResources);
            return new SvgImage { Source = svgSource };
        }
        catch { }

        return null;
    }

}
