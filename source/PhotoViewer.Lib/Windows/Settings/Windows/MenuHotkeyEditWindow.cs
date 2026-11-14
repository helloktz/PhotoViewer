using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System.Collections.Generic;

namespace PhotoViewer.Common.Windows;

internal sealed class MenuHotkeyEditWindow : DialogWindow
{
    private readonly string _actionPath;
    private readonly Hotkey[] _defaultHotkeys;
    private readonly PhHotkeyPicker _picker;
    private readonly PhButton _restoreBtn;
    private readonly PhTextBlock _pathLabel;
    private readonly WrapPanel _defaultChips;
    private readonly PhTextBlock? _defaultEmptyLabel;

    protected override int MIN_WIDTH => 460;
    protected override int MAX_WIDTH => 460;

    public Hotkey[]? ResultHotkeys { get; private set; }

    public MenuHotkeyEditWindow(string actionPath, IReadOnlyList<Hotkey> current, IReadOnlyList<Hotkey> defaultHotkeys)
    {
        _actionPath = actionPath;
        _defaultHotkeys = [.. defaultHotkeys];

        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Default;

        PressEnterToSubmit = false;

        _pathLabel = new PhTextBlock
        {
            Text = actionPath,
            FontWeight = FontWeight.SemiBold
        };
        _picker = new PhHotkeyPicker { Hotkeys = [.. current] };
        _restoreBtn = new PhButton { Variant = PhButtonVariant.Link };
        _restoreBtn.Click += (_, _) => _picker.Hotkeys = [.. _defaultHotkeys];

        _defaultChips = new WrapPanel { Opacity = 0.85 };
        if (_defaultHotkeys.Length > 0)
        {
            foreach (var hk in _defaultHotkeys)
            {
                _defaultChips.Children.Add(new PhHotkeyChip(hk.KeyString)
                {
                    Margin = new Thickness(0, 0, 6, 6),
                });
            }
        }
        else
        {
            _defaultEmptyLabel = new PhTextBlock { Opacity = 0.7 };
            _defaultChips.Children.Add(_defaultEmptyLabel);
        }

        var footer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Children = { _restoreBtn, _defaultChips },
        };

        DialogContent = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            Children = { _pathLabel, _picker, footer },
        };
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Settings_Keyboard_EditTitle];
        Button1Text = Core.Lang[LangId._OK];
        Button2Text = Core.Lang[LangId._Cancel];

        _restoreBtn.Text = Core.Lang[LangId._ResetToDefault];
        _picker.PlaceholderText = Core.Lang[LangId.Settings_Toolbar_RecordHotkeyHint];

        if (_defaultEmptyLabel is not null) _defaultEmptyLabel.Text = Core.Lang[LangId._Empty];
    }

    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        ResultHotkeys = [.. _picker.Hotkeys];
        base.OnDialogSubmitted(e);
    }

}
