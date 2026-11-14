using Avalonia.Controls;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.UI.Windowing;
using PhotoViewer.Windows;
using System;
using System.IO;

namespace PhotoViewer.Common.Windows;

public partial class GeneralSettingsView : SettingsPageView
{
    public GeneralSettingsView()
    {
        InitializeComponent();
    }

    public GeneralSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        var startupDir = BHelper.BasePath;

        var configDir = BHelper.GetRealPlatformConfigDir();
        var userConfig = BHelper.GetRealPlatformConfigDir(Config.CONFIG_USER);

        BindLink(PART_StartupDir, LangId.Settings_StartupDir, startupDir,
            () => BHelper.OpenFolderPath(startupDir));
        BindLink(PART_ConfigDir, LangId.Settings_ConfigDir, configDir,
            () => BHelper.OpenFolderPath(configDir));
        BindLink(PART_UserConfig, LangId.Settings_UserConfigFile, userConfig,
            () => OpenUserConfigFile(userConfig));

        BindLink(PART_ResetSettings, LangId.Settings_ResetSettings, ShowQuickSetup);

        BindToggle(PART_WelcomeImage, ConfigId.EnableWelcomeImage,
            LangId.Settings_EnableWelcomeImage, LangId.Settings_Startup);
        BindToggle(PART_LastSeenImage, ConfigId.EnableLastSeenImage,
            LangId.Settings_EnableLastSeenImage, LangId.Settings_Startup);
        BindToggle(PART_MultiInstances, ConfigId.EnableMultiInstances,
            LangId.Settings_EnableMultiInstances, LangId.Settings_Startup);

        BindIntInput(PART_MsgDuration, ConfigId.InAppMessageDuration,
            LangId.Settings_InAppMessageDuration, LangId.Settings_Others);
    }

    private async void ShowQuickSetup()
    {
        try
        {
            var quickSetup = new QuickSetupWindow();
            _ = await quickSetup.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow);
        }
        catch
        {
        }
    }

    private static async void OpenUserConfigFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            BHelper.OpenFolderPath(Path.GetDirectoryName(filePath));
            return;
        }

        try
        {
            var exitCode = await BHelper.RunExeAsync(filePath);
            if (exitCode == 0) return;
        }
        catch { }

        BHelper.OpenFilePath(filePath);
    }

}
