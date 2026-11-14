using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using System.Collections.Generic;

namespace PhotoViewer.Common.Windows;

public partial class KeyboardSettingsView : SettingsPageView
{
    public KeyboardSettingsView()
    {
        InitializeComponent();
    }

    public KeyboardSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        var current = VM.GetValue(ConfigId.MenuHotkeys, new Dictionary<LangId, Hotkey[]>());

        PART_Editor.LoadHotkeys(current);
        PART_Editor.HotkeysChanged += (_, _) =>
            VM.SetValue(ConfigId.MenuHotkeys, PART_Editor.CurrentHotkeys);

        RegisterSearchKey(PART_Editor, LangId.Settings_Keyboard_MenuHotkeys,
            ConfigId.MenuHotkeys, LangId.Settings_Keyboard_MenuHotkeys);

        DisableIfLocked(ConfigId.MenuHotkeys, PART_Editor);
    }

}
