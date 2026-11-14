using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using System;
using System.ComponentModel;

namespace PhotoViewer.UI;

public class PhButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    private static readonly Thickness ICON_GAP = new(0, 0, 4, 0);

    private TextBlock? _textEl;
    private Path? _iconEl;

    public event TEventHandler<ContextMenu, CancelEventArgs>? DropdownOpening;
    public event TEventHandler<ContextMenu, RoutedEventArgs>? DropdownOpened;
    public event TEventHandler<ContextMenu, CancelEventArgs>? DropdownClosing;
    public event TEventHandler<ContextMenu, RoutedEventArgs>? DropdownClosed;

    public double Dpi => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;

    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }
    public static readonly StyledProperty<Geometry?> IconDataProperty =
        AvaloniaProperty.Register<PhButton, Geometry?>(nameof(IconData));

    public double IconStrokeThickness
    {
        get => GetValue(IconStrokeThicknessProperty);
        set => SetValue(IconStrokeThicknessProperty, value);
    }
    public static readonly StyledProperty<double> IconStrokeThicknessProperty =
        AvaloniaProperty.Register<PhButton, double>(nameof(IconStrokeThickness));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<PhButton, string>(nameof(Text));

    public TextTrimming TextTrimming
    {
        get => GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }
    public static readonly StyledProperty<TextTrimming> TextTrimmingProperty =
        AvaloniaProperty.Register<PhButton, TextTrimming>(nameof(TextTrimming), TextTrimming.None);

    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }
    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        AvaloniaProperty.Register<PhButton, TextWrapping>(nameof(TextWrapping), TextWrapping.NoWrap);

    public PhButtonVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
    public static readonly StyledProperty<PhButtonVariant> VariantProperty =
        AvaloniaProperty.Register<PhButton, PhButtonVariant>(nameof(Variant), PhButtonVariant.Default);

    public bool IsIconVisible => IconData != null;
    public static readonly DirectProperty<PhButton, bool> IsIconVisibleProperty =
        AvaloniaProperty.RegisterDirect<PhButton, bool>(nameof(IsIconVisible), i => i.IsIconVisible);

    public bool IsTextVisible => !string.IsNullOrWhiteSpace(Text);
    public static readonly DirectProperty<PhButton, bool> IsTextVisibleProperty =
        AvaloniaProperty.RegisterDirect<PhButton, bool>(nameof(IsTextVisible), i => i.IsTextVisible);

    public bool IsCheckOnClick
    {
        get => GetValue(IsCheckOnClickProperty);
        set => SetValue(IsCheckOnClickProperty, value);
    }
    public static readonly StyledProperty<bool> IsCheckOnClickProperty =
        AvaloniaProperty.Register<PhButton, bool>(nameof(IsCheckOnClick));

    public bool IsOpenDropdownMenuOnClick
    {
        get => GetValue(IsOpenDropdownMenuOnClickProperty);
        set => SetValue(IsOpenDropdownMenuOnClickProperty, value);
    }
    public static readonly StyledProperty<bool> IsOpenDropdownMenuOnClickProperty =
        AvaloniaProperty.Register<PhButton, bool>(nameof(IsOpenDropdownMenuOnClick));

    public ContextMenu? DropdownMenu
    {
        get => GetValue(DropdownMenuProperty);
        set
        {
            DropdownMenu?.Opening -= DropdownMenu_Opening;
            DropdownMenu?.Opened -= DropdownMenu_Opened;
            DropdownMenu?.Closing -= DropdownMenu_Closing;
            DropdownMenu?.Closed -= DropdownMenu_Closed;

            SetValue(DropdownMenuProperty, value);
        }
    }
    public static readonly StyledProperty<ContextMenu?> DropdownMenuProperty =
        AvaloniaProperty.Register<PhButton, ContextMenu?>(nameof(DropdownMenu));

    public PhButton()
    {
        Content = CreateContentElement();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        OnIgLanguageChanged();
        UpdateOutlineBorderBrush();
        UpdateIconPaint();
        Core.ThemeChanged += Core_ThemeChanged;
        Core.LanguageChanged += Core_LanguageChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        Core.ThemeChanged -= Core_ThemeChanged;
        Core.LanguageChanged -= Core_LanguageChanged;
    }

    protected override void OnClick()
    {
        if (IsOpenDropdownMenuOnClick)
        {
            OpenDropdownMenu();
        }

        base.OnClick();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == DropdownMenuProperty)
        {
            DropdownMenu?.Opening -= DropdownMenu_Opening;
            DropdownMenu?.Opened -= DropdownMenu_Opened;
            DropdownMenu?.Closing -= DropdownMenu_Closing;
            DropdownMenu?.Closed -= DropdownMenu_Closed;

            DropdownMenu?.Opening += DropdownMenu_Opening;
            DropdownMenu?.Opened += DropdownMenu_Opened;
            DropdownMenu?.Closing += DropdownMenu_Closing;
            DropdownMenu?.Closed += DropdownMenu_Closed;
        }
        else if (e.Property == TextProperty)
        {
            RaisePropertyChanged(IsTextVisibleProperty, default, IsTextVisible);
            UpdateContentSpacing();
        }
        else if (e.Property == IconDataProperty)
        {
            RaisePropertyChanged(IsIconVisibleProperty, default, IsIconVisible);
        }
        else if (e.Property == IsPressedProperty || e.Property == IsPointerOverProperty)
        {
            UpdateContentVisual();
        }
        else if (e.Property == ForegroundProperty || e.Property == IconStrokeThicknessProperty)
        {
            UpdateIconPaint();
        }
        else if (e.Property == VariantProperty)
        {
            ApplyVariant();
        }
    }

    private void ApplyVariant()
    {
        Classes.Remove("accent");
        Classes.Remove("link");
        Classes.Remove("outline");
        ClearValue(ForegroundProperty);
        ClearValue(BackgroundProperty);
        ClearValue(BorderThicknessProperty);
        ClearValue(CursorProperty);

        switch (Variant)
        {
            case PhButtonVariant.Accent:
                Classes.Add("accent");
                this[!ForegroundProperty] = Resx.CreateBinding(ResxId.AccentButtonForeground);
                break;

            case PhButtonVariant.Link:
                Classes.Add("link");
                Background = Brushes.Transparent;
                BorderThickness = new Thickness(0);
                Cursor = new Cursor(StandardCursorType.Hand);
                this[!PaddingProperty] = this[!PaddingProperty];
                this[!ForegroundProperty] = Resx.CreateBinding(ResxId.IG_TextAccentColor);
                break;

            case PhButtonVariant.Outline:
                Classes.Add("outline");
                this[!ForegroundProperty] = Resx.CreateBinding(ResxId.IG_TextAccentColor);
                break;

            case PhButtonVariant.Default:
            default:
                break;
        }

        UpdateOutlineBorderBrush();
        UpdateContentVisual();
    }

    private void UpdateOutlineBorderBrush()
    {
        if (Variant != PhButtonVariant.Outline)
        {
            Resources.Remove("PhOutlineButtonBorderBrush");
            return;
        }

        if (!this.TryFindResource("SystemAccentColor", ActualThemeVariant, out var res)
            || res is not Color accent)
        {
            return;
        }

        var soft = accent.WithAlpha(0x50);
        var strong = accent.WithAlpha(0xA0);

        Resources["PhOutlineButtonBorderBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(soft, 0.0),
                new GradientStop(soft, 0.9),
                new GradientStop(strong, 1.0),
            },
        };
    }

    private void DropdownMenu_Opening(object? sender, CancelEventArgs e)
    {
        OnIgDropdownMenuOpening(e);
    }

    private void DropdownMenu_Opened(object? sender, RoutedEventArgs e)
    {
        OnIgDropdownMenuOpened(e);
    }

    private void DropdownMenu_Closing(object? sender, CancelEventArgs e)
    {
        OnIgDropdownMenuClosing(e);
    }

    private void DropdownMenu_Closed(object? sender, RoutedEventArgs e)
    {
        OnIgDropdownMenuClosed(e);
    }

    private void Core_ThemeChanged(object? sender, ThemePackChangedEventArgs e)
    {
        UpdateOutlineBorderBrush();
        OnIgThemeChanged(e);
    }

    private void Core_LanguageChanged(object? sender, EventArgs e)
    {
        OnIgLanguageChanged();
    }

    protected virtual void OnIgThemeChanged(ThemePackChangedEventArgs e) { }

    protected virtual void OnIgLanguageChanged() { }

    protected virtual void OnIgDropdownMenuOpening(CancelEventArgs e)
    {
        DropdownOpening?.Invoke(DropdownMenu!, e);
    }

    protected virtual void OnIgDropdownMenuOpened(RoutedEventArgs e)
    {
        DropdownOpened?.Invoke(DropdownMenu!, e);
    }

    protected virtual void OnIgDropdownMenuClosing(CancelEventArgs e)
    {
        DropdownClosing?.Invoke(DropdownMenu!, e);
    }

    protected virtual void OnIgDropdownMenuClosed(RoutedEventArgs e)
    {
        DropdownClosed?.Invoke(DropdownMenu!, e);
    }

    private DockPanel CreateContentElement()
    {
        var pathEl = _iconEl = new Path
        {
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            [!Path.DataProperty] = this[!IconDataProperty],
            [!Path.IsVisibleProperty] = this[!IsIconVisibleProperty],
        };
        DockPanel.SetDock(pathEl, Dock.Left);

        var textEl = _textEl = new TextBlock
        {
            [!TextBlock.TextProperty] = this[!TextProperty],
            [!TextBlock.ForegroundProperty] = this[!ForegroundProperty],
            [!TextBlock.IsVisibleProperty] = this[!IsTextVisibleProperty],
            [!TextBlock.TextTrimmingProperty] = this[!TextTrimmingProperty],
            [!TextBlock.TextWrappingProperty] = this[!TextWrappingProperty],
        };

        var panel = new DockPanel
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        panel.Children.AddRange([pathEl, textEl]);

        return panel;
    }

    private void UpdateContentSpacing()
    {
        if (_iconEl is null) return;

        _iconEl.Margin = IsTextVisible ? ICON_GAP : default;
    }

    private void UpdateIconPaint()
    {
        if (_iconEl is null) return;

        var isOutlined = IconStrokeThickness > 0;
        _iconEl.StrokeThickness = IconStrokeThickness;
        _iconEl.Fill = isOutlined ? null : Foreground;
        _iconEl.Stroke = isOutlined ? Foreground : null;
    }

    private void UpdateContentVisual()
    {
        if (Content is Visual content)
        {
            content.RenderTransform = IsPressed ? new TranslateTransform(0, 0.5) : null;
            content.Opacity = IsPressed ? 0.6 : 1d;
        }

        if (Variant == PhButtonVariant.Link && _textEl is not null)
        {
            _textEl.TextDecorations = IsPointerOver || IsPressed
                ? TextDecorations.Underline
                : null;
        }
    }

    public void OpenDropdownMenu(PlacementMode? placement = null)
    {
        if (DropdownMenu is null) return;

        DropdownMenu.Placement = placement ?? PlacementMode.BottomEdgeAlignedRight;
        DropdownMenu.Open(this);
    }

}

public enum PhButtonVariant
{

    Default,

    Accent,

    Link,

    Outline,
}
