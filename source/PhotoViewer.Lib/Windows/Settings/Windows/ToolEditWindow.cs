using PhotoViewer.Common.Localization;
using PhotoViewer.Tools;
using PhotoViewer.UI.Windowing;
using System.Collections.Generic;

namespace PhotoViewer.Common.Windows;

internal sealed class ToolEditWindow : DialogWindow
{
    private readonly bool _isEditMode;
    private readonly ToolEditWindowView _view;

    protected override int MIN_WIDTH => 500;
    protected override int MAX_WIDTH => 500;

    public ExternalTool? ResultTool { get; private set; }

    public ToolEditWindow(ExternalTool? tool, ISet<string> takenIds, bool isIdLocked = false)
    {
        _isEditMode = tool is not null;

        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Default;

        _view = new ToolEditWindowView();
        _view.LoadData(tool, takenIds, isIdLocked);
        DialogContent = _view;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[_isEditMode
            ? LangId.Settings_Tools_EditTool
            : LangId.Settings_Tools_AddNewTool];
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

        ResultTool = _view.BuildTool();
        base.OnDialogSubmitted(e);
    }

}
