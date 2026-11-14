using Avalonia.Controls;
using Avalonia.Layout;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.UI.Windowing;

namespace PhotoViewer.Tools;

internal class ColorPickerSettingsWindow : DialogWindow
{
    private CheckBox _chkShowRgbA = null!;
    private CheckBox _chkShowHexA = null!;
    private CheckBox _chkShowHslA = null!;
    private CheckBox _chkShowHsvA = null!;
    private CheckBox _chkShowCmykA = null!;
    private CheckBox _chkShowCIELabA = null!;

    public ColorPickerConfig ResultConfig { get; private set; } = new();

    public ColorPickerSettingsWindow(ColorPickerConfig config)
    {
        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = false;

        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Button1;

        DialogContent = CreateDialogContentElement();
        LoadConfigValues(config);
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Tool_ColorPicker_Title];
        Button1Text = Core.Lang[LangId._OK];
        Button2Text = Core.Lang[LangId._Cancel];

        _chkShowRgbA.Content = Core.Lang[LangId.Tool_ColorPicker_ChkShowRgbA];
        _chkShowHexA.Content = Core.Lang[LangId.Tool_ColorPicker_ChkShowHexA];
        _chkShowHslA.Content = Core.Lang[LangId.Tool_ColorPicker_ChkShowHslA];
        _chkShowHsvA.Content = Core.Lang[LangId.Tool_ColorPicker_ChkShowHsvA];
        _chkShowCmykA.Content = Core.Lang[LangId.Tool_ColorPicker_ChkShowCmykA];
        _chkShowCIELabA.Content = Core.Lang[LangId.Tool_ColorPicker_ChkShowCIELabA];
    }

    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        ResultConfig = new ColorPickerConfig
        {
            ShowRgbWithAlpha = _chkShowRgbA.IsChecked == true,
            ShowHexWithAlpha = _chkShowHexA.IsChecked == true,
            ShowHslWithAlpha = _chkShowHslA.IsChecked == true,
            ShowHsvWithAlpha = _chkShowHsvA.IsChecked == true,
            ShowCmykWithAlpha = _chkShowCmykA.IsChecked == true,
            ShowCIELabWithAlpha = _chkShowCIELabA.IsChecked == true,
        };

        base.OnDialogSubmitted(e);
    }

    private void LoadConfigValues(ColorPickerConfig config)
    {
        _chkShowRgbA.IsChecked = config.ShowRgbWithAlpha;
        _chkShowHexA.IsChecked = config.ShowHexWithAlpha;
        _chkShowHslA.IsChecked = config.ShowHslWithAlpha;
        _chkShowHsvA.IsChecked = config.ShowHsvWithAlpha;
        _chkShowCmykA.IsChecked = config.ShowCmykWithAlpha;
        _chkShowCIELabA.IsChecked = config.ShowCIELabWithAlpha;
    }

    private StackPanel CreateDialogContentElement()
    {
        _chkShowRgbA = new CheckBox();
        _chkShowHexA = new CheckBox();
        _chkShowHslA = new CheckBox();
        _chkShowHsvA = new CheckBox();
        _chkShowCmykA = new CheckBox();
        _chkShowCIELabA = new CheckBox();

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            MinWidth = 300,
        };

        root.Children.AddRange([
            _chkShowRgbA,
            _chkShowHexA,
            _chkShowHslA,
            _chkShowHsvA,
            _chkShowCmykA,
            _chkShowCIELabA,
        ]);

        return root;
    }

}
