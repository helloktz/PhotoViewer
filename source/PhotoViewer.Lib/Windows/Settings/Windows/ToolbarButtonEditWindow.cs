using Avalonia;
using PhotoViewer.Common.Localization;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System.Collections.Generic;

namespace PhotoViewer.Common.Windows;

internal sealed class ToolbarButtonEditWindow : DialogWindow
{
    private readonly bool _isEditMode;
    private readonly bool _isReadOnly;
    private readonly ToolbarButtonEditWindowView _view;

    protected override int MIN_WIDTH => 500;
    protected override int MAX_WIDTH => 500;
    protected override Thickness ContentPadding => new(0);

    public ToolbarItemModel? ResultModel { get; private set; }

    public ToolbarButtonEditWindow(ToolbarItemModel? model, ISet<string> takenIds, bool isBuiltIn)
    {
        _isEditMode = model is not null;
        _isReadOnly = isBuiltIn;

        IsButton2Visible = true;
        IsButton3Visible = false;

        IsButton1Visible = !_isReadOnly;
        DefaultButton = _isReadOnly ? DialogButton.Button2 : DialogButton.Button1;
        DefaultFocus = _isReadOnly ? DialogFocus.Button2 : DialogFocus.Default;

        _view = new ToolbarButtonEditWindowView();
        _view.LoadData(model, _isReadOnly, takenIds);
        DialogContent = _view;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[_isEditMode
            ? LangId.Settings_Toolbar_EditButton
            : LangId.Settings_Toolbar_AddNewButton];
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

        if (!_view.Validate())
        {
            e.CanProceed = false;
            return;
        }

        ResultModel = _view.BuildModel();
        base.OnDialogSubmitted(e);
    }

}
