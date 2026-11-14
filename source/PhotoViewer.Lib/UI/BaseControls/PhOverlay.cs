using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using PhotoViewer.Common;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using System;

namespace PhotoViewer.UI;

public enum PhOverlayDirection
{

    Auto,
    Top,
    Bottom,
    Left,
    Right,
}

public class PhOverlay : PhControl
{
    private const int BORDER_ALPHA = 20;
    private const double SHADOW_OFFSET_Y = 4;
    private const double SHADOW_BLUR = 24;
    private const int SHADOW_ALPHA_DARK = 56;
    private const int SHADOW_ALPHA_LIGHT = 28;

    private const double SHOW_MS = 200;
    private const double HIDE_MS = 150;
    private const double SLIDE_DISTANCE = 12;
    private const double START_SCALE = 0.95;

    private readonly MatrixTransform _transform = new();
    private IImmutableBrush? _fillBrush;
    private ImmutablePen? _borderPen;
    private BoxShadows _shadow;

    private double _progress;
    private bool _isAnimating;
    private TimeSpan _lastFrameTime;

    public bool IsShown
    {
        get => GetValue(IsShownProperty);
        set => SetValue(IsShownProperty, value);
    }
    public static readonly StyledProperty<bool> IsShownProperty =
        AvaloniaProperty.Register<PhOverlay, bool>(nameof(IsShown));

    public PhOverlayDirection TransitionDirection
    {
        get => GetValue(TransitionDirectionProperty);
        set => SetValue(TransitionDirectionProperty, value);
    }
    public static readonly StyledProperty<PhOverlayDirection> TransitionDirectionProperty =
        AvaloniaProperty.Register<PhOverlay, PhOverlayDirection>(nameof(TransitionDirection), PhOverlayDirection.Auto);

    public bool IsOnScreen
    {
        get
        {
            if (IsShown) return true;

            var isFading = _progress > 0;
            return isFading;
        }
    }

    static PhOverlay()
    {
        IsVisibleProperty.OverrideDefaultValue<PhOverlay>(false);
        AffectsRender<PhOverlay>(CornerRadiusProperty);

        ClipToBoundsProperty.OverrideDefaultValue<PhOverlay>(false);
    }

    public PhOverlay()
    {
        RenderTransform = _transform;
        Opacity = 0;
        UpdateTransformOrigin();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateBrushes();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);

        UpdateBrushes();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsShownProperty)
        {
            OnIsShownChanged();
        }
        else if (change.Property == TransitionDirectionProperty)
        {
            UpdateTransformOrigin();
            ApplyProgress();
        }
    }

    public override void Render(DrawingContext c)
    {
        base.Render(c);

        var size = Bounds.Size;
        if (size.Width <= 0) return;
        if (size.Height <= 0) return;

        var surface = new RoundedRect(new Rect(size), GetCornerRadius());
        c.DrawRectangle(_fillBrush, _borderPen, surface, _shadow);
    }

    private void UpdateBrushes()
    {
        var fillColor = Resx.GetBrushColor(ResxId.IG_OverlayBackgroundBrush, AppThemeColors.BgBrush.Color);
        var borderColor = Core.Theme.InvertedBaseColor.WithAlpha(BORDER_ALPHA);
        var shadowAlpha = Core.Theme.Settings.IsDarkMode ? SHADOW_ALPHA_DARK : SHADOW_ALPHA_LIGHT;

        _fillBrush = new ImmutableSolidColorBrush(fillColor);
        _borderPen = new ImmutablePen(new ImmutableSolidColorBrush(borderColor));
        _shadow = new BoxShadows(new BoxShadow
        {
            OffsetY = SHADOW_OFFSET_Y,
            Blur = SHADOW_BLUR,
            Color = Colors.Black.WithAlpha(shadowAlpha),
        });
    }

    private CornerRadius GetCornerRadius()
    {
        var isCustom = IsSet(CornerRadiusProperty);
        if (isCustom) return CornerRadius;

        var token = Resx.Get<object?>(ResxId.OverlayCornerRadius);
        if (token is CornerRadius radius) return radius;

        return CornerRadius;
    }

    private void UpdateTransformOrigin()
    {
        RenderTransformOrigin = TransitionDirection switch
        {
            PhOverlayDirection.Top => new RelativePoint(0.5, 0, RelativeUnit.Relative),
            PhOverlayDirection.Bottom => new RelativePoint(0.5, 1, RelativeUnit.Relative),
            PhOverlayDirection.Left => new RelativePoint(0, 0.5, RelativeUnit.Relative),
            PhOverlayDirection.Right => new RelativePoint(1, 0.5, RelativeUnit.Relative),
            _ => new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        };
    }

    private void OnIsShownChanged()
    {
        if (IsShown)
        {
            ApplyProgress();
            SetCurrentValue(IsVisibleProperty, true);
        }

        StartAnimation();
    }

    private void StartAnimation()
    {
        if (_isAnimating) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            _progress = IsShown ? 1 : 0;
            ApplyProgress();
            return;
        }

        _isAnimating = true;
        _lastFrameTime = TimeSpan.Zero;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            _isAnimating = false;
            _progress = IsShown ? 1 : 0;
            ApplyProgress();
            return;
        }

        var isFirstFrame = _lastFrameTime == TimeSpan.Zero;
        var elapsedMs = isFirstFrame ? 0 : (time - _lastFrameTime).TotalMilliseconds;
        _lastFrameTime = time;

        var isShown = IsShown;
        var step = elapsedMs / (isShown ? SHOW_MS : HIDE_MS);
        _progress = isShown
            ? Math.Min(1, _progress + step)
            : Math.Max(0, _progress - step);

        ApplyProgress();

        var targetProgress = isShown ? 1 : 0;
        var isSettled = _progress == targetProgress;
        if (isSettled)
        {
            _isAnimating = false;
            return;
        }

        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void ApplyProgress()
    {
        var eased = EaseOutCubic(_progress);
        var scale = START_SCALE + (1 - START_SCALE) * eased;
        var slide = SLIDE_DISTANCE * (1 - eased);
        var offset = TransitionDirection switch
        {
            PhOverlayDirection.Top => new Vector(0, -slide),
            PhOverlayDirection.Bottom => new Vector(0, slide),
            PhOverlayDirection.Left => new Vector(-slide, 0),
            PhOverlayDirection.Right => new Vector(slide, 0),
            _ => default,
        };

        Opacity = eased;
        _transform.Matrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset);

        if (IsShown) return;

        var isFadedOut = _progress <= 0;
        if (isFadedOut) SetCurrentValue(IsVisibleProperty, false);
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

}
