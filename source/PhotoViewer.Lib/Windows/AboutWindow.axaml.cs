using PhotoViewer.Common.Localization;
using PhotoViewer.UI.Windowing;
using ImageMagick;
using System.Runtime.InteropServices;

namespace PhotoViewer.Common.Windows;

public partial class AboutWindow : DialogWindow
{

    public AboutWindow()
    {
        InitializeComponent();

        IsButton1Visible = true;
        IsButton2Visible = false;
        IsButton3Visible = false;

        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Button1;
        ShowInTaskbar = true;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Menu_MnuAbout];
        Button1Text = Core.Lang[LangId._Close];

        PART_LblCopyright.Text = Core.Lang[LangId._AboutCopyright];
        PART_LblCreditContent.Text = Core.Lang[LangId._AboutThirdPartyNotices];
        PART_LblSlogan.Text = Core.Lang[LangId._Slogan];
        PART_LblCredits.Text = Core.Lang[LangId._Credits];
        UpdateVersionText();
    }

    private void UpdateVersionText()
    {
        var magickVersion = MagickNET.Version;
        var dotnetVersion = RuntimeInformation.FrameworkDescription;

        PART_LblVersion.Text = $"""
            {Core.Lang[LangId._AboutVersion]} {Core.BuildInfo.Version}
            {magickVersion}
            {dotnetVersion}
            """;
    }

}
