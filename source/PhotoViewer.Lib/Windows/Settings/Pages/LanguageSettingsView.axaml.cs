using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhotoViewer.Common.Localization;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class LanguageSettingsView : SettingsPageView
{

    private List<Lang> _langs = [];
    private bool _isPopulating;

    private static FilePickerFileType LangPackFileType => new("Photo Viewer language pack")
    {
        Patterns = ["*.iglang.json"],
    };

    public LanguageSettingsView()
    {
        InitializeComponent();
    }

    public LanguageSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        PART_LanguageList.SelectionChanged += (_, _) =>
        {
            if (_isPopulating) return;
            if (PART_LanguageList.SelectedItem is Lang lang)
            {
                VM.SetValue(ConfigId.Language, ToConfigValue(lang));
            }
        };
        RegisterSearchKey(PART_LanguageList, LangId.Settings_DisplayLanguage, ConfigId.Language, null);

        SetLocalizedText(PART_RefreshLanguages, LangId.Settings_Refresh);
        PART_RefreshLanguages.Click += async (_, _) => await ReloadLanguagesAsync();
        RegisterSearchKey(PART_RefreshLanguages, LangId.Settings_Refresh, null, null);

        SetLocalizedText(PART_ExportLanguage, LangId.Settings_ExportLanguagePack);
        PART_ExportLanguage.Click += async (_, _) => await ExportLanguageAsync();
        RegisterSearchKey(PART_ExportLanguage, LangId.Settings_ExportLanguagePack, null, null);

        _ = ReloadLanguagesAsync();
    }

    private async Task ReloadLanguagesAsync()
    {
        var packs = await Lang.LoadAllLanguagePacksAsync();

        _langs = [new Lang(string.Empty), .. packs];
        PopulateCombo();
    }

    private void PopulateCombo()
    {
        _isPopulating = true;

        PART_LanguageList.ItemsSource = _langs;

        var current = VM.GetValue(ConfigId.Language, "English");
        PART_LanguageList.SelectedItem = _langs
            .FirstOrDefault(l => ToConfigValue(l).Equals(current, StringComparison.OrdinalIgnoreCase))
            ?? _langs[0];

        _isPopulating = false;
    }

    private async Task ExportLanguageAsync()
    {
        if (PART_LanguageList.SelectedItem is not Lang selected) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = selected.IsBuiltIn ? "English.iglang.json" : selected.FileName,
            FileTypeChoices = [LangPackFileType],
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var toExport = selected.IsBuiltIn ? new Lang(string.Empty) { Items = Lang.DefaultLangMap } : selected;
        await toExport.SaveAsFileAsync(path);
    }

    private static string ToConfigValue(Lang lang) => lang.IsBuiltIn ? "English" : lang.FileName;

}
