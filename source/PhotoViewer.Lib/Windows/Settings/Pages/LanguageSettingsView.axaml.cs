using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhotoViewer.Common.Localization;
using PhotoViewer.UI.Windowing;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class LanguageSettingsView : SettingsPageView
{

    private Lang[] _langs = [];
    private bool _isPopulating;

    private static FilePickerFileType LangPackFileType => new(Core.Lang[LangId._LanguagePackFileType])
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
                VM.SetValue(ConfigId.Language, lang.Metadata.Code);
            }
        };
        RegisterSearchKey(PART_LanguageList, LangId.Settings_DisplayLanguage, ConfigId.Language, null);

        SetLocalizedText(PART_RefreshLanguages, LangId.Settings_Refresh);
        PART_RefreshLanguages.Click += async (_, _) =>
        {
            await ReloadLanguagesAsync();
            await Core.Config.LoadCurrentLanguageAsync();
        };
        RegisterSearchKey(PART_RefreshLanguages, LangId.Settings_Refresh, null, null);

        SetLocalizedText(PART_ExportLanguage, LangId.Settings_ExportLanguagePack);
        PART_ExportLanguage.Click += async (_, _) => await ExportLanguageAsync();
        RegisterSearchKey(PART_ExportLanguage, LangId.Settings_ExportLanguagePack, null, null);

        _ = ReloadLanguagesAsync();
    }

    private async Task ReloadLanguagesAsync()
    {
        _langs = await Lang.LoadAllLanguagePacksAsync();
        PopulateCombo();
    }

    private void PopulateCombo()
    {
        _isPopulating = true;

        PART_LanguageList.ItemsSource = _langs;

        var current = Lang.NormalizeLanguageCode(VM.GetValue(ConfigId.Language, Lang.DefaultLanguageCode));
        PART_LanguageList.SelectedItem = _langs
            .FirstOrDefault(lang => lang.Metadata.Code.Equals(current, StringComparison.OrdinalIgnoreCase));

        _isPopulating = false;
    }

    private async Task ExportLanguageAsync()
    {
        if (PART_LanguageList.SelectedItem is not Lang selected) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = selected.FileName,
            FileTypeChoices = [LangPackFileType],
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        await selected.SaveAsFileAsync(path);
    }

}
