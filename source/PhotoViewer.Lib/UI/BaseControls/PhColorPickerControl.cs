using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Windowing;
using System;

namespace PhotoViewer.UI;

public class PhColorPickerControl : PhControl
{
    private Border _swatchBox = null!;
    private Border _swatch = null!;
    private SelectableTextBlock _hexLabel = null!;
    private PhButton _resetButton = null!;

    public event TEventHandler<PhColorPickerControl, EventArgs>? ColorChanged;

    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }
    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<PhColorPickerControl, Color>(nameof(SelectedColor));

    public Color DefaultColor
    {
        get => GetValue(DefaultColorProperty);
        set => SetValue(DefaultColorProperty, value);
    }
    public static readonly StyledProperty<Color> DefaultColorProperty =
        AvaloniaProperty.Register<PhColorPickerControl, Color>(nameof(DefaultColor));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PhColorPickerControl, string>(nameof(Title), string.Empty);

    public bool ShowResetButton
    {
        get => GetValue(ShowResetButtonProperty);
        set => SetValue(ShowResetButtonProperty, value);
    }
    public static readonly StyledProperty<bool> ShowResetButtonProperty =
        AvaloniaProperty.Register<PhColorPickerControl, bool>(nameof(ShowResetButton), true);

    public bool ShowHexLabel
    {
        get => GetValue(ShowHexLabelProperty);
        set => SetValue(ShowHexLabelProperty, value);
    }
    public static readonly StyledProperty<bool> ShowHexLabelProperty =
        AvaloniaProperty.Register<PhColorPickerControl, bool>(nameof(ShowHexLabel), true);

    public double SwatchWidth
    {
        get => GetValue(SwatchWidthProperty);
        set => SetValue(SwatchWidthProperty, value);
    }
    public static readonly StyledProperty<double> SwatchWidthProperty =
        AvaloniaProperty.Register<PhColorPickerControl, double>(nameof(SwatchWidth), 80);

    public PhColorPickerControl()
    {
        Content = BuildContent();
        UpdateSwatch();
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();
        _resetButton.Text = Core.Lang[LangId._Reset];
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == SelectedColorProperty)
        {
            UpdateSwatch();
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Property == ShowResetButtonProperty)
        {
            _resetButton.IsVisible = ShowResetButton;
        }
        else if (e.Property == ShowHexLabelProperty)
        {
            _hexLabel.IsVisible = ShowHexLabel;
        }
        else if (e.Property == SwatchWidthProperty)
        {
            _swatchBox.Width = SwatchWidth;
        }
    }

    private StackPanel BuildContent()
    {
        var checker = new Border { CornerRadius = new CornerRadius(3) };
        checker[!BackgroundProperty] = new DynamicResourceExtension("ColorControlCheckeredBackgroundBrush");

        _swatch = new Border { CornerRadius = new CornerRadius(3) };

        _swatchBox = new Border
        {
            Width = SwatchWidth,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Child = new Grid { Children = { checker, _swatch } },
        };

        var swatchButton = new PhButton
        {
            Padding = new Thickness(6),
            Content = _swatchBox,
        };
        swatchButton[!CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius);
        swatchButton.Click += async (_, _) => await PickColorAsync();

        _hexLabel = new SelectableTextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily(Const.FONT_CODE),
            IsVisible = ShowHexLabel,
        };

        _resetButton = new PhButton
        {
            VerticalAlignment = VerticalAlignment.Center,
            Variant = PhButtonVariant.Link,
            Text = Core.Lang[LangId._Reset],
            IsVisible = ShowResetButton,
        };
        _resetButton.Click += (_, _) => SelectedColor = DefaultColor;

        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Orientation = Orientation.Horizontal,
            Spacing = 10,
        };
        panel.Children.AddRange([swatchButton, _hexLabel, _resetButton]);

        return panel;
    }

    private async System.Threading.Tasks.Task PickColorAsync()
    {
        var dialog = new PhColorPickerDialog(SelectedColor, DefaultColor) { Title = Title };
        if (await dialog.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) == DialogExitCode.OK)
        {
            SelectedColor = dialog.SelectedColor;
        }
    }

    private void UpdateSwatch()
    {
        _swatch.Background = new SolidColorBrush(SelectedColor);
        _hexLabel.Text = SelectedColor.ToHex();
    }

}
