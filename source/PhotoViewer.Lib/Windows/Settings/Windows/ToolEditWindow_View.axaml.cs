using Avalonia.Controls;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.Tools;
using PhotoViewer.UI;
using System.Collections.Generic;

namespace PhotoViewer.Common.Windows;

public partial class ToolEditWindowView : PhControl
{
    private ISet<string> _takenIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    public ToolEditWindowView()
    {
        InitializeComponent();

    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        var template = Core.Lang[LangId.Settings_Tools_IntegratedWith];
        var idx = template.IndexOf("{0}", System.StringComparison.Ordinal);

        PART_IntegratedBefore.Text = idx < 0 ? template : template[..idx];
        PART_IntegratedAfter.Text = idx < 0 ? string.Empty : template[(idx + 3)..];
        PART_IntegratedAfter.IsVisible = !string.IsNullOrEmpty(PART_IntegratedAfter.Text);
    }

    public void LoadData(ExternalTool? tool, ISet<string> takenIds, bool isIdLocked = false)
    {
        _takenIds = takenIds;

        PART_Id.Text = tool?.ToolId ?? string.Empty;
        PART_Id.IsEnabled = !isIdLocked;
        PART_Name.Text = tool?.ToolName ?? string.Empty;
        PART_Integrated.IsChecked = tool?.IsIntegrated ?? false;

        PART_Action.Executable = tool?.Executable ?? string.Empty;
        PART_Action.Argument = tool?.Arguments ?? Const.FILE_MACRO;
        PART_Action.Hotkeys = tool?.Hotkeys ?? [];

        DataValidationErrors.ClearErrors(PART_Id);
        PART_Action.ClearValidationErrors();
    }

    public bool Validate()
    {
        var idOk = PART_Id.ValidateAndShowError();
        if (idOk)
        {
            var id = PART_Id.Text?.Trim() ?? string.Empty;
            if (_takenIds.Contains(id))
            {
                DataValidationErrors.SetError(PART_Id,
                    new ValidationException(Core.Lang[LangId.Settings_Tools_Errors_ToolIdDuplicated, id]));
                idOk = false;
            }
        }

        var exeOk = PART_Action.ValidateExecutable();

        return idOk & exeOk;
    }

    public ExternalTool BuildTool() => new()
    {
        ToolId = PART_Id.Text?.Trim() ?? string.Empty,
        ToolName = PART_Name.Text?.Trim() ?? string.Empty,
        Executable = PART_Action.Executable?.Trim() ?? string.Empty,
        Arguments = PART_Action.Argument?.Trim() ?? string.Empty,
        IsIntegrated = PART_Integrated.IsChecked == true,
        Hotkeys = [.. PART_Action.Hotkeys],
    };

}
