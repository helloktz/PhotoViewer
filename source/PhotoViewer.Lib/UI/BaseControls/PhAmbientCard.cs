using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using System;

namespace PhotoViewer.UI;

public class PhAmbientCard : PhControl
{
    private const double WASH_ALPHA = 0.055;
    private const int BLOOM_ALPHA = 40;
    private const double BLOOM_RADIUS = 0.85;

    private const int RESTING_BORDER_ALPHA = 64;
    private const double GLOW_OPACITY = 0.5;
    private const double GLOW_WIDTH_SCALE = 6.0;
    private const double LIGHT_ARC = 0.05;
    private const double GLOW_ARC = 0.2;
    private const int BAND_FALLOFF_ALPHA = 110;
    private const double BAND_FALLOFF_OFFSET = 0.4;

    private const float LIGHT_BRIGHTNESS_DARK = 0.55f;
    private const float LIGHT_BRIGHTNESS_LIGHT = -0.2f;

    private TimeSpan? _startTimestamp;
    private double _angleDeg;
    private bool _animRunning;

    public PhAmbientCard()
    {
        Background = null;
        BorderBrush = null;
    }

    public Color? AmbientColor
    {
        get => GetValue(AmbientColorProperty);
        set => SetValue(AmbientColorProperty, value);
    }
    public static readonly StyledProperty<Color?> AmbientColorProperty =
        AvaloniaProperty.Register<PhAmbientCard, Color?>(nameof(AmbientColor));

    public bool EnableLight
    {
        get => GetValue(EnableLightProperty);
        set => SetValue(EnableLightProperty, value);
    }
    public static readonly StyledProperty<bool> EnableLightProperty =
        AvaloniaProperty.Register<PhAmbientCard, bool>(nameof(EnableLight), true);

    public double LightCycleSeconds
    {
        get => GetValue(LightCycleSecondsProperty);
        set => SetValue(LightCycleSecondsProperty, value);
    }
    public static readonly StyledProperty<double> LightCycleSecondsProperty =
        AvaloniaProperty.Register<PhAmbientCard, double>(nameof(LightCycleSeconds), 9.0);

    public double LightThickness
    {
        get => GetValue(LightThicknessProperty);
        set => SetValue(LightThicknessProperty, value);
    }
    public static readonly StyledProperty<double> LightThicknessProperty =
        AvaloniaProperty.Register<PhAmbientCard, double>(nameof(LightThickness), 0.5);

    public override void Render(DrawingContext c)
    {
        base.Render(c);

        try
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

            var accent = AmbientColor ?? Core.AccentColor;
            var lightColor = accent.WithBrightness(Core.Theme.Settings.IsDarkMode
                ? LIGHT_BRIGHTNESS_DARK
                : LIGHT_BRIGHTNESS_LIGHT);

            var lightWidth = Math.Max(0.5, LightThickness);
            var cardRect = new RoundedRect(new Rect(Bounds.Size), CornerRadius);

            c.DrawRectangle(new ImmutableSolidColorBrush(accent, WASH_ALPHA), null, cardRect);
            c.DrawRectangle(CreateBloomBrush(accent, _angleDeg), null, cardRect);

            using var _ = c.PushClip(cardRect);

            var strokeRect = cardRect.Deflate(lightWidth / 2, lightWidth / 2);
            var glowPen = new ImmutablePen(
                CreateSweepBrush(lightColor, GLOW_ARC, GLOW_OPACITY, _angleDeg),
                lightWidth * GLOW_WIDTH_SCALE);
            var lightPen = new ImmutablePen(
                CreateSweepBrush(lightColor, LIGHT_ARC, 1.0, _angleDeg), lightWidth);

            c.DrawRectangle(null, glowPen, strokeRect);
            c.DrawRectangle(null, lightPen, strokeRect);
        }
        catch { }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        StartLight();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        _animRunning = false;
        _startTimestamp = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == EnableLightProperty)
        {
            if (EnableLight) StartLight();
            else
            {
                _animRunning = false;
                _angleDeg = 0;
                InvalidateVisual();
            }
        }
        else if (e.Property == AmbientColorProperty || e.Property == LightThicknessProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        InvalidateVisual();
    }

    private void StartLight()
    {
        try
        {
            if (_animRunning || !EnableLight) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (!IsLoaded || topLevel is null) return;

            _startTimestamp = null;
            _animRunning = true;
            topLevel.RequestAnimationFrame(OnAnimationFrame);
        }
        catch { }
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        try
        {
            if (!_animRunning) return;

            _startTimestamp ??= timestamp;

            var cycle = Math.Max(0.5, LightCycleSeconds);
            var elapsed = (timestamp - _startTimestamp.Value).TotalSeconds;
            _angleDeg = elapsed / cycle % 1.0 * 360.0;

            if (IsEffectivelyVisible) InvalidateVisual();

            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnAnimationFrame);
        }
        catch
        {
            _animRunning = false;
        }
    }

    private static ImmutableConicGradientBrush CreateSweepBrush(Color color, double arc,
        double opacity, double angleDeg)
    {
        var halfArc = Math.Clamp(arc, 0.005, 0.49);
        var falloffArc = halfArc * BAND_FALLOFF_OFFSET;
        var fadeColor = color.WithAlpha(0);
        var falloffColor = color.WithAlpha(BAND_FALLOFF_ALPHA);

        ImmutableGradientStop[] stops =
        [
            new(0, fadeColor),
            new(0.5 - halfArc, fadeColor),
            new(0.5 - falloffArc, falloffColor),
            new(0.5, color),
            new(0.5 + falloffArc, falloffColor),
            new(0.5 + halfArc, fadeColor),
            new(1, fadeColor),
        ];

        return new ImmutableConicGradientBrush(stops, opacity, angle: angleDeg + 180.0);
    }

    private static ImmutableRadialGradientBrush CreateBloomBrush(Color color, double angleDeg)
    {
        var radian = angleDeg * Math.PI / 180.0;
        var center = new RelativePoint(
            0.5 + 0.5 * Math.Sin(radian),
            0.5 - 0.5 * Math.Cos(radian),
            RelativeUnit.Relative);

        ImmutableGradientStop[] stops =
        [
            new(0, color.WithAlpha(BLOOM_ALPHA)),
            new(1, color.WithAlpha(0)),
        ];

        return new ImmutableRadialGradientBrush(stops,
            center: center, gradientOrigin: center, radius: BLOOM_RADIUS);
    }

}
