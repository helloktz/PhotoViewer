using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using System;
using System.ComponentModel;

namespace PhotoViewer.UI;

public partial class PhToolButton : ToggleButton
{
    protected override Type StyleKeyOverride => typeof(ToggleButton);

    public event TEventHandler<ContextMenu, CancelEventArgs>? DropdownOpening;
    public event TEventHandler<ContextMenu, RoutedEventArgs>? DropdownOpened;
    public event TEventHandler<ContextMenu, CancelEventArgs>? DropdownClosing;
    public event TEventHandler<ContextMenu, RoutedEventArgs>? DropdownClosed;

    public double Dpi => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;

    public bool IsCheckOnClick
    {
        get => GetValue(IsCheckOnClickProperty);
        set => SetValue(IsCheckOnClickProperty, value);
    }
    public static readonly StyledProperty<bool> IsCheckOnClickProperty =
        AvaloniaProperty.Register<PhToolButton, bool>(nameof(IsCheckOnClick));

    public bool IsOpenDropdownMenuOnClick
    {
        get => GetValue(IsOpenDropdownMenuOnClickProperty);
        set => SetValue(IsOpenDropdownMenuOnClickProperty, value);
    }
    public static readonly StyledProperty<bool> IsOpenDropdownMenuOnClickProperty =
        AvaloniaProperty.Register<PhToolButton, bool>(nameof(IsOpenDropdownMenuOnClick));

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
        AvaloniaProperty.Register<PhToolButton, ContextMenu?>(nameof(DropdownMenu));

    static PhToolButton()
    {
        ThemeProperty.OverrideDefaultValue<PhToolButton>(new ControlTheme(typeof(ToggleButton))
        {
            Setters =
            {
                new Setter(BackgroundProperty, Resx.CreateBinding(ResxId.IG_ToolButtonBackground)),
                new Setter(BorderBrushProperty, Resx.CreateBinding(ResxId.IG_ToolButtonBackground)),
                new Setter(CornerRadiusProperty, Resx.CreateBinding(ResxId.ControlCornerRadius)),
                new Setter(TransitionsProperty, new Transitions
                {
                    new BrushTransition {
                        Property = BackgroundProperty,
                        Duration = TimeSpan.FromMilliseconds(50),
                    },
                    new BrushTransition {
                        Property = BorderBrushProperty,
                        Duration = TimeSpan.FromMilliseconds(50),
                    },
                    new TransformOperationsTransition {
                        Property = RenderTransformProperty,
                        Duration = TimeSpan.FromMilliseconds(50),
                    },
                }),
            },
            Children =
            {
                new Style(x => x.Nesting().Class(":checked")
                    .Not(y => y.Class(":disabled")))
                {
                    Setters = {
                        new Setter(BackgroundProperty, Resx.CreateBinding(ResxId.IG_ToolButtonBackgroundChecked)),
                        new Setter(BorderBrushProperty, Resx.CreateBinding(ResxId.IG_ToolButtonBackgroundChecked)),
                    },
                },
                new Style(x => x.Nesting().Class(":pressed")
                    .Not(y => y.Class(":disabled")))
                {
                    Setters = {
                        new Setter(BackgroundProperty, Resx.CreateBinding(ResxId.IG_ToolButtonBackgroundPressed)),
                        new Setter(BorderBrushProperty, Resx.CreateBinding(ResxId.IG_ToolButtonBackgroundPressed)),
                        new Setter(RenderTransformProperty, new ScaleTransform(0.95, 0.95)),
                        new Setter(TransitionsProperty, new Transitions
                        {
                            new BrushTransition {
                                Property = BackgroundProperty,
                                Duration = TimeSpan.FromMilliseconds(30),
                            },
                            new BrushTransition {
                                Property = BorderBrushProperty,
                                Duration = TimeSpan.FromMilliseconds(30),
                            },
                        }),
                    }
                },
                new Style(x => x.Nesting().Class(":pointerover")
                    .Not(y => y.Class(":pressed"))
                    .Not(y => y.Class(":disabled")))
                {
                    Setters = {
                        new Setter(BackgroundProperty, Resx.CreateBinding(ResxId.IG_ToolButtonBackgroundHover)),
                        new Setter(BorderBrushProperty, Resx.CreateBinding(ResxId.IG_ToolButtonBackgroundHover)),
                    },
                },
            },
        });
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        OnIgLanguageChanged();
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

    protected override void Toggle()
    {
        if (!IsCheckOnClick) return;

        base.Toggle();
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

    public void OpenDropdownMenu(PlacementMode? placement = null)
    {
        if (DropdownMenu is null) return;

        DropdownMenu.Placement = placement ?? PlacementMode.BottomEdgeAlignedRight;
        DropdownMenu.Open(this);
    }

}
