using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using System;

namespace PhotoViewer.UI;

public class PhGridSplitter : GridSplitter
{
    protected override Type StyleKeyOverride => typeof(GridSplitter);

    private bool _isPointerDown = false;
    private IBrush _bgNormal = Brushes.Transparent;
    private IBrush _bgHover = Brushes.Transparent;
    private IBrush _bgPressed = Brushes.Transparent;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        UploadThemeColors();
        Background = _bgNormal;

        Core.ThemeChanged += Core_ThemeChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Core.ThemeChanged -= Core_ThemeChanged;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        Background = _bgHover;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        _isPointerDown = true;
        Background = _bgPressed;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isPointerDown) Background = _bgPressed;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPointerDown = false;

        if (IsPointerOver) Background = _bgHover;
        else Background = _bgNormal;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Background = _bgNormal;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isPointerDown = false;
    }

    private void Core_ThemeChanged(object? sender, ThemePackChangedEventArgs e)
    {
        UploadThemeColors();
        Background = _bgNormal;
    }

    private void UploadThemeColors()
    {
        var alpha = (byte)Math.Max(50d, AppThemeColors.GalleryBgBrush.A);

        _bgNormal = Resx.Get<IBrush>(ResxId.IG_BorderNeutralBrush);

        _bgHover = AppThemeColors.GalleryBgBrush.Color
            .Blend(Core.Theme.InvertedBaseColor, 0.8, alpha)
            .ToBrush();

        _bgPressed = AppThemeColors.GalleryBgBrush.Color
            .Blend(Core.Theme.InvertedBaseColor, 0.7, alpha)
            .ToBrush();
    }

}
