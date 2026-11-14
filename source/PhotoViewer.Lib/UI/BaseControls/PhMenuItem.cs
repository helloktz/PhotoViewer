using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using System;

namespace PhotoViewer.UI;

public class PhMenuItem : MenuItem
{
    protected override Type StyleKeyOverride => typeof(MenuItem);

    private bool _disabledByGate;

    public LangId? LangKey
    {
        get => GetValue(LangKeyProperty);
        set => SetValue(LangKeyProperty, value);
    }
    public static readonly StyledProperty<LangId?> LangKeyProperty =
        AvaloniaProperty.Register<PhMenuItem, LangId?>(nameof(LangKey));

    public object? LangParams
    {
        get => GetValue(LangParamsProperty);
        set => SetValue(LangParamsProperty, value);
    }
    public static readonly StyledProperty<object?> LangParamsProperty =
        AvaloniaProperty.Register<PhMenuItem, object?>(nameof(LangParams));

    public string? HotkeyText
    {
        get => GetValue(HotkeyTextProperty);
        set => SetValue(HotkeyTextProperty, value);
    }
    public static readonly StyledProperty<string?> HotkeyTextProperty =
        AvaloniaProperty.Register<PhMenuItem, string?>(nameof(HotkeyText));

    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }
    public static readonly StyledProperty<Geometry?> IconDataProperty =
        AvaloniaProperty.Register<PhMenuItem, Geometry?>(nameof(IconData));

    public PhMenuItem() { }

    public PhMenuItem(object? header = null)
    {
        Header = header;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<TextBlock>("PART_InputGestureText") is not TextBlock gestureEl) return;
        gestureEl.ClearValue(TextBlock.TextProperty);
        gestureEl[!TextBlock.TextProperty] = this[!HotkeyTextProperty];
        gestureEl.FontSize = Const.FONT_SIZE_SMALL;

        if (e.NameScope.Find<Path>("PART_ChevronPath") is not Path chevronEl) return;
        chevronEl.Width = 10;
        chevronEl.Height = 10;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        LocalizeText();

        Core.LanguageChanged += Core_LanguageChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        Core.LanguageChanged -= Core_LanguageChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == LangKeyProperty || e.Property == LangParamsProperty)
        {
            LocalizeText();
        }
        else if (e.Property == IconDataProperty)
        {
            Icon = IconData is null ? null : new PathIcon { Data = IconData };
        }
    }

    private void Core_LanguageChanged(object? sender, System.EventArgs e)
    {
        LocalizeText();
    }

    public void LocalizeText()
    {
        if (LangKey is not { } key) return;
        var localizedText = Core.Lang[key, LangParams];

        if (FeatureManager.IsLocked(LangKey))
        {
            Header = $"{localizedText} 🔒";
            Disable();
        }
        else
        {
            Header = localizedText;
            RestoreEnabled();
        }
    }

    private void Disable()
    {
        _disabledByGate = true;
        IsEnabled = false;
    }

    private void RestoreEnabled()
    {
        if (!_disabledByGate) return;

        _disabledByGate = false;
        IsEnabled = true;
    }

}
