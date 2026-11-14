using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using System.Collections.ObjectModel;

namespace PhotoViewer.Common.Windows;

public partial class ToolbarSettingsView : SettingsPageView
{
    public ToolbarSettingsView()
    {
        InitializeComponent();
    }

    public ToolbarSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        BindToggle(PART_ShowToolbarInFullscreen, ConfigId.ShowToolbarInFullscreen,
            LangId.Settings_Toolbar_ShowToolbarInFullscreen, LangId.Settings_Appearance);
        BindUIntSlider(PART_ToolbarIconHeight, ConfigId.ToolbarIconHeight,
            LangId.Settings_Toolbar_ToolbarIconHeight, LangId.Settings_Appearance,
            (uint)Const.TOOLBAR_ICON_HEIGHT, PART_IconHeightLabel);

        BuildToolbarButtons();
    }

    private void BuildToolbarButtons()
    {
        var current = VM.GetValue(ConfigId.ToolbarButtons,
            new ObservableCollection<ToolbarItemModel>(Config.DefaultToolbarItems));

        PART_Editor.LoadButtons(current);
        PART_Editor.ButtonsChanged += (_, _) =>
            VM.SetValue(ConfigId.ToolbarButtons, PART_Editor.CurrentButtons);

        RegisterSearchKey(PART_Editor, LangId.Settings_Toolbar_ToolbarButtons,
            ConfigId.ToolbarButtons, LangId.Settings_Toolbar_ToolbarButtons);

        DisableIfLocked(ConfigId.ToolbarButtons, PART_Editor);
    }

}
