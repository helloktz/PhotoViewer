using Avalonia.Layout;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System.Threading.Tasks;

namespace PhotoViewer.Windows;

public partial class QuickSetupWindow : DialogWindow
{
    protected override int MIN_WIDTH => 500;
    protected override int MAX_WIDTH => 500;

    private readonly QuickSetupView _view;
    private PhButton _btnSkip = null!;

    public static bool ShouldShowAtStartup => Core.Config.QuickSetupVersion < Const.QUICK_SETUP_VERSION;

    public bool IsRestarting { get; private set; }

    public QuickSetupWindow()
    {
        ShowInTaskbar = true;
        WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen;

        IsButton1Visible = false;
        IsButton2Visible = true;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button2;
        DefaultFocus = DialogFocus.Button2;

        _view = new QuickSetupView();
        _view.PreviewLanguageChanged += (_, _) => RefreshFooter();
        DialogContent = _view;
        DialogFooterLeftContent = _btnSkip = BuildSkipButton();

        RefreshFooter();
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();
        RefreshFooter();
    }

    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        if (_view.CurrentStep <= 1) return;

        _view.ShowStep(_view.CurrentStep - 1);
        RefreshFooter();
    }

    protected override void OnDialogCancelled(DialogEventArgs e)
    {
        if (_view.CurrentStep < _view.StepCount)
        {
            _view.ShowStep(_view.CurrentStep + 1);
            RefreshFooter();
            return;
        }

        _ = SaveAndCloseAsync();
    }

    private void RefreshFooter()
    {
        var lang = _view.PreviewLang;
        var isLastStep = _view.CurrentStep >= _view.StepCount;

        IsButton1Visible = _view.CurrentStep > 1;

        Title = lang[LangId.QuickSetup_Title];
        Button1Text = lang[LangId._Back];
        Button2Text = lang[isLastStep ? LangId._Save : LangId._Next];
        if (_btnSkip is not null) _btnSkip.Text = lang[LangId.QuickSetup_SkipAndLaunch];
    }

    private PhButton BuildSkipButton()
    {
        var btn = new PhButton
        {
            Variant = PhButtonVariant.Link,
            VerticalAlignment = VerticalAlignment.Center,
        };
        btn.Click += (_, _) => SkipSetup();

        return btn;
    }

    private void SkipSetup()
    {
        DialogResult = DialogExitCode.Cancel;
        Close(DialogResult);
    }

    private async Task SaveAndCloseAsync()
    {
        if (BHelper.HasOtherInstances())
        {
            var modal = await ModalWindow.ShowWarningAsync(this, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.QuickSetup_Title],
                Heading = Core.Lang[LangId.QuickSetup_ConfirmCloseProcess],
                Description = Core.Lang[LangId.QuickSetup_ConfirmCloseProcess_Description],
            }, ModalWindowButton.Yes_No);

            if (modal.ExitCode != DialogExitCode.OK)
            {
                DialogResult = DialogExitCode.Cancel;
                Close(DialogResult);
                return;
            }

            BHelper.CloseOtherInstances();
        }

        Core.Config.ResetToDefault();
        ApplySettings();

        if (!await Core.Config.SaveAsync())
        {
            await ModalWindow.ShowSettingsSaveErrorAsync(this, Core.Lang[LangId.QuickSetup_Title]);
            return;
        }

        IsRestarting = true;
        BHelper.RestartApp(suppressQuickSetup: true);
    }

    private void ApplySettings()
    {
        Core.Config.QuickSetupVersion = Const.QUICK_SETUP_VERSION;

        Core.Config.Language = _view.SelectedLanguageValue;

        Core.Config.ColorProfile = _view.IsProfessional
            ? nameof(ColorProfileOption.CurrentMonitorProfile)
            : nameof(ColorProfileOption.None);
        Core.Config.EnableHdrToneMapping = _view.IsProfessional;
        Core.Config.EnableExplorerSortOrder = _view.IsProfessional;
        Core.Config.EnableOnlyLoadRawPreview = !_view.IsProfessional;
    }

}
