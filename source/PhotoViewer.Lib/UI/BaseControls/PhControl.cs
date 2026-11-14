using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PhotoViewer.Common;
using System;

namespace PhotoViewer.UI;

public partial class PhControl : ContentControl
{
    protected override Type StyleKeyOverride => typeof(ContentControl);

    public double Dpi => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;

    public bool IsContentVisible
    {
        get => GetValue(IsContentVisibleProperty);
        set => SetValue(IsContentVisibleProperty, value);
    }
    public static readonly StyledProperty<bool> IsContentVisibleProperty =
        AvaloniaProperty.Register<PhControl, bool>(nameof(IsContentVisible), true,
            coerce: (sender, value) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var control = (PhControl)sender;
                    if (control.Content is Control contentEl)
                    {
                        contentEl.IsVisible = value;
                    }
                });

                return value;
            });

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        OnIgLanguageChanged();

        Core.ThemeChanged += Core_ThemeChanged;
        Core.LanguageChanged += Core_LanguageChanged;
        TopLevel.GetTopLevel(this)?.ScalingChanged += TopLevel_ScalingChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        Core.ThemeChanged -= Core_ThemeChanged;
        Core.LanguageChanged -= Core_LanguageChanged;
        TopLevel.GetTopLevel(this)?.ScalingChanged -= TopLevel_ScalingChanged;
    }

    private void Core_ThemeChanged(object? sender, ThemePackChangedEventArgs e)
    {
        OnIgThemeChanged(e);
    }

    private void Core_LanguageChanged(object? sender, EventArgs e)
    {
        OnIgLanguageChanged();
    }

    private void TopLevel_ScalingChanged(object? sender, EventArgs e)
    {
        OnIgDpiChanged();
    }

    protected virtual void OnIgDpiChanged() { }

    protected virtual void OnIgThemeChanged(ThemePackChangedEventArgs e) { }

    protected virtual void OnIgLanguageChanged() { }

    public double DpiScale(double value, double? scaleFactor = null) => (scaleFactor ?? Dpi) * value;

    public Size DpiScale(Size value, double? scaleFactor = null) => new Size(DpiScale(value.Width, scaleFactor), DpiScale(value.Height, scaleFactor));

    public Point DpiScale(Point value, double? scaleFactor = null) => new Point(DpiScale(value.X, scaleFactor), DpiScale(value.Y, scaleFactor));

}
