using Avalonia;
using PhotoViewer.Common.Actions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using PhotoViewer.Common.Types.JsonTypeConverters;
using PhotoViewer.Tools;
using PhotoViewer.UI;
using PhotoViewer.UI.Viewer;
using PhotoViewer.UI.Viewer.Transitions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common;

[JsonSerializable(typeof(Config))]
public partial class ConfigJsonContext : JsonSerializerContext { }

public partial class Config : PhReactive
{
    [JsonIgnore]
    private readonly Dictionary<ConfigId, object> _values = [];

    public ConfigMetadata _Metadata { get; set; } = new();

    public bool ShowToolbar
    {
        get => Get(ConfigId.ShowToolbar, true);
        set => Set(ConfigId.ShowToolbar, value);
    }

    public bool ShowToolbarInFullscreen
    {
        get => Get(ConfigId.ShowToolbarInFullscreen, false);
        set => Set(ConfigId.ShowToolbarInFullscreen, value);
    }

    public bool ShowGallery
    {
        get => Get(ConfigId.ShowGallery, true);
        set => Set(ConfigId.ShowGallery, value);
    }

    public bool ShowGalleryInFullscreen
    {
        get => Get(ConfigId.ShowGalleryInFullscreen, false);
        set => Set(ConfigId.ShowGalleryInFullscreen, value);
    }

    public bool ShowGalleryFileName
    {
        get => Get(ConfigId.ShowGalleryFileName, true);
        set => Set(ConfigId.ShowGalleryFileName, value);
    }

    public bool ShowAppIcon
    {
        get => Get(ConfigId.ShowAppIcon, true);
        set => Set(ConfigId.ShowAppIcon, value);
    }

    public bool EnableMainWindowMaximized
    {
        get => Get(ConfigId.EnableMainWindowMaximized, false);
        set => Set(ConfigId.EnableMainWindowMaximized, value);
    }

    public bool EnableSettingsWindowMaximized
    {
        get => Get(ConfigId.EnableSettingsWindowMaximized, false);
        set => Set(ConfigId.EnableSettingsWindowMaximized, value);
    }

    public bool EnablePrintWindowMaximized
    {
        get => Get(ConfigId.EnablePrintWindowMaximized, false);
        set => Set(ConfigId.EnablePrintWindowMaximized, value);
    }

    [JsonIgnore]
    public bool EnableSlideshow
    {
        get => Get(ConfigId.EnableSlideshow, false);
        set => Set(ConfigId.EnableSlideshow, value);
    }

    public bool EnableSlideshowCountdown
    {
        get => Get(ConfigId.EnableSlideshowCountdown, true);
        set => Set(ConfigId.EnableSlideshowCountdown, value);
    }

    public bool EnableSlideshowRandomInterval
    {
        get => Get(ConfigId.EnableSlideshowRandomInterval, false);
        set => Set(ConfigId.EnableSlideshowRandomInterval, value);
    }

    public bool EnableLoopSlideshow
    {
        get => Get(ConfigId.EnableLoopSlideshow, true);
        set => Set(ConfigId.EnableLoopSlideshow, value);
    }

    public bool EnableFullscreenSlideshow
    {
        get => Get(ConfigId.EnableFullscreenSlideshow, true);
        set => Set(ConfigId.EnableFullscreenSlideshow, value);
    }

    public bool EnableFrameless
    {
        get => Get(ConfigId.EnableFrameless, false);
        set => Set(ConfigId.EnableFrameless, value);
    }

    public bool EnableFullScreen
    {
        get => Get(ConfigId.EnableFullScreen, false);
        set => Set(ConfigId.EnableFullScreen, value);
    }

    public bool EnableLoopBackNavigation
    {
        get => Get(ConfigId.EnableLoopBackNavigation, true);
        set => Set(ConfigId.EnableLoopBackNavigation, value);
    }

    public bool EnableAutoSwitchSiblingDir
    {
        get => Get(ConfigId.EnableAutoSwitchSiblingDir, false);
        set => Set(ConfigId.EnableAutoSwitchSiblingDir, value);
    }

    public bool EnableMultiInstances
    {
        get => Get(ConfigId.EnableMultiInstances, true);
        set => Set(ConfigId.EnableMultiInstances, value);
    }

    public bool EnableWindowTopMost
    {
        get => Get(ConfigId.EnableWindowTopMost, false);
        set => Set(ConfigId.EnableWindowTopMost, value);
    }

    public bool EnableFreePan
    {
        get => Get(ConfigId.EnableFreePan, false);
        set => Set(ConfigId.EnableFreePan, value);
    }

    public bool EnableDeleteConfirmation
    {
        get => Get(ConfigId.EnableDeleteConfirmation, true);
        set => Set(ConfigId.EnableDeleteConfirmation, value);
    }

    public bool EnableSaveConfirmation
    {
        get => Get(ConfigId.EnableSaveConfirmation, true);
        set => Set(ConfigId.EnableSaveConfirmation, value);
    }

    public bool EnablePreserveModifiedDate
    {
        get => Get(ConfigId.EnablePreserveModifiedDate, false);
        set => Set(ConfigId.EnablePreserveModifiedDate, value);
    }

    public bool EnableOpenSaveAsInCurrentFolder
    {
        get => Get(ConfigId.EnableOpenSaveAsInCurrentFolder, true);
        set => Set(ConfigId.EnableOpenSaveAsInCurrentFolder, value);
    }

    public bool EnableWelcomeImage
    {
        get => Get(ConfigId.EnableWelcomeImage, true);
        set => Set(ConfigId.EnableWelcomeImage, value);
    }

    public bool EnableLastSeenImage
    {
        get => Get(ConfigId.EnableLastSeenImage, true);
        set => Set(ConfigId.EnableLastSeenImage, value);
    }

    public bool EnableVectorRenderer
    {
        get => Get(ConfigId.EnableVectorRenderer, true);
        set => Set(ConfigId.EnableVectorRenderer, value);
    }

    public bool EnableHdrToneMapping
    {
        get => Get(ConfigId.EnableHdrToneMapping, true);
        set => Set(ConfigId.EnableHdrToneMapping, value);
    }

    public bool EnableAlwaysApplyColorProfile
    {
        get => Get(ConfigId.EnableAlwaysApplyColorProfile, false);
        set => Set(ConfigId.EnableAlwaysApplyColorProfile, value);
    }

    public bool EnableNavigationButtons
    {
        get => Get(ConfigId.EnableNavigationButtons, true);
        set => Set(ConfigId.EnableNavigationButtons, value);
    }

    public bool EnableSubfoldersLoading
    {
        get => Get(ConfigId.EnableSubfoldersLoading, false);
        set => Set(ConfigId.EnableSubfoldersLoading, value);
    }

    public bool EnableHiddenImagesLoading
    {
        get => Get(ConfigId.EnableHiddenImagesLoading, false);
        set => Set(ConfigId.EnableHiddenImagesLoading, value);
    }

    public bool EnableImageFolderGrouping
    {
        get => Get(ConfigId.EnableImageFolderGrouping, false);
        set => Set(ConfigId.EnableImageFolderGrouping, value);
    }

    public bool EnableExplorerSortOrder
    {
        get => Get(ConfigId.EnableExplorerSortOrder, true);
        set => Set(ConfigId.EnableExplorerSortOrder, value);
    }

    public bool EnableWindowFit
    {
        get => Get(ConfigId.EnableWindowFit, false);
        set => Set(ConfigId.EnableWindowFit, value);
    }

    public bool EnableCenterWindowFit
    {
        get => Get(ConfigId.EnableCenterWindowFit, true);
        set => Set(ConfigId.EnableCenterWindowFit, value);
    }

    public bool EnableOnlyLoadRawPreview
    {
        get => Get(ConfigId.EnableOnlyLoadRawPreview, false);
        set => Set(ConfigId.EnableOnlyLoadRawPreview, value);
    }

    public bool EnableOnlyLoadNonRawPreview
    {
        get => Get(ConfigId.EnableOnlyLoadNonRawPreview, false);
        set => Set(ConfigId.EnableOnlyLoadNonRawPreview, value);
    }

    public bool EnableImagePreview
    {
        get => Get(ConfigId.EnableImagePreview, true);
        set => Set(ConfigId.EnableImagePreview, value);
    }

    public bool EnableGalleryShellThumbnail
    {
        get => Get(ConfigId.EnableGalleryShellThumbnail, true);
        set => Set(ConfigId.EnableGalleryShellThumbnail, value);
    }

    public bool EnableCopyMultipleFiles
    {
        get => Get(ConfigId.EnableCopyMultipleFiles, true);
        set => Set(ConfigId.EnableCopyMultipleFiles, value);
    }

    public bool EnableCutMultipleFiles
    {
        get => Get(ConfigId.EnableCutMultipleFiles, true);
        set => Set(ConfigId.EnableCutMultipleFiles, value);
    }

    public bool EnableFileWatcher
    {
        get => Get(ConfigId.EnableFileWatcher, true);
        set => Set(ConfigId.EnableFileWatcher, value);
    }

    public bool EnableAutoOpenNewAddedImage
    {
        get => Get(ConfigId.EnableAutoOpenNewAddedImage, false);
        set => Set(ConfigId.EnableAutoOpenNewAddedImage, value);
    }

    public bool EnableDebug
    {
        get => Get(ConfigId.EnableDebug, false);
        set => Set(ConfigId.EnableDebug, value);
    }

    public double QuickSetupVersion
    {
        get => Get(ConfigId.QuickSetupVersion, 0d);
        set => Set(ConfigId.QuickSetupVersion, value);
    }

    public double PanMargin
    {
        get => Get(ConfigId.PanMargin, 0d);
        set => Set(ConfigId.PanMargin, value);
    }

    public double PanSpeed
    {
        get => Get(ConfigId.PanSpeed, 20d);
        set => Set(ConfigId.PanSpeed, value);
    }

    public double ZoomSpeed
    {
        get => Get(ConfigId.ZoomSpeed, 0d);
        set => Set(ConfigId.ZoomSpeed, value);
    }

    public double SlideshowInterval
    {
        get => Get(ConfigId.SlideshowInterval, 5d);
        set => Set(ConfigId.SlideshowInterval, value);
    }

    public double SlideshowIntervalTo
    {
        get => Get(ConfigId.SlideshowIntervalTo, 5d);
        set => Set(ConfigId.SlideshowIntervalTo, value);
    }

    public uint SlideshowImagesToNotifySound
    {
        get => Get(ConfigId.SlideshowImagesToNotifySound, 0u);
        set => Set(ConfigId.SlideshowImagesToNotifySound, value);
    }

    public uint SlideshowTransitionDuration
    {
        get => Get(ConfigId.SlideshowTransitionDuration, 1000u);
        set => Set(ConfigId.SlideshowTransitionDuration, value);
    }

    public uint NavigationTransitionDuration
    {
        get => Get(ConfigId.NavigationTransitionDuration, 300u);
        set => Set(ConfigId.NavigationTransitionDuration, value);
    }

    public uint ThumbnailSize
    {
        get => Get(ConfigId.ThumbnailSize, 70u);
        set => Set(ConfigId.ThumbnailSize, value);
    }

    public uint GalleryCacheSizeInMb
    {
        get => Get(ConfigId.GalleryCacheSizeInMb, 100u);
        set => Set(ConfigId.GalleryCacheSizeInMb, value);
    }

    public uint GalleryColumns
    {
        get => Get(ConfigId.GalleryColumns, 3u);
        set => Set(ConfigId.GalleryColumns, value);
    }

    public uint CacheMaxFiles
    {
        get => Get(ConfigId.CacheMaxFiles, 10u);
        set => Set(ConfigId.CacheMaxFiles, value);
    }

    public uint CacheMaxMemoryInMb
    {
        get => Get(ConfigId.CacheMaxMemoryInMb, 1000u);
        set => Set(ConfigId.CacheMaxMemoryInMb, value);
    }

    public double CacheMaxFileSizeInMb
    {
        get => Get(ConfigId.CacheMaxFileSizeInMb, 100d);
        set => Set(ConfigId.CacheMaxFileSizeInMb, value);
    }

    public uint CacheMaxDimension
    {
        get => Get(ConfigId.CacheMaxDimension, 8_000u);
        set => Set(ConfigId.CacheMaxDimension, value);
    }

    public double ZoomLockValue
    {
        get => Get(ConfigId.ZoomLockValue, 100d);
        set => Set(ConfigId.ZoomLockValue, value);
    }

    public uint ToolbarIconHeight
    {
        get => Get(ConfigId.ToolbarIconHeight, (uint)Const.TOOLBAR_ICON_HEIGHT);
        set => Set(ConfigId.ToolbarIconHeight, value);
    }

    public uint ImageEditQuality
    {
        get => Get(ConfigId.ImageEditQuality, 80u);
        set => Set(ConfigId.ImageEditQuality, value);
    }

    public int InAppMessageDuration
    {
        get => Get(ConfigId.InAppMessageDuration, 2000);
        set => Set(ConfigId.InAppMessageDuration, value);
    }

    public int PreviewMinWidth
    {
        get => Get(ConfigId.PreviewMinWidth, 0);
        set => Set(ConfigId.PreviewMinWidth, value);
    }

    public int PreviewMinHeight
    {
        get => Get(ConfigId.PreviewMinHeight, 0);
        set => Set(ConfigId.PreviewMinHeight, value);
    }

    public string ColorProfile
    {
        get => Get(ConfigId.ColorProfile, nameof(ColorProfileOption.CurrentMonitorProfile));
        set => Set(ConfigId.ColorProfile, value);
    }

    public string LastSeenImagePath
    {
        get => Get(ConfigId.LastSeenImagePath, string.Empty);
        set => Set(ConfigId.LastSeenImagePath, value);
    }

    public string LastOpenedTool
    {
        get => Get(ConfigId.LastOpenedTool, string.Empty);
        set => Set(ConfigId.LastOpenedTool, value);
    }

    public string LastOpenedSetting
    {
        get => Get(ConfigId.LastOpenedSetting, string.Empty);
        set => Set(ConfigId.LastOpenedSetting, value);
    }

    public string BackgroundColor
    {
        get => Get(ConfigId.BackgroundColor, "#00000000");
        set => Set(ConfigId.BackgroundColor, value);
    }

    public string SlideshowBackgroundColor
    {
        get => Get(ConfigId.SlideshowBackgroundColor, "#000000");
        set => Set(ConfigId.SlideshowBackgroundColor, value);
    }

    public string SlideshowTransition
    {
        get => Get(ConfigId.SlideshowTransition, TransitionEffects.NONE);
        set => Set(ConfigId.SlideshowTransition, value);
    }

    public string NavigationTransition
    {
        get => Get(ConfigId.NavigationTransition, TransitionEffects.NONE);
        set => Set(ConfigId.NavigationTransition, value);
    }

    public string DarkTheme
    {
        get => Get(ConfigId.DarkTheme, Const.DEFAULT_THEME);
        set => Set(ConfigId.DarkTheme, value);
    }

    public string LightTheme
    {
        get => Get(ConfigId.LightTheme, Const.DEFAULT_LIGHT_THEME);
        set => Set(ConfigId.LightTheme, value);
    }

    public string Language
    {
        get => Lang.NormalizeLanguageCode(Get(ConfigId.Language, Lang.DefaultLanguageCode));
        set => Set(ConfigId.Language, Lang.NormalizeLanguageCode(value));
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<CheckerboardType>))]
    public CheckerboardType CheckerboardMode
    {
        get => Get(ConfigId.CheckerboardMode, CheckerboardType.None);
        set => Set(ConfigId.CheckerboardMode, value);
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<ScrollbarMode>))]
    public ScrollbarMode ViewerScrollbarMode
    {
        get => Get(ConfigId.ViewerScrollbarMode, ScrollbarMode.AutoHide);
        set => Set(ConfigId.ViewerScrollbarMode, value);
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<ImageInfoOverlayMode>))]
    public ImageInfoOverlayMode ImageInfoOverlayMode
    {
        get => Get(ConfigId.ImageInfoOverlayMode, ImageInfoOverlayMode.BorderlessAutoHide);
        set => Set(ConfigId.ImageInfoOverlayMode, value);
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<BrowsingMode>))]
    public BrowsingMode BrowsingMode
    {
        get => Get(ConfigId.BrowsingMode, BrowsingMode.Turbo);
        set => Set(ConfigId.BrowsingMode, value);
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<ImageOrderBy>))]
    public ImageOrderBy ImageLoadingOrder
    {
        get => Get(ConfigId.ImageLoadingOrder, ImageOrderBy.Name);
        set => Set(ConfigId.ImageLoadingOrder, value);
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<ImageOrderType>))]
    public ImageOrderType ImageLoadingOrderType
    {
        get => Get(ConfigId.ImageLoadingOrderType, ImageOrderType.Asc);
        set => Set(ConfigId.ImageLoadingOrderType, value);
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<ZoomMode>))]
    public ZoomMode ZoomMode
    {
        get => Get(ConfigId.ZoomMode, ZoomMode.AutoZoom);
        set => Set(ConfigId.ZoomMode, value);
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<ImageInterpolation>))]
    public ImageInterpolation ImageInterpolationScaleDown
    {
        get => Get(ConfigId.ImageInterpolationScaleDown, ImageInterpolation.LinearMipmapNearest);
        set => Set(ConfigId.ImageInterpolationScaleDown, value);
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<ImageInterpolation>))]
    public ImageInterpolation ImageInterpolationScaleUp
    {
        get => Get(ConfigId.ImageInterpolationScaleUp, ImageInterpolation.Nearest);
        set => Set(ConfigId.ImageInterpolationScaleUp, value);
    }

    public AfterEditAppAction AfterEditingAction
    {
        get => Get(ConfigId.AfterEditingAction, AfterEditAppAction.Nothing);
        set => Set(ConfigId.AfterEditingAction, value);
    }

    [JsonConverter(typeof(JsonStringEnumSafeConverter<BackdropStyle>))]
    public BackdropStyle WindowBackdrop
    {
        get => Get(ConfigId.WindowBackdrop, BackdropStyle.Mica);
        set => Set(ConfigId.WindowBackdrop, value);
    }

    [JsonConverter(typeof(JsonArrayToRectConverter))]
    public Rect MainWindowBounds
    {
        get => Get(ConfigId.MainWindowBounds, new Rect(200, 200, 800, 500));
        set => Set(ConfigId.MainWindowBounds, value);
    }

    [JsonConverter(typeof(JsonArrayToRectConverter))]
    public Rect SettingsWindowBounds
    {
        get => Get(ConfigId.SettingsWindowBounds, new Rect(200, 200, 900, 580));
        set => Set(ConfigId.SettingsWindowBounds, value);
    }

    [JsonConverter(typeof(JsonArrayToRectConverter))]
    public Rect PrintWindowBounds
    {
        get => Get(ConfigId.PrintWindowBounds, new Rect(200, 200, 1000, 680));
        set => Set(ConfigId.PrintWindowBounds, value);
    }

    [JsonConverter(typeof(JsonArrayToZoomFactorConverter))]
    public double[] ZoomLevels
    {
        get => Get(ConfigId.ZoomLevels, Array.Empty<double>());
        set => Set(ConfigId.ZoomLevels, value);
    }

    public Dictionary<string, EditingApp?> EditApps
    {
        get => Get(ConfigId.EditApps, new Dictionary<string, EditingApp?>());
        set => Set(ConfigId.EditApps, value);
    }

    [JsonConverter(typeof(JsonHashSetToStringConverter))]
    public HashSet<string> FileFormats
    {
        get => Get(ConfigId.FileFormats, new HashSet<string>(DefaultFileFormats, StringComparer.OrdinalIgnoreCase));
        set => Set(ConfigId.FileFormats, value);
    }

    [JsonConverter(typeof(JsonObservableCollectionToStringConverter))]
    public ObservableCollection<string> ImageInfoTags
    {
        get => Get(ConfigId.ImageInfoTags, new ObservableCollection<string>(DefaultImageInfoTags));
        set => Set(ConfigId.ImageInfoTags, value);
    }

    public Dictionary<LangId, Hotkey[]> MenuHotkeys
    {
        get => Get(ConfigId.MenuHotkeys, new Dictionary<LangId, Hotkey[]>());
        set => Set(ConfigId.MenuHotkeys, value);
    }

    public Dictionary<MouseClickEvent, SingleAction> MouseClickActions
    {
        get => Get(ConfigId.MouseClickActions, new Dictionary<MouseClickEvent, SingleAction>());
        set => Set(ConfigId.MouseClickActions, value);
    }

    public Dictionary<MouseWheelEvent, MouseWheelAction> MouseWheelActions
    {
        get => Get(ConfigId.MouseWheelActions, new Dictionary<MouseWheelEvent, MouseWheelAction>());
        set => Set(ConfigId.MouseWheelActions, value);
    }

    public Dictionary<LayoutControl, LayoutPosition> Layout
    {
        get => Get(ConfigId.Layout, new Dictionary<LayoutControl, LayoutPosition>());
        set => Set(ConfigId.Layout, value);
    }

    public Dictionary<string, PluginTrustInfo> PluginTrust
    {
        get => Get(ConfigId.PluginTrust, new Dictionary<string, PluginTrustInfo>(StringComparer.Ordinal));
        set => Set(ConfigId.PluginTrust, value);
    }

    public ObservableCollection<ExternalTool> Tools
    {
        get => Get(ConfigId.Tools, new ObservableCollection<ExternalTool>());
        set => Set(ConfigId.Tools, value);
    }

    public Dictionary<string, JsonElement> ToolSettings
    {
        get => Get(ConfigId.ToolSettings, new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase));
        set => Set(ConfigId.ToolSettings, value);
    }

    public ObservableCollection<ToolbarItemModel> ToolbarButtons
    {
        get => Get(ConfigId.ToolbarButtons, new ObservableCollection<ToolbarItemModel>(DefaultToolbarItems));
        set => Set(ConfigId.ToolbarButtons, value);
    }

    public void ResetToDefault()
    {
        _values.Clear();
    }

    public bool HasValue(ConfigId configName) => _values.ContainsKey(configName);

    public void Set(ConfigId configName, object value)
    {
        var oldValue = Get<object?>(configName, null);
        if (Equals(value, oldValue)) return;

        _values[configName] = value;
        _ = OnPropertyChanged(value, oldValue, configName.ToString());
    }

    public T Get<T>(string configName, T defaultValue)
    {
        if (!Enum.TryParse<ConfigId>(configName, out var configId)) return defaultValue;

        return Get<T>(configId, defaultValue);
    }

    public string GetAsString(string configName)
    {
        if (!Enum.TryParse<ConfigId>(configName, out var configId)) return string.Empty;

        var value = _values.GetValueOrDefault(configId)?.ToString() ?? string.Empty;
        return value;
    }

    public T Get<T>(ConfigId configName, T defaultValue)
    {
        var value = _values.GetValueOrDefault(configName) ?? defaultValue;
        return (T)value!;
    }

    public IReadOnlyList<string> PurgePluginFileFormats(IEnumerable<string> extensions)
    {
        var current = FileFormats;
        var defaults = new HashSet<string>(DefaultFileFormats, StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();

        foreach (var ext in extensions)
        {
            if (string.IsNullOrWhiteSpace(ext)) continue;
            var normalized = ext.StartsWith('.') ? ext : "." + ext;
            if (defaults.Contains(normalized)) continue;
            if (current.Contains(normalized)) removed.Add(normalized);
        }

        if (removed.Count == 0) return [];

        var next = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        foreach (var ext in removed) next.Remove(ext);

        FileFormats = next;
        return removed;
    }

}
