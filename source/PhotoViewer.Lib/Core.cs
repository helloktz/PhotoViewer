using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.Plugins;
using ImageGlass.SDK.Plugins;
using ImageGlass.SDK.Tools;
using PhotoViewer.Tools;
using PhotoViewer.UI.Viewer;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common;

public static class Core
{
    public static readonly AppInstance AppInstance = new AppInstance("PHOTO_VIEWER_APP");

    public static event EventHandler? LanguageChanged;
    public static event EventHandler<ThemePackChangedEventArgs>? ThemeChanged;
    public static event EventHandler<PhotoUnloadedEventArgs>? PhotoUnloaded;
    public static event EventHandler<PhotoSaveEventArgs>? PhotoSaved;
    public static event EventHandler<DestColorProfileChangedEventArgs>? ColorProfileChanged;

    private static string _initImagePathFromArgs = string.Empty;

    public static IAppBuildInfo BuildInfo { get; set; } = null!;

    public static IWindowColorProfileProvider? ColorProfileProvider { get; set; } = null;

    public static IPhotoPreviewProvider PreviewProvider { get; set; } = null!;

    public static IFileSearchProvider FileSearchProvider { get; set; } = null!;

    public static IShellProvider? ShellProvider { get; set; } = null;

    public static IPrintProvider PrintProvider { get; set; } = null!;

    public static IPipeSecurityProvider? PipeSecurityProvider { get; set; } = null;

    public static AppAPIProvider API { get; set; } = null!;

    public static SlideshowProvider? Slideshow { get; set; } = null;

    public static PluginRegistry PluginRegistry { get; } = new();

    public static ToolRegistry ToolRegistry { get; } = new();

    public static CodecRegistry CodecRegistry { get; } = new();

    public static Config Config { get; set; } = new();

    public static string[] Args { get; set; } = [];

    public static PhotoManager Photos { get; set; } = new();

    public static bool IsBusy { get; set; } = false;

    public static bool IsSystemDarkMode { get; set; } = true;

    public static Color AccentColor { get; private set; } = new();

    public static IgTheme Theme { get; private set; } = new();

    public static Lang Lang
    {
        get; set
        {
            if (field == value) return;

            field = value;
            Core.OnLanguageChanged();
        }
    } = new();

    public static HdrToneMappingOptions HdrToneMappingConfig { get; set; } = new();

    public static string InputImagePathFromArgs => _initImagePathFromArgs;

    public static PhotoTransform ImageTransform { get; set; } = new();

    public static HashSet<string> StringClipboard { get; set; } = [];

    public static Photo? ClipboardImage { get; set; }

    public static string? TempImagePath { get; set; }

    public static SKColorSpace? DestColorProfile { get; private set; }

    public static bool IsDestColorProfileSupported { get; private set; } = true;

    public static ColorChannels ColorChannels { get; set; } = ColorChannels.RGBA;

    public static void Dispose()
    {
        Config.CleanUpPropertyChangedEvents();

        ToolRegistry.Dispose();
        PluginRegistry.Dispose();
        CodecRegistry.Dispose();

        DisposeClipboardPhoto();

        Core.Slideshow?.Dispose();
        Core.Slideshow = null;

        ProjectorToolControl.DisposeManager();

        Core.Photos.Dispose();
        Core.ColorProfileProvider?.Dispose();
        Core.ColorProfileProvider = null;

        Core.FileSearchProvider?.Dispose();
        Core.FileSearchProvider = null!;

        SleepGuard.ReleaseAll();
        Core.ShellProvider?.Dispose();
        Core.ShellProvider = null;
    }

    public static Task PluginDiscoveryTask { get; private set; } = Task.CompletedTask;

    public static void DiscoverPlugins()
    {
        PluginDiscoveryTask = Task.Run(() =>
        {
            StartupTrace.Mark("plugins:discover:begin");
            var pluginsDir = BHelper.ConfigDir(Dir.Plugins);

            PluginRegistry.CleanupTrashDirs(pluginsDir);

            var discovered = PluginRegistry.DiscoverManifests(pluginsDir);

            var pluginExtensions = new List<string>();

            foreach (var (manifest, dir) in discovered)
            {
                try
                {
                    RegisterPluginCodecs(manifest, dir, pluginExtensions);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Core.DiscoverPlugins] '{manifest.Id}' failed: {ex.Message}");
                }
            }

            StartupTrace.Mark("plugins:discover:end");

            if (pluginExtensions.Count > 0)
            {
                Dispatcher.UIThread.Post(() => Config.PurgePluginFileFormats(pluginExtensions));
            }

            StartupTrace.Flush();
        });
    }

    private static readonly Dictionary<string, List<NativeCodecProxy>> _pluginProxies = new(StringComparer.Ordinal);
    private static readonly Lock _pluginProxiesLock = new();

    private static bool RegisterPluginCodecs(PluginManifest manifest, string dir, List<string> pluginExtensions)
    {
        var handle = PluginRegistry.LoadAndProbe(manifest, dir);
        if (handle is null) return false;

        return RegisterProxies(handle, pluginExtensions);
    }

    private static bool RegisterProxies(NativePlugin handle, List<string> declaredExtensions)
    {
        var proxies = new List<NativeCodecProxy>();
        foreach (var proxy in PluginRegistry.CreateProxies(handle))
        {
            if (proxy.DecodingExtensions.Count == 0 && proxy.EncodingExtensions.Count == 0)
            {
                declaredExtensions.AddRange(proxy.DeclaredDecodingExtensions);
                proxy.Dispose();
                continue;
            }

            try
            {
                CodecRegistry.Register(proxy);
                proxies.Add(proxy);
                declaredExtensions.AddRange(proxy.DeclaredDecodingExtensions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Core.RegisterProxies] register '{proxy.CodecId}' failed: {ex.Message}");
                proxy.Dispose();
            }
        }

        if (proxies.Count == 0) return false;

        lock (_pluginProxiesLock)
        {
            _pluginProxies[handle.PluginId] = proxies;
        }
        return true;
    }

    public static void ReloadPluginCodecs(string pluginId)
    {
        var handle = PluginRegistry.GetLoadedPlugin(pluginId);
        if (handle is null) return;

        Photos.ClearCache();

        List<NativeCodecProxy>? old;
        lock (_pluginProxiesLock)
        {
            _pluginProxies.Remove(pluginId, out old);
        }

        var oldDecodeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (old is not null)
        {
            foreach (var proxy in old)
            {
                foreach (var ext in proxy.DecodingExtensions) oldDecodeExts.Add(ext);

                CodecRegistry.Unregister(proxy);
                proxy.Dispose();
            }
        }

        var declared = new List<string>();
        RegisterProxies(handle, declared);

        if (declared.Count > 0) Config.PurgePluginFileFormats(declared);

        var newDecodeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_pluginProxiesLock)
        {
            if (_pluginProxies.TryGetValue(pluginId, out var fresh))
            {
                foreach (var proxy in fresh)
                {
                    foreach (var ext in proxy.DecodingExtensions) newDecodeExts.Add(ext);
                }
            }
        }

        if (!oldDecodeExts.SetEquals(newDecodeExts))
        {
            InvalidateCodecsAndReload();
            AppAPIProvider.IG_ReloadList();
        }
    }

    public static async Task<bool> EnablePluginAsync(PluginManifest manifest, string pluginDir,
        bool formatsOff = false)
    {
        if (PluginRegistry.IsLoaded(manifest.Id)) return true;

        var handle = await Task.Run(() => PluginRegistry.LoadAndProbe(manifest, pluginDir));
        if (handle is null) return false;

        if (formatsOff)
        {
            var (decode, encode) = PluginRegistry.GetDeclaredCodecExtensions(manifest.Id);
            await PluginTrustPolicy.SetExtensionExclusionsAsync(manifest.Id, decode, encode);
        }

        var extensions = new List<string>();
        var registered = RegisterProxies(handle, extensions);

        if (extensions.Count > 0) Config.PurgePluginFileFormats(extensions);

        if (!registered) return true;

        Photos.ClearCache();
        InvalidateCodecsAndReload();

        if (extensions.Count > 0) AppAPIProvider.IG_ReloadList();

        return true;
    }

    public static void DisablePlugin(string pluginId)
    {
        Photos.ClearCache();

        List<NativeCodecProxy>? proxies;
        lock (_pluginProxiesLock)
        {
            _pluginProxies.Remove(pluginId, out proxies);
        }

        if (proxies is not null)
        {
            foreach (var proxy in proxies)
            {
                CodecRegistry.Unregister(proxy);
                proxy.Dispose();
            }
        }

        PluginRegistry.UnloadPlugin(pluginId);

        InvalidateCodecsAndReload();
    }

    public static HashSet<string> GetSupportedFileExtensions()
    {
        var set = new HashSet<string>(Config.FileFormats, StringComparer.OrdinalIgnoreCase);

        lock (_pluginProxiesLock)
        {
            foreach (var proxies in _pluginProxies.Values)
            {
                foreach (var proxy in proxies)
                {
                    foreach (var ext in proxy.DecodingExtensions) set.Add(ext);
                }
            }
        }

        return set;
    }

    private static void InvalidateCodecsAndReload()
    {
        CodecRegistry.InvalidateSelectionCaches();
        AppAPIProvider.IG_Reload(false);
    }

    public static void RegisterExternalTools()
    {
        foreach (var tool in Config.Tools)
        {
            if (string.IsNullOrEmpty(tool.ToolId)) continue;
            if (ToolRegistry.Contains(tool.ToolId)) continue;

            try
            {
                var proxy = new ExternalToolProxy(tool, ToolRegistry.ExternalTools);
                ToolRegistry.Register(tool.ToolId, proxy);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Core.RegisterExternalTools] '{tool.ToolId}' failed: {ex.Message}");
            }
        }
    }

    public static void ReloadExternalTools()
    {
        ToolRegistry.RemoveExternalTools();
        RegisterExternalTools();
    }

    public static bool SetTheme(IgTheme theme)
    {
        if (Core.Theme == theme) return false;

        Core.Theme = theme;
        return true;
    }

    public static bool SetAccentColor(Color accent)
    {
        if (Core.AccentColor == accent) return false;

        Core.AccentColor = accent;
        return true;
    }

    public static void UpdateBaseResources()
    {
        if (Application.Current is not App app) return;

        if (FontManager.Current.DefaultFontFamily.Name.Equals("Segoe UI"))
        {
            var fm = FontFamily.Parse("Segoe UI Variable Text");
            Resx.Set(ResxId.ContentControlThemeFontFamily, fm);
        }

        if (BHelper.OS != OSType.Windows)
        {
            app.Resources["ControlContentThemeFontSize"] =
                app.Resources["ToolTipContentThemeFontSize"] = Const.FONT_SIZE_BODY;
        }
    }

    public static void UpdateAppThemedColorResources()
    {
        if (Application.Current is not App app) return;

        Resx.Set(ResxId.IG_ThemeBackgroundBrush, AppThemeColors.BgBrush);
        Resx.Set(ResxId.IG_ThemeForegroundBrush, AppThemeColors.TextColorBrush);
        Resx.Set(ResxId.IG_ThemeToolbarBackgroundBrush, AppThemeColors.ToolbarBgBrush);
        Resx.Set(ResxId.IG_ThemeGalleryBackgroundBrush, AppThemeColors.GalleryBgBrush);
        Resx.Set(ResxId.IG_ThemeMenuBackgroundBrush, AppThemeColors.MenuBgBrush);
        UpdateViewerBackgroundBrushResource();

        if (Core.Theme.Settings.IsDarkMode)
        {
            Resx.Set(ResxId.IG_BackgroundInfoBrush, AppThemeColors.BackgroundInfoDark.ToBrush());
            Resx.Set(ResxId.IG_BackgroundSuccessBrush, AppThemeColors.BackgroundSuccessDark.ToBrush());
            Resx.Set(ResxId.IG_BackgroundWarningBrush, AppThemeColors.BackgroundWarningDark.ToBrush());
            Resx.Set(ResxId.IG_BackgroundDangerBrush, AppThemeColors.BackgroundDangerDark.ToBrush());

            Resx.Set(ResxId.IG_TextSuccessBrush, AppThemeColors.TextSuccessDark.ToBrush());
            Resx.Set(ResxId.IG_TextWarningBrush, AppThemeColors.TextWarningDark.ToBrush());
            Resx.Set(ResxId.IG_TextDangerBrush, AppThemeColors.TextDangerDark.ToBrush());

            Resx.Set(ResxId.IG_TextSuccessColor, AppThemeColors.TextSuccessDark);
            Resx.Set(ResxId.IG_TextWarningColor, AppThemeColors.TextWarningDark);
            Resx.Set(ResxId.IG_TextDangerColor, AppThemeColors.TextDangerDark);
        }
        else
        {
            Resx.Set(ResxId.IG_BackgroundInfoBrush, AppThemeColors.BackgroundInfoLight.ToBrush());
            Resx.Set(ResxId.IG_BackgroundSuccessBrush, AppThemeColors.BackgroundSuccessLight.ToBrush());
            Resx.Set(ResxId.IG_BackgroundWarningBrush, AppThemeColors.BackgroundWarningLight.ToBrush());
            Resx.Set(ResxId.IG_BackgroundDangerBrush, AppThemeColors.BackgroundDangerLight.ToBrush());

            Resx.Set(ResxId.IG_TextSuccessBrush, AppThemeColors.TextSuccessLight.ToBrush());
            Resx.Set(ResxId.IG_TextWarningBrush, AppThemeColors.TextWarningLight.ToBrush());
            Resx.Set(ResxId.IG_TextDangerBrush, AppThemeColors.TextDangerLight.ToBrush());

            Resx.Set(ResxId.IG_TextSuccessColor, AppThemeColors.TextSuccessLight);
            Resx.Set(ResxId.IG_TextWarningColor, AppThemeColors.TextWarningLight);
            Resx.Set(ResxId.IG_TextDangerColor, AppThemeColors.TextDangerLight);
        }

        var bgNeutralAlpha = 100;
        var bgColor = AppThemeColors.BgBrush.Color.NoAlpha();
        var bgNeutral = bgColor.Blend(Core.Theme.InvertedBaseColor, 0.95f, bgNeutralAlpha);
        var borderNeutral = bgColor.Blend(Core.Theme.InvertedBaseColor, 0.8f, bgNeutralAlpha / 2);
        var borderControl = bgColor.Blend(Core.Theme.InvertedBaseColor, 0.5f, bgNeutralAlpha / 2);

        Resx.Set(ResxId.IG_BackgroundNeutralBrush, bgNeutral.ToBrush());
        Resx.Set(ResxId.IG_BorderNeutralBrush, borderNeutral.ToBrush());
        Resx.Set(ResxId.IG_BorderControlBrush, borderControl.ToBrush());
        Resx.Set(ResxId.IG_OverlayBackgroundBrush, bgColor.A(220).ToBrush());

        var textBrush = AppThemeColors.TextColorBrush.Color.ToBrush();
        var textPlaceholder = AppThemeColors.TextColorBrush.Color.Blend(Core.Theme.BaseColor, 0.7f, AppThemeColors.TextColorBrush.A);

        Resx.Set(ResxId.SystemControlForegroundBaseHighBrush, textBrush);
        Resx.Set(ResxId.TextControlForeground, textBrush);
        Resx.Set(ResxId.TextControlForegroundPointerOver, textBrush);
        Resx.Set(ResxId.TextControlForegroundFocused, textBrush);
        Resx.Set(ResxId.TextControlPlaceholderForeground, textPlaceholder.ToBrush());
        Resx.Set(ResxId.CheckBoxForegroundChecked, textBrush);
        Resx.Set(ResxId.CheckBoxForegroundCheckedPointerOver, textBrush);
        Resx.Set(ResxId.CheckBoxForegroundUnchecked, textBrush);
        Resx.Set(ResxId.CheckBoxForegroundUncheckedPointerOver, textBrush);

        Resx.Set(ResxId.TextControlBorderBrush, borderControl);
        Resx.Set(ResxId.TextControlBorderBrushDisabled, borderControl);
        Resx.Set(ResxId.ComboBoxBorderBrush, borderControl);
        Resx.Set(ResxId.CheckBoxCheckBackgroundStrokeUnchecked, borderControl);

        var menuBg = AppThemeColors.MenuBgBrush.Color.NoAlpha();
        var menuBorder = Core.Theme.InvertedBaseColor.WithAlpha(30);
        var menuText = AppThemeColors.TextColorBrush.Color;
        var menuTextDisabled = menuText.Blend(Core.Theme.InvertedBaseColor, 0.8f, 100);

        Resx.Set(ResxId.MenuFlyoutPresenterBackground, menuBg);
        Resx.Set(ResxId.MenuFlyoutPresenterBorderBrush, menuBorder);
        Resx.Set(ResxId.IG_MenuSeparatorBackground, menuText.A(20));

        Resx.Set(ResxId.MenuFlyoutItemForeground, menuText);
        Resx.Set(ResxId.MenuFlyoutItemForegroundPointerOver, menuText);
        Resx.Set(ResxId.MenuFlyoutItemForegroundPressed, menuText);
        Resx.Set(ResxId.MenuFlyoutItemForegroundDisabled, menuTextDisabled);

        var hotkeyTextColor = menuText.A(200);
        Resx.Set(ResxId.MenuFlyoutItemKeyboardAcceleratorTextForeground, hotkeyTextColor);
        Resx.Set(ResxId.MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver, hotkeyTextColor);
        Resx.Set(ResxId.MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed, hotkeyTextColor);
        Resx.Set(ResxId.MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled, menuTextDisabled);

        Resx.Set(ResxId.MenuFlyoutSubItemChevron, menuText);
        Resx.Set(ResxId.MenuFlyoutSubItemChevronPointerOver, menuText);
        Resx.Set(ResxId.MenuFlyoutSubItemChevronPressed, menuText);
        Resx.Set(ResxId.MenuFlyoutSubItemChevronDisabled, menuTextDisabled);
        Resx.Set(ResxId.MenuFlyoutSubItemChevronSubMenuOpened, menuText);

        Resx.Set(ResxId.ToolTipForeground, menuText);
        Resx.Set(ResxId.ToolTipBackground, menuBg);
        Resx.Set(ResxId.ToolTipBorder, menuBorder);

        Resx.Set(ResxId.ComboBoxForeground, textBrush);
        Resx.Set(ResxId.ComboBoxDropDownBackground, menuBg);
        Resx.Set(ResxId.ComboBoxDropDownBorderBrush, menuBorder);

        Resx.Set(ResxId.ComboBoxItemForeground, menuText);
        Resx.Set(ResxId.ComboBoxItemForegroundPointerOver, menuText);
        Resx.Set(ResxId.ComboBoxItemForegroundPressed, menuText);
        Resx.Set(ResxId.ComboBoxItemForegroundDisabled, menuTextDisabled);
    }

    public static void UpdateViewerBackgroundBrushResource()
    {
        if (Application.Current is not App app) return;

        var bgColorText = Config.EnableSlideshow
            ? Config.SlideshowBackgroundColor
            : Config.BackgroundColor;
        var bgColor = BHelper.ColorFromHex(bgColorText, AccentColor);

        if (bgColor.IsEmpty)
        {
            bgColor = AppThemeColors.BgBrush.Color;
        }

        Resx.Set(ResxId.IG_ViewerBackgroundBrush, bgColor.ToBrush());

        var toolHostBg = bgColor.Blend(Core.Theme.BaseColor, 0.55, Math.Max((byte)50, bgColor.A));
        if (bgColor.NoAlpha() == Core.Theme.BaseColor)
        {
            toolHostBg = bgColor.Blend(Core.Theme.InvertedBaseColor, 0.95, Math.Max((byte)50, bgColor.A));
        }

        Resx.Set(ResxId.IG_ToolHostBackgroundBrush, toolHostBg.ToBrush());

        var projectorBg = BHelper.ColorFromHex(Config.SlideshowBackgroundColor, AccentColor);
        if (projectorBg.IsEmpty) projectorBg = bgColor;

        Resx.Set(ResxId.IG_ProjectorBackgroundBrush, projectorBg.ToBrush());
    }

    public static void UpdateAccentColorResources()
    {
        if (Application.Current is not App app) return;

        var accent = Core.AccentColor;
        var accentLight1 = accent.WithBrightness(0.12f);
        var accentLight2 = accent.WithBrightness(0.28f);
        var accentLight3 = accent.WithBrightness(0.5f);

        var accentDark1 = accent.WithBrightness(-0.07f);
        var accentDark2 = accent.WithBrightness(-0.20f);
        var accentDark3 = accent.WithBrightness(-0.35f);

        Resx.Set(ResxId.SystemAccentColor, accent);
        Resx.Set(ResxId.SystemAccentColorLight1, accentLight1);
        Resx.Set(ResxId.SystemAccentColorLight2, accentLight2);
        Resx.Set(ResxId.SystemAccentColorLight3, accentLight3);
        Resx.Set(ResxId.SystemAccentColorDark1, accentDark1);
        Resx.Set(ResxId.SystemAccentColorDark2, accentDark2);
        Resx.Set(ResxId.SystemAccentColorDark3, accentDark3);
        Resx.Set(ResxId.IG_AccentBrush, accent.ToBrush());

        var textAccent = Core.Theme.Settings.IsDarkMode ? accentLight3 : accentDark1;
        Resx.Set(ResxId.IG_TextAccentColor, textAccent);

        var accentText = accent.InvertBlackOrWhite();
        var accentTextBrush = accentText.ToBrush();
        Resx.Set(ResxId.AccentButtonForeground, accentTextBrush);
        Resx.Set(ResxId.AccentButtonForegroundPointerOver, accentTextBrush);
        Resx.Set(ResxId.AccentButtonForegroundPressed, accentTextBrush);
        Resx.Set(ResxId.AccentButtonForegroundDisabled, accentText.A(128).ToBrush());

        var borderHoverBrush = accentLight1.ToBrush();
        Resx.Set(ResxId.TextControlBorderBrushPointerOver, borderHoverBrush);
        Resx.Set(ResxId.ComboBoxBorderBrushPointerOver, borderHoverBrush);
        Resx.Set(ResxId.CheckBoxCheckBackgroundStrokeUncheckedPointerOver, borderHoverBrush);

        var btnBgAlphaGap = Core.Theme.Settings.IsDarkMode ? 0 : -50;
        var btnBg = accent.A(0);
        var btnBgHover = accent.A((byte)(90 + btnBgAlphaGap));
        var btnBgPressed = accent.A((byte)(130 + btnBgAlphaGap));
        var btnBgChecked = accent.A((byte)(150 + btnBgAlphaGap));

        Resx.Set(ResxId.IG_ToolButtonBackground, btnBg);
        Resx.Set(ResxId.IG_ToolButtonBackgroundHover, btnBgHover);
        Resx.Set(ResxId.IG_ToolButtonBackgroundPressed, btnBgPressed);
        Resx.Set(ResxId.IG_ToolButtonBackgroundChecked, btnBgChecked);

        Resx.Set(ResxId.MenuFlyoutItemBackground, btnBg);
        Resx.Set(ResxId.MenuFlyoutItemBackgroundPointerOver, btnBgHover);
        Resx.Set(ResxId.MenuFlyoutItemBackgroundPressed, btnBgPressed);

        Resx.Set(ResxId.ComboBoxItemBackground, btnBg);
        Resx.Set(ResxId.ComboBoxItemBackgroundPointerOver, btnBgHover);
        Resx.Set(ResxId.ComboBoxItemBackgroundPressed, btnBgPressed);
        Resx.Set(ResxId.ComboBoxItemBackgroundSelected, btnBgChecked);
    }

    public static void SetAppDarkThemeVariant(bool enable)
    {
        if (enable)
        {
            Application.Current?.RequestedThemeVariant = ThemeVariant.Dark;
        }
        else
        {
            Application.Current?.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    public static void UpdateInitImagePath(string? path = null)
    {
        var pathToLoad = path;

        if (string.IsNullOrWhiteSpace(pathToLoad) && Core.Args.Length >= 2)
        {
            var cmdPath = Core.Args
                .Skip(1)
                .FirstOrDefault(i => !i.StartsWith(Const.CONFIG_CMD_PREFIX, StringComparison.Ordinal)
                    && !i.StartsWith("--", StringComparison.Ordinal));

            if (!string.IsNullOrEmpty(cmdPath))
            {
                pathToLoad = cmdPath;
            }
        }

        _initImagePathFromArgs = BHelper.ResolvePath(pathToLoad);
    }

    public static async Task<string?> SavePhotoAsTempFileAsync(string ext = ".png")
    {
        var photo = Core.ClipboardImage ?? Core.Photos.Current;
        var state = new TempImageState(photo, Core.ImageTransform.Orientation, Core.ImageTransform.IsColorInverted);

        if (File.Exists(Core.TempImagePath) && state == _tempImageState)
        {
            var extension = Path.GetExtension(Core.TempImagePath);

            if (extension.Equals(ext, StringComparison.OrdinalIgnoreCase))
            {
                return Core.TempImagePath;
            }
        }

        var tempFilePath = BHelper.ConfigDir(Dir.Temporary, $"ig_temp_{DateTime.UtcNow:yyyy-MM-dd-hh-mm-ss}{ext}");

        if (photo is not null)
        {
            try
            {
                var quality = ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ? 10u : 85u;

                await photo.SaveAsAsync(tempFilePath, Core.ImageTransform, quality);

                Core.TempImagePath = tempFilePath;
                _tempImageState = state;
            }
            catch
            {
                Core.TempImagePath = null;
            }
        }

        return Core.TempImagePath;
    }

    private static TempImageState _tempImageState;
    private readonly record struct TempImageState(Photo? Photo, ImageOrientation Orientation, bool IsColorInverted);

    public static void DisposeClipboardPhoto()
    {
        Core.ClipboardImage?.Dispose();
        Core.ClipboardImage = null;
        Core.TempImagePath = null;
    }

    public static void UpdateDestColorProfile(bool requiresPhotoReload = true)
    {
        var results = SkiaCodec.GetColorProfileByName(Core.Config.ColorProfile);
        Core.IsDestColorProfileSupported = results.IsSupported;

        Core.CodecRegistry.InvalidateSelectionCaches();

        var oldProfile = Core.DestColorProfile;
        Core.DestColorProfile = results.ColorSpace;
        if (!ReferenceEquals(oldProfile, results.ColorSpace)) oldProfile?.Dispose();

        Core.OnColorProfileChanged(requiresPhotoReload);
    }

    public static async Task LoadClipboardPhotoAsync(Photo? photo)
    {
        if (API is null) return;
        await AppAPIProvider.LoadClipboardPhotoAsync(photo);
    }

    public static void OnColorProfileChanged(bool requiresPhotoReload = true)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ColorProfileChanged?.Invoke(null, new(requiresPhotoReload));
        });

        ToolRegistry.ExternalTools.BroadcastToAll(MessageTypes.COLOR_PROFILE_CHANGED);
    }

    public static void OnThemeChanged(string propName = "")
    {
        Dispatcher.UIThread.Post(() =>
        {
            ThemeChanged?.Invoke(null, new ThemePackChangedEventArgs(propName));
        });

        ToolRegistry.ExternalTools.BroadcastToAll(MessageTypes.THEME_CHANGED, new ThemeInfo
        {
            IsDarkMode = Theme.Settings.IsDarkMode,
            AccentColor = AccentColor.ToString(),
            BackgroundColor = Config.BackgroundColor,
        });
    }

    public static void OnLanguageChanged()
    {
        if (Avalonia.Application.Current is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            LanguageChanged?.Invoke(null, new());
        });

        ToolRegistry.ExternalTools.BroadcastToAll(MessageTypes.LANGUAGE_CHANGED, new LanguageChangedEventArgs
        {
            Code = Lang.Metadata.Code,
            EnglishName = Lang.Metadata.EnglishName,
            LocalName = Lang.Metadata.LocalName,
        });
    }

    public static void OnPhotoUnloaded(PhotoUnloadedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PhotoUnloaded?.Invoke(null, e);
        });
    }

    public static void OnPhotoSaved(PhotoSaveEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PhotoSaved?.Invoke(null, e);
        });
    }

    public static void BroadcastPhotoChanged(Photo? photo)
    {
        if (photo is null) return;

        ToolRegistry.ExternalTools.BroadcastToAll(MessageTypes.PHOTO_CHANGED, new PhotoChangedEventArgs
        {
            FilePath = photo.FilePath,
            Width = (int)(photo.Metadata?.OriginalWidth ?? 0),
            Height = (int)(photo.Metadata?.OriginalHeight ?? 0),
            Format = photo.Metadata?.FileExtension,
            FrameCount = (int)(photo.Metadata?.FrameCount ?? 1),
            CanAnimate = photo.Metadata?.CanAnimate ?? false,
        });
    }

    internal static void Viewer_PhotoLoadingForPlugins(ViewerControl sender, PhotoLoadingEventArgs e)
    {
        if (e.State == PhotoState.Loaded)
        {
            BroadcastPhotoChanged(e.Photo);
        }
    }

    internal static void Viewer_PointerMovedForPlugins(ViewerControl sender, ViewerPointerEventArgs e)
    {
        ToolRegistry.ExternalTools.BroadcastToSubscribed(
            MessageTypes.POINTER_MOVED,
            new PointerEventArgs
            {
                SourceX = e.SourcePoint.X,
                SourceY = e.SourcePoint.Y,
                ClientX = (float)e.Point.Position.X,
                ClientY = (float)e.Point.Position.Y,
            },
            s => s.PointerMoved);
    }

    internal static void Viewer_PointerPressedForPlugins(ViewerControl sender, ViewerPointerEventArgs e)
    {
        ToolRegistry.ExternalTools.BroadcastToSubscribed(
            MessageTypes.POINTER_PRESSED,
            new PointerEventArgs
            {
                SourceX = e.SourcePoint.X,
                SourceY = e.SourcePoint.Y,
                ClientX = (float)e.Point.Position.X,
                ClientY = (float)e.Point.Position.Y,
            },
            s => s.PointerPressed);
    }

    internal static void Viewer_SelectionChangedForPlugins(ViewerControl sender, ViewerSelectionChangedEventArgs e)
    {
        var src = e.SourceSelection;
        ToolRegistry.ExternalTools.BroadcastToSubscribed(
            MessageTypes.SELECTION_CHANGED,
            src == default ? null : new SelectionEventArgs
            {
                X = (float)src.X,
                Y = (float)src.Y,
                Width = (float)src.Width,
                Height = (float)src.Height,
            },
            s => s.SelectionChanged);
    }

    internal static void Viewer_FrameChangedForPlugins(ViewerControl sender, PhotoFrameChangedEventArgs e)
    {
        ToolRegistry.ExternalTools.BroadcastToSubscribed(
            MessageTypes.FRAME_CHANGED,
            new FrameChangedPayload
            {
                FrameIndex = (int)e.CurrentFrame,
            },
            s => s.FrameChanged);
    }

}
