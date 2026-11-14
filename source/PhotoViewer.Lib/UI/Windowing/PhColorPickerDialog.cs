using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using System;
using System.Globalization;

namespace PhotoViewer.UI.Windowing;

public partial class PhColorPickerDialog : DialogWindow
{
    private const double SPECTRUM_SIZE = 280;
    private const double SLIDER_WIDTH = 20;
    private const double INPUT_WIDTH = 100;

    private ColorSpectrum _colorSpectrum = null!;
    private ColorSlider _colorSlider = null!;
    private ColorSlider _alphaSlider = null!;
    private TextBox _txtHex = null!;
    private NumericUpDown _numR = null!;
    private NumericUpDown _numG = null!;
    private NumericUpDown _numB = null!;
    private NumericUpDown _numA = null!;
    private Grid _currentColorPreview = null!;
    private Grid _previousColorPreview = null!;
    private SolidColorBrush _currentColorPreviewBrush = null!;
    private SolidColorBrush _previousColorPreviewBrush = null!;
    private PhButton _btnReset = null!;
    private Color _previousColor = Const.COLOR_EMPTY;
    private HsvColor _selectedHsvColor = Const.COLOR_EMPTY.ToHsv();
    private bool _isUpdatingColor;

    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }
    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<PhColorPickerDialog, Color>(nameof(SelectedColor), Const.COLOR_EMPTY);

    public Color DefaultColor
    {
        get => GetValue(DefaultColorProperty);
        set => SetValue(DefaultColorProperty, value);
    }
    public static readonly StyledProperty<Color> DefaultColorProperty =
        AvaloniaProperty.Register<PhColorPickerDialog, Color>(nameof(DefaultColor), Const.COLOR_EMPTY);

    public PhColorPickerDialog(Color? initColor = null, Color? defaultColor = null)
    {
        Title = string.Empty;
        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Default;

        if (initColor != null)
        {
            SelectedColor = initColor.Value;
            _previousColor = initColor.Value;
            _selectedHsvColor = initColor.Value.ToHsv();
        }
        if (defaultColor != null) DefaultColor = defaultColor.Value;

        DialogContent = CreateDialogContentElement();
        DialogFooterLeftContent = CreateDialogFooterLeftContentElement();
        UpdatePickerControlsFromSelectedColor();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == SelectedColorProperty && !_isUpdatingColor)
        {
            UpdatePickerControlsFromSelectedColor();
        }
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Button1Text = Core.Lang[LangId._OK];
        Button2Text = Core.Lang[LangId._Cancel];
        _btnReset.Text = Core.Lang[LangId._Reset];
    }

    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        CommitHexTextInput();

        base.OnDialogSubmitted(e);
    }

    private Grid CreateDialogContentElement()
    {
        _currentColorPreview = CreateColorPreview(
            SelectedColor,
            out _currentColorPreviewBrush);
        _previousColorPreview = CreateColorPreview(
            _previousColor,
            out _previousColorPreviewBrush);

        var previewGrid = new Grid
        {
            ColumnDefinitions = new("*,*"),
        };
        var previewBorder = new Border
        {
            Child = previewGrid,
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 0, 0, 8),
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,

            [!Border.BorderBrushProperty] = Resx.CreateBinding(ResxId.IG_BorderControlBrush),
        };
        previewGrid.Children.Add(_currentColorPreview);
        Grid.SetColumn(_previousColorPreview, 1);
        previewGrid.Children.Add(_previousColorPreview);

        _colorSpectrum = new ColorSpectrum
        {
            Width = SPECTRUM_SIZE,
            Height = SPECTRUM_SIZE,
            Components = ColorSpectrumComponents.HueSaturation,
            Shape = ColorSpectrumShape.Box,
            HsvColor = SelectedColor.ToHsv(),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _colorSpectrum.ColorChanged += ColorControl_ColorChanged;

        _colorSlider = CreateVerticalColorSlider(ColorComponent.Component3, false);
        _colorSlider.ColorChanged += ColorControl_ColorChanged;
        _alphaSlider = CreateVerticalColorSlider(ColorComponent.Alpha, true);
        _alphaSlider.ColorChanged += ColorControl_ColorChanged;

        var inputsGrid = CreateInputsGrid();
        var rightPanel = new StackPanel
        {
            Spacing = 12,
        };
        rightPanel.Children.AddRange([previewBorder, inputsGrid]);

        var root = new Grid
        {
            ColumnDefinitions = new($"{SPECTRUM_SIZE},{SLIDER_WIDTH},{SLIDER_WIDTH},Auto"),
            ColumnSpacing = 12,
        };

        var colorSpectrumHost = new Border
        {
            Width = SPECTRUM_SIZE,
            Height = SPECTRUM_SIZE,
            ClipToBounds = true,
            Child = _colorSpectrum,
        };
        root.Children.Add(colorSpectrumHost);

        Grid.SetColumn(_colorSlider, 1);
        root.Children.Add(_colorSlider);

        Grid.SetColumn(_alphaSlider, 2);
        root.Children.Add(_alphaSlider);

        Grid.SetColumn(rightPanel, 3);
        root.Children.Add(rightPanel);

        return root;
    }

    private StackPanel CreateDialogFooterLeftContentElement()
    {
        _btnReset = new PhButton
        {
            MinWidth = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _btnReset.Click += (_, _) =>
        {
            SelectedColor = DefaultColor;
        };

        var root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        root.Children.Add(_btnReset);

        return root;
    }

    private static Grid CreateColorPreview(Color color, out SolidColorBrush colorBrush)
    {
        colorBrush = new SolidColorBrush(color);

        return new Grid
        {
            Children =
                {
                    new Border
                    {
                        [!Border.BackgroundProperty] = new DynamicResourceExtension("ColorControlCheckeredBackgroundBrush"),
                    },
                    new Border
                    {
                        Background = colorBrush,
                    },
                },
        };
    }

    private static ColorSlider CreateVerticalColorSlider(ColorComponent component, bool isAlphaVisible)
    {
        return new ColorSlider
        {
            Width = SLIDER_WIDTH,
            Height = SPECTRUM_SIZE,
            BorderThickness = new Thickness(1),
            Orientation = Orientation.Vertical,
            ColorModel = ColorModel.Hsva,
            ColorComponent = component,
            IsAlphaVisible = isAlphaVisible,
            IsPerceptive = true,
            HsvColor = Colors.White.ToHsv(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,

            [!TemplatedControl.BorderBrushProperty] = Resx.CreateBinding(ResxId.IG_BorderControlBrush),
        };
    }

    private Grid CreateInputsGrid()
    {
        _txtHex = new TextBox
        {
            MinWidth = INPUT_WIDTH,
            MaxLength = 9,
            Text = SelectedColor.ToHex(),
            TextAlignment = TextAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _txtHex.LostFocus += HexTextBox_LostFocus;

        _numR = CreateByteInput();
        _numG = CreateByteInput();
        _numB = CreateByteInput();
        _numA = CreateByteInput();

        var root = new Grid
        {
            ColumnDefinitions = new($"Auto,{INPUT_WIDTH}"),
            RowDefinitions = new("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
        };

        AddInputRow(root, 0, "HEX", _txtHex);
        AddInputRow(root, 1, "R", _numR);
        AddInputRow(root, 2, "G", _numG);
        AddInputRow(root, 3, "B", _numB);
        AddInputRow(root, 4, "A", _numA);

        return root;
    }

    private NumericUpDown CreateByteInput()
    {
        var input = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 255,
            Increment = 1,
            ClipValueToMinMax = true,
            FormatString = "N0",
            ParsingNumberStyle = NumberStyles.Integer,
            ShowButtonSpinner = false,
            MinWidth = INPUT_WIDTH,
            TextAlignment = TextAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        input.ValueChanged += ComponentInput_ValueChanged;

        return input;
    }

    private static void AddInputRow(Grid root, int row, string label, Control input)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            Opacity = 0.65,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetRow(labelBlock, row);
        root.Children.Add(labelBlock);

        Grid.SetColumn(input, 1);
        Grid.SetRow(input, row);
        root.Children.Add(input);
    }

    private void ColorControl_ColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (_isUpdatingColor) return;

        var hsvColor = sender switch
        {
            ColorSpectrum colorSpectrum => colorSpectrum.HsvColor,
            ColorSlider colorSlider => colorSlider.HsvColor,
            _ => e.NewColor.ToHsv(),
        };

        SetSelectedHsvColorFromInput(hsvColor);
    }

    private void ComponentInput_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isUpdatingColor) return;

        var currentColor = SelectedColor;
        var color = Color.FromArgb(
            GetByteInputValue(_numA, currentColor.A),
            GetByteInputValue(_numR, currentColor.R),
            GetByteInputValue(_numG, currentColor.G),
            GetByteInputValue(_numB, currentColor.B));

        SetSelectedColorFromInput(color);
    }

    private void HexTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        CommitHexTextInput();
    }

    private void CommitHexTextInput()
    {
        if (_isUpdatingColor) return;

        if (TryParseHexColor(_txtHex.Text, out var color))
        {
            SetSelectedColorFromInput(color);
            return;
        }

        UpdatePickerControlsFromSelectedColor();
    }

    private void SetSelectedColorFromInput(Color color)
    {
        _isUpdatingColor = true;
        try
        {
            _selectedHsvColor = color.ToHsv();
            SelectedColor = color;

            UpdatePickerControls(_selectedHsvColor);
        }
        finally
        {
            _isUpdatingColor = false;
        }
    }

    private void SetSelectedHsvColorFromInput(HsvColor hsvColor)
    {
        _isUpdatingColor = true;
        try
        {
            _selectedHsvColor = hsvColor;
            SelectedColor = hsvColor.ToRgb();

            UpdatePickerControls(hsvColor);
        }
        finally
        {
            _isUpdatingColor = false;
        }
    }

    private void UpdatePickerControlsFromSelectedColor()
    {
        _isUpdatingColor = true;
        try
        {
            _selectedHsvColor = SelectedColor.ToHsv();

            UpdatePickerControls(_selectedHsvColor);
        }
        finally
        {
            _isUpdatingColor = false;
        }
    }

    private void UpdatePickerControls(HsvColor hsvColor)
    {
        if (_colorSpectrum is null
            || _colorSlider is null
            || _alphaSlider is null
            || _txtHex is null
            || _numR is null
            || _numG is null
            || _numB is null
            || _numA is null
            || _currentColorPreviewBrush is null
            || _previousColorPreviewBrush is null)
        {
            return;
        }

        var color = hsvColor.ToRgb();

        _colorSpectrum.HsvColor = hsvColor;
        _colorSlider.HsvColor = hsvColor;
        _alphaSlider.HsvColor = hsvColor;

        _txtHex.Text = color.ToHex();
        _numR.Value = color.R;
        _numG.Value = color.G;
        _numB.Value = color.B;
        _numA.Value = color.A;

        _currentColorPreviewBrush.Color = color;
        _previousColorPreviewBrush.Color = _previousColor;
    }

    private static byte GetByteInputValue(NumericUpDown input, byte fallback)
    {
        if (input.Value is not { } value) return fallback;

        var roundedValue = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp(roundedValue, 0, 255);
    }

    private static bool TryParseHexColor(string? text, out Color color)
    {
        color = Const.COLOR_EMPTY;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var colorText = text.Trim();
        var hex = colorText.StartsWith('#') ? colorText[1..] : colorText;

        if (hex.Length is not (3 or 4 or 6 or 8)) return false;

        foreach (var c in hex.AsSpan())
        {
            if (!Uri.IsHexDigit(c)) return false;
        }

        color = BHelper.ColorFromHex(colorText, skipAlpha: false);
        return true;
    }

}
