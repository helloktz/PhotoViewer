using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class ImageSettingsView : SettingsPageView
{
    private string _customProfilePath = string.Empty;

    public ImageSettingsView()
    {
        InitializeComponent();
    }

    public ImageSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        BindEnumDropdown(PART_OrderBy, ConfigId.ImageLoadingOrder, ImageOrderBy.Name,
            LangId.Settings_ImageLoadingOrder, LangId.Settings_Browsing);
        BindEnumDropdown(PART_OrderType, ConfigId.ImageLoadingOrderType, ImageOrderType.Asc,
            LangId.Settings_ImageLoadingOrder, LangId.Settings_Browsing);

        BindToggle(PART_ExplorerSortOrder, ConfigId.EnableExplorerSortOrder,
            LangId.Settings_EnableExplorerSortOrder, LangId.Settings_Browsing, true);
        BindToggle(PART_SubfoldersLoading, ConfigId.EnableSubfoldersLoading,
            LangId.Settings_EnableSubfoldersLoading, LangId.Settings_Browsing);
        BindToggle(PART_FolderGrouping, ConfigId.EnableImageFolderGrouping,
            LangId.Settings_EnableImageFolderGrouping, LangId.Settings_Browsing);
        BindToggle(PART_HiddenImages, ConfigId.EnableHiddenImagesLoading,
            LangId.Settings_EnableHiddenImagesLoading, LangId.Settings_Browsing);
        BindToggle(PART_LoopBack, ConfigId.EnableLoopBackNavigation,
            LangId.Settings_EnableLoopBackNavigation, LangId.Settings_Browsing, true);
        BindToggle(PART_AutoSwitchSiblingDir, ConfigId.EnableAutoSwitchSiblingDir,
            LangId.Settings_EnableAutoSwitchSiblingDir, LangId.Settings_Browsing);
        BuildBrowsingMode();
        BindTransitionEditor(PART_Transition, PART_TransitionDuration, PART_TransitionError,
            PART_OpenTransitionsFolder,
            ConfigId.NavigationTransition, ConfigId.NavigationTransitionDuration, 300u, LangId.Settings_Browsing);

        BindToggle(PART_ImagePreview, ConfigId.EnableImagePreview,
            LangId.Settings_EnableImagePreview, LangId.Settings_ImagePreview, true);
        BindToggle(PART_OnlyRawPreview, ConfigId.EnableOnlyLoadRawPreview,
            LangId.Settings_EnableOnlyLoadRawPreview, LangId.Settings_ImagePreview);
        BindToggle(PART_OnlyNonRawPreview, ConfigId.EnableOnlyLoadNonRawPreview,
            LangId.Settings_EnableOnlyLoadNonRawPreview, LangId.Settings_ImagePreview);

        PART_OnlyRawPreview.IsCheckedChanged += (_, _) => UpdatePreviewSizeVisibility();
        PART_OnlyNonRawPreview.IsCheckedChanged += (_, _) => UpdatePreviewSizeVisibility();
        UpdatePreviewSizeVisibility();

        BindIntInput(PART_PreviewMinWidth, ConfigId.PreviewMinWidth,
            LangId.Settings_MinEmbeddedThumbnailSize, LangId.Settings_ImagePreview);
        BindIntInput(PART_PreviewMinHeight, ConfigId.PreviewMinHeight,
            LangId.Settings_MinEmbeddedThumbnailSize, LangId.Settings_ImagePreview);

        BindToggle(PART_FileWatcher, ConfigId.EnableFileWatcher,
            LangId.Settings_EnableFileWatcher, LangId.Settings_FileWatcher, true);
        BindToggle(PART_AutoOpenNewAddedImage, ConfigId.EnableAutoOpenNewAddedImage,
            LangId.Settings_EnableAutoOpenNewAddedImage, LangId.Settings_FileWatcher);

        BindToggle(PART_HdrToneMapping, ConfigId.EnableHdrToneMapping,
            LangId.Settings_EnableHdrToneMapping, LangId.Settings_ColorManagement, true);
        BindToggle(PART_AlwaysApplyColorProfile, ConfigId.EnableAlwaysApplyColorProfile,
            LangId.Settings_EnableAlwaysApplyColorProfile, LangId.Settings_ColorManagement);
        BuildColorProfile();

        BindUIntSlider(PART_CacheMaxFiles, ConfigId.CacheMaxFiles,
            LangId.Settings_ImageBoosterCacheMaxFiles, LangId.Settings_Caching, 10u, PART_CacheMaxFilesLabel);
        BindUIntInput(PART_CacheMaxMemory, ConfigId.CacheMaxMemoryInMb,
            LangId.Settings_ImageBoosterCacheMaxMemoryInMb, LangId.Settings_Caching);
        BindUIntInput(PART_CacheMaxDimension, ConfigId.CacheMaxDimension,
            LangId.Settings_ImageBoosterCacheMaxDimension, LangId.Settings_Caching, 8_000u);
        BindDoubleInput(PART_CacheMaxFileSize, ConfigId.CacheMaxFileSizeInMb,
            LangId.Settings_ImageBoosterCacheMaxFileSizeInMb, LangId.Settings_Caching, 100d);
    }

    private void BuildBrowsingMode()
    {
        var options = new List<BrowsingModeOption>
        {
            new(BrowsingMode.Turbo, LangId.BrowsingMode_Turbo, LangId.BrowsingMode_Turbo_Description),
            new(BrowsingMode.Sequential, LangId.BrowsingMode_Sequential, LangId.BrowsingMode_Sequential_Description),
        };

        var current = VM.GetValue(ConfigId.BrowsingMode, BrowsingMode.Turbo);
        PART_BrowsingMode.ItemsSource = options;
        PART_BrowsingMode.SelectedItem = options.Find(o => o.Value == current) ?? options[0];

        PART_BrowsingMode.SelectionChanged += (_, _) =>
        {
            if (PART_BrowsingMode.SelectedItem is BrowsingModeOption opt)
            {
                VM.SetValue(ConfigId.BrowsingMode, opt.Value);
            }
        };

        AddLangRefresher(() => options.ForEach(o => o.RefreshLang()));

        RegisterSearchKey(PART_BrowsingMode, LangId.Settings_BrowsingMode,
            ConfigId.BrowsingMode, LangId.Settings_Browsing);
    }

    private void UpdatePreviewSizeVisibility()
    {
        PART_PreviewSizeSection.IsVisible =
            (PART_OnlyRawPreview.IsChecked ?? false) || (PART_OnlyNonRawPreview.IsChecked ?? false);
    }

    private void BuildColorProfile()
    {
        var current = VM.GetValue(ConfigId.ColorProfile, nameof(ColorProfileOption.CurrentMonitorProfile));
        var isCustomPath = current.Contains('.', StringComparison.Ordinal);
        if (isCustomPath)
        {
            _customProfilePath = current;
            PART_CustomColorProfile.Text = current;
            ToolTip.SetTip(PART_CustomColorProfile, current);
        }

        var names = Enum.GetNames<ColorProfileOption>();
        var selectedIndex = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var item = new ComboBoxItem { Tag = name };

            BindComboItemText(item, Lang.GetKey($"{nameof(ColorProfileOption)}_{name}"), name);
            PART_ColorProfile.Items.Add(item);

            var match = isCustomPath
                ? name == nameof(ColorProfileOption.Custom)
                : name == current;
            if (match) selectedIndex = i;
        }
        PART_ColorProfile.SelectedIndex = selectedIndex;

        PART_ColorProfile.SelectionChanged += (_, _) =>
        {
            StageColorProfile();
            UpdateColorProfileVisibility();
        };

        SetLocalizedText(PART_BrowseColorProfile, LangId._Browse);
        PART_BrowseColorProfile.Click += async (_, _) => await BrowseColorProfileAsync();
        PART_CustomColorProfile.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_customProfilePath)) BHelper.OpenFilePath(_customProfilePath);
        };

        UpdateColorProfileVisibility();

        RegisterSearchKey(PART_ColorProfile, LangId.Settings_ColorProfile,
            ConfigId.ColorProfile, LangId.Settings_ColorManagement);

        DisableIfLocked(ConfigId.ColorProfile, PART_ColorProfile, PART_BrowseColorProfile);
    }

    private void StageColorProfile()
    {
        var selected = SelectedColorProfileName();
        if (selected == nameof(ColorProfileOption.Custom))
        {
            VM.SetValue(ConfigId.ColorProfile,
                string.IsNullOrEmpty(_customProfilePath) ? selected : _customProfilePath);
        }
        else
        {
            VM.SetValue(ConfigId.ColorProfile, selected);
        }
    }

    private void UpdateColorProfileVisibility()
    {
        var selected = SelectedColorProfileName();
        var isCustom = selected == nameof(ColorProfileOption.Custom);

        PART_BrowseColorProfile.IsVisible = isCustom;
        PART_CustomColorProfile.IsVisible = isCustom && !string.IsNullOrEmpty(_customProfilePath);
        PART_MonitorProfileNote.IsVisible = selected == nameof(ColorProfileOption.CurrentMonitorProfile);
    }

    private string SelectedColorProfileName()
    {
        return PART_ColorProfile.SelectedItem is ComboBoxItem { Tag: string name }
            ? name
            : nameof(ColorProfileOption.CurrentMonitorProfile);
    }

    private async Task BrowseColorProfileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ICC/ICM") { Patterns = ["*.icc", "*.icm"] },
                FilePickerFileTypes.All,
            ],
        });

        var path = (files.Count > 0 ? files[0] : null)?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        _customProfilePath = path;
        PART_CustomColorProfile.Text = path;
        ToolTip.SetTip(PART_CustomColorProfile, path);

        StageColorProfile();
        UpdateColorProfileVisibility();
    }

}
