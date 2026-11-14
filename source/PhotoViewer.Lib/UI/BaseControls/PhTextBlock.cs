using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using System;

namespace PhotoViewer.UI;

public class PhTextBlock : TextBlock
{

    protected override Type StyleKeyOverride => typeof(TextBlock);

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
    }

    private void Core_LanguageChanged(object? sender, System.EventArgs e)
    {
        LocalizeText();
    }

    private void LocalizeText()
    {
        var localizedText = Core.Lang[LangKey, LangParams];
        if (string.IsNullOrWhiteSpace(localizedText)) return;

        Text = localizedText;
    }

}
