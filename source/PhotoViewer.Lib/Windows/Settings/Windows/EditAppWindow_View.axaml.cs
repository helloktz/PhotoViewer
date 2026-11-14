using Avalonia.Controls;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using System;
using System.Linq;

namespace PhotoViewer.Common.Windows;

public partial class EditAppWindowView : PhControl
{
    public EditAppWindowView()
    {
        InitializeComponent();

        PART_Extension.AcceptValue = TextBoxAcceptValue.FileExtensionsValueOnly;
    }

    public string ResultExtKey => PART_Extension.Text?.Trim() ?? string.Empty;

    public void LoadData(string? extKey, EditingApp? app)
    {
        PART_Extension.Text = extKey ?? string.Empty;
        PART_AppName.Text = app?.AppName ?? string.Empty;
        PART_Action.Executable = app?.Executable ?? string.Empty;
        PART_Action.Argument = app?.Argument ?? Const.FILE_MACRO;

        DataValidationErrors.ClearErrors(PART_Extension);
        DataValidationErrors.ClearErrors(PART_AppName);
        PART_Action.ClearValidationErrors();
    }

    public bool Validate()
    {
        var extOk = PART_Extension.ValidateAndShowError();
        var nameOk = PART_AppName.ValidateAndShowError();
        var exeOk = PART_Action.ValidateExecutable();

        if (extOk) PART_Extension.Text = NormalizeExtensions(PART_Extension.Text);

        return extOk & nameOk & exeOk;
    }

    private static string NormalizeExtensions(string? raw)
    {
        var segments = (raw ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.ToLowerInvariant())
            .Distinct();

        return string.Join(';', segments);
    }

    public EditingApp BuildApp() => new(
        PART_AppName.Text?.Trim() ?? string.Empty,
        PART_Action.Executable?.Trim() ?? string.Empty,
        PART_Action.Argument?.Trim() ?? string.Empty);

}
