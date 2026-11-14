using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoViewer.Common.Localization;
using PhotoViewer.UI.Windowing;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class SettingsWindow : DialogWindow
{
    private readonly SettingsViewModel _vm = new();
    private readonly SettingsWindowView _viewEl;
    private readonly string? _targetConfigId;
    private readonly string? _editToolId;
    private bool _editToolShown;

    protected override int MIN_WIDTH => 0;
    protected override int MAX_WIDTH => int.MaxValue;
    protected override Thickness ContentPadding => new(0);

    private const double DEFAULT_WIDTH = 900;
    private const double DEFAULT_HEIGHT = 580;
    private const double MIN_RESTORE_WIDTH = 200;
    private const double MIN_RESTORE_HEIGHT = 100;

    public SettingsWindow(string? targetConfigId = null, string? editToolId = null)
    {
        _targetConfigId = targetConfigId;
        _editToolId = editToolId;

        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = true;

        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Default;
        ShowInTaskbar = true;
        PressEnterToSubmit = false;

        CanResize = true;
        CanMinimize = true;
        CanMaximize = true;
        SizeToContent = SizeToContent.Manual;

        RestoreWindowBounds(Core.Config.SettingsWindowBounds,
            new(DEFAULT_WIDTH, DEFAULT_HEIGHT),
            new(MIN_RESTORE_WIDTH, MIN_RESTORE_HEIGHT));

        if (Core.Config.EnableSettingsWindowMaximized) WindowState = WindowState.Maximized;

        _viewEl = new SettingsWindowView(_vm);
        DialogContent = _viewEl;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (!string.IsNullOrWhiteSpace(_targetConfigId))
        {
            _viewEl.NavigateToConfig(_targetConfigId);
        }

        if (!_editToolShown && !string.IsNullOrWhiteSpace(_editToolId))
        {
            _editToolShown = true;
            _viewEl.NavigateToTool(_editToolId);
        }

        _viewEl.FocusSearch();
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Menu_MnuSettings];
        Button1Text = Core.Lang[LangId._OK];
        Button2Text = Core.Lang[LangId._Cancel];
        Button3Text = Core.Lang[LangId._Apply];
    }

    protected override async void OnDialogSubmitted(DialogEventArgs e)
    {
        if (!e.CanProceed) return;

        await CommitAndReportAsync();
        base.OnDialogSubmitted(e);
    }

    protected override async void OnDialogApplied(DialogEventArgs e)
    {
        if (!e.CanProceed) return;

        await CommitAndReportAsync();
        base.OnDialogApplied(e);
    }

    private async Task CommitAndReportAsync()
    {
        if (await _vm.CommitAsync()) return;

        await ModalWindow.ShowSettingsSaveErrorAsync(this, Core.Lang[LangId.Menu_MnuSettings]);
    }

    protected override void OnDialogCancelled(DialogEventArgs e)
    {
        _vm.Discard();
        base.OnDialogCancelled(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {

        Core.Config.EnableSettingsWindowMaximized = RestorableWindowState == WindowState.Maximized;

        if (WindowedBounds is { } bounds
            && bounds.Width >= MIN_RESTORE_WIDTH
            && bounds.Height >= MIN_RESTORE_HEIGHT)
        {
            Core.Config.SettingsWindowBounds = bounds;
        }

        _ = Core.Config.SaveAsync();

        base.OnClosing(e);
    }

    protected override void OnDialogAborted()
    {
        _vm.Discard();
        base.OnDialogAborted();
    }

    public void NavigateToConfig(string? configId) => _viewEl.NavigateToConfig(configId);

    public void NavigateToTool(string? toolId) => _viewEl.NavigateToTool(toolId);

}
