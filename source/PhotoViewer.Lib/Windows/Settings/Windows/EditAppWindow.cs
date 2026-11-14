using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Windowing;

namespace PhotoViewer.Common.Windows;

internal sealed class EditAppWindow : DialogWindow
{
    private readonly bool _isEditMode;
    private readonly EditAppWindowView _view;

    protected override int MIN_WIDTH => 500;
    protected override int MAX_WIDTH => 500;

    public string ResultExtKey { get; private set; } = string.Empty;

    public EditingApp ResultApp { get; private set; } = new();

    public EditAppWindow(string? extKey = null, EditingApp? app = null)
    {
        _isEditMode = !string.IsNullOrEmpty(extKey);

        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Default;

        _view = new EditAppWindowView();
        _view.LoadData(extKey, app);
        DialogContent = _view;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[_isEditMode
            ? LangId.Settings_EditAppDialog_EditApp
            : LangId.Settings_EditAppDialog_AddApp];
        Button1Text = Core.Lang[LangId._OK];
        Button2Text = Core.Lang[LangId._Cancel];
    }

    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        if (!_view.Validate())
        {
            e.CanProceed = false;
            return;
        }

        ResultExtKey = _view.ResultExtKey;
        ResultApp = _view.BuildApp();

        base.OnDialogSubmitted(e);
    }

}
