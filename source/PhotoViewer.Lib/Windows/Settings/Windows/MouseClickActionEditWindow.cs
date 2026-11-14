using PhotoViewer.Common.Actions;
using PhotoViewer.Common.Localization;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;

namespace PhotoViewer.Common.Windows;

internal sealed class MouseClickActionEditWindow : DialogWindow
{
    private readonly PhActionEditorControl _editor;
    private readonly string _eventLabel;
    private readonly bool _isReadOnly;

    protected override int MIN_WIDTH => 460;
    protected override int MAX_WIDTH => 460;

    public SingleAction? ResultAction { get; private set; }

    public MouseClickActionEditWindow(string eventLabel, SingleAction? action, bool isReadOnly = false)
    {
        _eventLabel = eventLabel;
        _isReadOnly = isReadOnly;

        IsButton2Visible = true;
        IsButton3Visible = false;

        IsButton1Visible = !_isReadOnly;
        DefaultButton = _isReadOnly ? DialogButton.Button2 : DialogButton.Button1;
        DefaultFocus = _isReadOnly ? DialogFocus.Button2 : DialogFocus.Default;

        _editor = new PhActionEditorControl
        {
            ShowHotkeys = false,
            IsExecutableRequired = false,
            IsEnabled = !_isReadOnly,
        };
        _editor.LoadAction(action);
        DialogContent = _editor;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = $"{_eventLabel} – {Core.Lang[LangId.Settings_MouseClickAction]}";
        Button1Text = Core.Lang[LangId._OK];
        Button2Text = Core.Lang[_isReadOnly ? LangId._Close : LangId._Cancel];
    }

    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        if (_isReadOnly)
        {
            e.CanProceed = false;
            return;
        }

        if (!_editor.ValidateExecutable())
        {
            e.CanProceed = false;
            return;
        }

        ResultAction = new SingleAction(
            _editor.Executable?.Trim() ?? string.Empty,
            _editor.Argument?.Trim() ?? string.Empty);

        base.OnDialogSubmitted(e);
    }

}
