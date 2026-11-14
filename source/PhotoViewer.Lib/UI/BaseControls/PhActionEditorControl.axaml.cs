using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhotoViewer.Common;
using PhotoViewer.Common.Actions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoViewer.UI;

public partial class PhActionEditorControl : PhControl
{

    public string? Executable
    {
        get => PART_Executable.Text;
        set => PART_Executable.Text = value;
    }

    public string? Argument
    {
        get => PART_Argument.Text;
        set => PART_Argument.Text = value;
    }

    public IReadOnlyList<Hotkey> Hotkeys
    {
        get => PART_Hotkeys.Hotkeys;
        set => PART_Hotkeys.Hotkeys = value;
    }

    public bool ShowHotkeys
    {
        get => PART_HotkeysSection.IsVisible;
        set => PART_HotkeysSection.IsVisible = value;
    }

    public string? ExecutablePlaceholder
    {
        get => PART_Executable.PlaceholderText;
        set => PART_Executable.PlaceholderText = value;
    }

    public string? ArgumentPlaceholder
    {
        get => PART_Argument.PlaceholderText;
        set => PART_Argument.PlaceholderText = value;
    }

    public bool IsExecutableRequired
    {
        get => PART_Executable.IsRequired;
        set => PART_Executable.IsRequired = value;
    }

    public PhActionEditorControl()
    {
        InitializeComponent();

        PART_Executable.TextChanged += (_, _) => UpdatePreview();
        PART_Argument.TextChanged += (_, _) => UpdatePreview();
        PART_Browse.Click += async (_, _) => await BrowseExecutableAsync();

        UpdatePreview();
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        ToolTip.SetTip(PART_Browse, Core.Lang[LangId._Browse]);
        PART_Hotkeys.PlaceholderText = Core.Lang[LangId.Settings_Toolbar_RecordHotkeyHint];
    }

    public void LoadAction(SingleAction? action)
    {
        Executable = action?.Executable ?? string.Empty;
        Argument = action?.Argument ?? string.Empty;
        Hotkeys = (action as HotkeySingleAction)?.Hotkeys ?? [];

        UpdatePreview();
        ClearValidationErrors();
    }

    public bool ValidateExecutable() => PART_Executable.ValidateAndShowError();

    public void ClearValidationErrors() => DataValidationErrors.ClearErrors(PART_Executable);

    private void UpdatePreview()
    {
        PART_CommandPreview.Executable = PART_Executable.Text;
        PART_CommandPreview.Argument = PART_Argument.Text;
    }

    private async Task BrowseExecutableAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
        });

        var path = (files.Count > 0 ? files[0] : null)?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        Executable = path;
    }

}
