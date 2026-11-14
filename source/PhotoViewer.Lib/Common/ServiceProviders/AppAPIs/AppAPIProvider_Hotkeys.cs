using Avalonia.Input;
using PhotoViewer.Common.Actions;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using PhotoViewer.Common.Windows;
using PhotoViewer.Tools;
using PhotoViewer.UI;
using PhotoViewer.UI.Viewer;
using PhotoViewer.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MKeys = Avalonia.Input.KeyModifiers;

namespace PhotoViewer.Common.ServiceProviders;

public partial class AppAPIProvider
{
    private Hotkey? _lastHotkeyPressed = null;
    private InterlockedBool _isQuickBrowsingPhotos = new(false);

    public bool IsQuickBrowsing => _isQuickBrowsingPhotos;

    public static IReadOnlyCollection<HotkeySingleAction> DefaultMenuList => [
        new(LangId.Menu_MnuMain,                 API.IG_OpenMainMenu,        MKeys.Alt, Key.F),

        new(LangId.Menu_MnuOpenFile,             API.IG_OpenFile,            Hotkey.Ctrl, Key.O),
        new(LangId.Menu_MnuNewWindow,            API.IG_NewWindow,           Hotkey.Ctrl, Key.N),
        new(LangId.Menu_MnuSave,                 API.IG_Save,                Hotkey.Ctrl, Key.S),
        new(LangId.Menu_MnuSaveAs,               API.IG_SaveAs,              Hotkey.Ctrl | MKeys.Shift, Key.S),
        new(LangId.Menu_MnuExportFrames,         API.IG_ExportImageFrames,   Hotkey.Ctrl, Key.J),
        new(LangId.Menu_MnuPrint,                API.IG_Print,               Hotkey.Ctrl, Key.P),
        .. SupportedOn(BHelper.OS == OSType.Windows, [
            new(LangId.Menu_MnuOpenWith,         API.IG_OpenWith,            Key.D),
        ]),
        new(LangId.Menu_MnuOpenLocation,         API.IG_OpenLocation,        Key.L),
        new(LangId.Menu_MnuRename,               API.IG_Rename,              Key.F2),
        new(LangId.Menu_MnuMoveToRecycleBin,     API.IG_Delete, "true",      [new(Hotkey.Delete)]),
        new(LangId.Menu_MnuDeleteFromHardDisk,   API.IG_Delete, "false",     [new(MKeys.Shift, Hotkey.Delete)]),
        new(LangId.Menu_MnuImageProperties,      API.IG_OpenProperties,      MKeys.Alt, Key.Enter),

        new(LangId.Menu_MnuViewNext,             API.IG_ViewNext,            Key.Right),
        new(LangId.Menu_MnuViewPrevious,         API.IG_ViewPrevious,        Key.Left),
        new(LangId.Menu_MnuGoTo,                 API.IG_Goto,                Hotkey.Ctrl, Key.F),
        new(LangId.Menu_MnuGoToFirst,            API.IG_GotoFirst,           Key.Home),
        new(LangId.Menu_MnuGoToLast,             API.IG_GotoLast,            Key.End),
        new(LangId.Menu_MnuViewNextFrame,        API.IG_ViewNextFrame,       Hotkey.Ctrl, Key.Right),
        new(LangId.Menu_MnuViewPreviousFrame,    API.IG_ViewPreviousFrame,   Hotkey.Ctrl, Key.Left),
        new(LangId.Menu_MnuViewFirstFrame,       API.IG_ViewFirstFrame,      Hotkey.Ctrl, Key.Up),
        new(LangId.Menu_MnuViewLastFrame,        API.IG_ViewLastFrame,       Hotkey.Ctrl, Key.Down),

        new(LangId.Menu_MnuCustomZoom,       API.IG_CustomZoom,          Key.Z),
        new(LangId.Menu_MnuActualSize,       API.IG_SetZoom, "1",        [new(Key.D0), new(Key.NumPad0)]),
        new(LangId.Menu_MnuZoomIn,           API.IG_ZoomIn,              Key.Add),
        new(LangId.Menu_MnuZoomOut,          API.IG_ZoomOut,             Key.Subtract),
        new(LangId.Menu_MnuAutoZoom,         API.IG_SetZoomMode, nameof(ZoomMode.AutoZoom),      [new(Key.D1), new(Key.NumPad1)]),
        new(LangId.Menu_MnuLockZoom,         API.IG_SetZoomMode, nameof(ZoomMode.LockZoom),      [new(Key.D2), new(Key.NumPad2)]),
        new(LangId.Menu_MnuScaleToWidth,     API.IG_SetZoomMode, nameof(ZoomMode.ScaleToWidth),  [new(Key.D3), new(Key.NumPad3)]),
        new(LangId.Menu_MnuScaleToHeight,    API.IG_SetZoomMode, nameof(ZoomMode.ScaleToHeight), [new(Key.D4), new(Key.NumPad4)]),
        new(LangId.Menu_MnuScaleToFit,       API.IG_SetZoomMode, nameof(ZoomMode.ScaleToFit),    [new(Key.D5), new(Key.NumPad5)]),
        new(LangId.Menu_MnuScaleToFill,      API.IG_SetZoomMode, nameof(ZoomMode.ScaleToFill),   [new(Key.D6), new(Key.NumPad6)]),

        new(LangId.Menu_MnuPanLeft,          API.IG_PanLeft,             [new(MKeys.Alt, Key.Left)]),
        new(LangId.Menu_MnuPanRight,         API.IG_PanRight,            [new(MKeys.Alt, Key.Right)]),
        new(LangId.Menu_MnuPanUp,            API.IG_PanUp,               [new(MKeys.Alt, Key.Up)]),
        new(LangId.Menu_MnuPanDown,          API.IG_PanDown,             [new(MKeys.Alt, Key.Down)]),
        new(LangId.Menu_MnuPanToLeftSide,    API.IG_PanToLeft,           [new(Hotkey.Ctrl | MKeys.Alt, Key.Left)]),
        new(LangId.Menu_MnuPanToRightSide,   API.IG_PanToRight,          [new(Hotkey.Ctrl | MKeys.Alt, Key.Right)]),
        new(LangId.Menu_MnuPanToTop,         API.IG_PanToTop,            [new(Hotkey.Ctrl | MKeys.Alt, Key.Up)]),
        new(LangId.Menu_MnuPanToBottom,      API.IG_PanToBottom,         [new(Hotkey.Ctrl | MKeys.Alt, Key.Down)]),

        new(LangId.Menu_MnuRefresh,                     API.IG_Refresh),
        new(LangId.Menu_MnuReload,                      API.IG_Reload,              Hotkey.Ctrl, Key.R),
        new(LangId.Menu_MnuReloadImageList,             API.IG_ReloadList,          Hotkey.Ctrl | MKeys.Shift, Key.R),
        new(LangId.Menu_MnuUnload,                      API.IG_Unload,              Key.U),
        new(LangId.Settings_EnableExplorerSortOrder,    API.IG_ToggleExplorerSortOrder),
        new(LangId.ImageOrderBy_Name,               API.IG_SetLoadingOrderBy,   nameof(ImageOrderBy.Name)),
        new(LangId.ImageOrderBy_Random,             API.IG_SetLoadingOrderBy,   nameof(ImageOrderBy.Random)),
        new(LangId.ImageOrderBy_FileSize,           API.IG_SetLoadingOrderBy,   nameof(ImageOrderBy.FileSize)),
        new(LangId.ImageOrderBy_Extension,          API.IG_SetLoadingOrderBy,   nameof(ImageOrderBy.Extension)),
        new(LangId.ImageOrderBy_DateCreated,        API.IG_SetLoadingOrderBy,   nameof(ImageOrderBy.DateCreated)),
        new(LangId.ImageOrderBy_DateAccessed,       API.IG_SetLoadingOrderBy,   nameof(ImageOrderBy.DateAccessed)),
        new(LangId.ImageOrderBy_DateModified,       API.IG_SetLoadingOrderBy,   nameof(ImageOrderBy.DateModified)),
        new(LangId.ImageOrderBy_ExifDateTaken,      API.IG_SetLoadingOrderBy,   nameof(ImageOrderBy.ExifDateTaken)),
        new(LangId.ImageOrderBy_ExifRating,         API.IG_SetLoadingOrderBy,   nameof(ImageOrderBy.ExifRating)),
        new(LangId.ImageOrderType_Asc,              API.IG_SetLoadingOrderType, nameof(ImageOrderType.Asc)),
        new(LangId.ImageOrderType_Desc,             API.IG_SetLoadingOrderType, nameof(ImageOrderType.Desc)),
        new(LangId.Menu_MnuEdit,                    API.IG_OpenEditingApp,          Key.E),
        new(LangId.Menu_MnuInvertColors,            API.IG_InvertColors,            Hotkey.Ctrl, Key.I),
        new(LangId.Menu_MnuToggleImageAnimation,    API.IG_ToggleImageAnimation,    Hotkey.Ctrl, Key.Space),
        new(LangId.Menu_MnuRotateLeft,              API.IG_Rotate,              nameof(RotateOption.Left),          [new(Hotkey.Ctrl, Key.OemPeriod)]),
        new(LangId.Menu_MnuRotateRight,             API.IG_Rotate,              nameof(RotateOption.Right),         [new(Hotkey.Ctrl, Key.OemQuestion)]),
        new(LangId.Menu_MnuFlipHorizontal,          API.IG_FlipImage,           nameof(FlipOptions.Horizontal),     [new(Hotkey.Ctrl, Key.OemSemicolon)]),
        new(LangId.Menu_MnuFlipVertical,            API.IG_FlipImage,           nameof(FlipOptions.Vertical),       [new(Hotkey.Ctrl, Key.OemQuotes)]),
        new(LangId.Menu_MnuSetDesktopBackground,    API.IG_SetDesktopBackground),
        new(LangId.Menu_MnuSetLockScreen,           API.IG_SetLockScreenImage),

        new(LangId.Menu_MnuPasteImage,          API.IG_PasteImage,          Hotkey.Ctrl, Key.V),
        new(LangId.Menu_MnuCopyImagePixels,     API.IG_CopyImagePixels,     Hotkey.Ctrl | MKeys.Shift, Key.C),
        new(LangId.Menu_MnuCopyFile,            API.IG_CopyFiles,           Hotkey.Ctrl, Key.C),
        .. SupportedOn(BHelper.OS != OSType.Mac, [
            new(LangId.Menu_MnuCutFile,         API.IG_CutFiles,            Hotkey.Ctrl, Key.X),
        ]),
        new(LangId.Menu_MnuCopyPath,            API.IG_CopyImagePath,       Hotkey.Ctrl, Key.L),
        new(LangId.Menu_MnuClearClipboard,      API.IG_ClearClipboard,      Hotkey.Ctrl, Key.OemTilde),

        new(LangId.Menu_MnuWindowFit,           API.IG_ToggleWindowFit,             Key.F9),
        .. SupportedOn(BHelper.OS != OSType.Linux, [
            new(LangId.Menu_MnuFrameless,       API.IG_ToggleFrameless,             Key.F10),
        ]),
        new(LangId.Menu_MnuFullScreen,          API.IG_ToggleFullScreen,            Key.F11),
        new(LangId.Menu_MnuSlideshow,           API.IG_ToggleSlideshow,             Key.F12),
        new(LangId._MnuPauseResumeSlideshow,    API.IG_ToggleSlideshowPlayback,     Key.Space),

        new(LangId.Menu_MnuToggleTopMost,            API.IG_ToggleWindowTopMost),
        new(LangId.Menu_MnuToggleToolbar,            API.IG_ToggleToolbar,           Key.T),
        new(LangId.Menu_MnuToggleGallery,            API.IG_ToggleGallery,           Key.G),
        new(LangId.Menu_MnuToggleCheckerboard,       API.IG_ToggleCheckerboard,      Key.B),
        new(LangId.Menu_MnuChangeBackgroundColor,    API.IG_SetBackgroundColor,      Hotkey.Ctrl, Key.B),

        new(LangId.Menu_MnuColorPicker,             API.IG_ToggleTool, ColorPickerToolControl.TOOL_ID,      [new(Key.K)]),
        new(LangId.Menu_MnuCropTool,                API.IG_ToggleTool, CropImageToolControl.TOOL_ID,        [new(Key.C)]),
        new(LangId.Menu_MnuFrameNav,                API.IG_ToggleTool, FrameNavToolControl.TOOL_ID,         [new(Key.F)]),
        new(LangId.Menu_MnuHdrToneMapper,           API.IG_ToggleTool, HdrToneMapperToolControl.TOOL_ID,    [new(Key.H)]),
        new(LangId.Menu_MnuProjector,               API.IG_ToggleTool, ProjectorToolControl.TOOL_ID,        [new(Key.P)]),
        new(LangId.Menu_MnuResizeTool,              API.IG_OpenTool, ImageResizerTool.TOOL_ID,              [new(Key.R)]),
        new(LangId.Menu_MnuLosslessCompression,     API.IG_OpenTool, LosslessCompressionTool.TOOL_ID,       [new(Key.M)]),
        new(LangId.Menu_MnuToolsSettings,           API.IG_OpenSettings, nameof(SettingsNavId.Tools)),

        new(LangId.Menu_MnuPlugins,                 API.IG_OpenSettings, nameof(SettingsNavId.Plugins)),
        new(LangId.Menu_MnuSettings,                API.IG_OpenSettings,                                    Hotkey.Ctrl, Key.OemComma),

        new(LangId.Menu_MnuAbout,                       API.IG_OpenAboutWindow, Key.F1),
        new(LangId.Menu_MnuQuickSetup,                  API.IG_QuickSetup),
        new(LangId.Menu_MnuSetDefaultPhotoViewer,       API.IG_SetDefaultPhotoViewer),
        new(LangId.Menu_MnuRemoveDefaultPhotoViewer,    API.IG_RemoveDefaultPhotoViewer),

        new(LangId.Menu_MnuExit,                        API.IG_Exit,            [new(Key.Escape), new(Hotkey.Ctrl, Key.W)]),
    ];

    private static HotkeySingleAction[] SupportedOn(bool isSupported, HotkeySingleAction[] actions)
        => isSupported ? actions : [];

    private static Dictionary<LangId, HotkeySingleAction> _menuMap { get; set; }
        = new(DefaultMenuList.Select(ac => new KeyValuePair<LangId, HotkeySingleAction>(Lang.GetKey(ac.LangKey)!.Value, ac)));

    public static Dictionary<string, SingleAction> AppHotkeysMap { get; private set; } = new();

    public void RegisterHotkeys()
    {
        AppHotkeysMap.Clear();

        foreach (var def in DefaultMenuList)
        {
            var key = Lang.GetKey(def.LangKey);
            if (key is not null && _menuMap.TryGetValue(key.Value, out var action))
            {
                action.Hotkeys = def.Hotkeys;
            }
        }

        var mainMenuHotkeys = Core.Config.MenuHotkeys.GetValueOrDefault(LangId.Menu_MnuMain)
            ?? _menuMap.GetValueOrDefault(LangId.Menu_MnuMain)?.Hotkeys
            ?? [];
        var mainMenuHotkeyText = string.Join(", ", mainMenuHotkeys.Select(hk => hk.KeyString));

        MainWindowViewModel.ToolbarVM.ButtonMenuVM = new ToolbarItemModel()
        {
            Image = nameof(IgThemeIcon.MainMenu),
            Text = nameof(LangId.Menu_MnuMain),
            HotkeyText = mainMenuHotkeyText,
        };

        foreach (var item in Core.Config.ToolbarButtons)
        {
            if (item.IsSeparator || item.OnClick is null) continue;

            if (string.IsNullOrWhiteSpace(item.OnClick.LangKey))
            {
                item.OnClick.LangKey = item.Text;
            }

            var hotkeyTextList = item.OnClick.Hotkeys.Select(hk => hk.KeyString) ?? [];
            if (!hotkeyTextList.Any())
            {
                var langKey = Lang.GetKey(item.OnClick.LangKey);
                if (langKey is not null)
                {
                    var menuHotkeys = Core.Config.MenuHotkeys.GetValueOrDefault(langKey.Value) ?? [];
                    if (menuHotkeys.Length == 0)
                    {
                        var menuAction = _menuMap.GetValueOrDefault(langKey.Value);
                        menuHotkeys = menuAction?.Hotkeys ?? [];
                    }

                    hotkeyTextList = menuHotkeys.Select(hk => hk.KeyString) ?? [];
                }
            }
            item.HotkeyText = string.Join(", ", hotkeyTextList);

            foreach (var hk in item.OnClick.Hotkeys)
            {
                _ = AppHotkeysMap.TryAdd(hk.KeyString, item.OnClick);
            }
        }

        foreach (var item in Core.Config.MenuHotkeys)
        {
            if (_menuMap.TryGetValue(item.Key, out var action))
            {
                action.Hotkeys = item.Value;
            }
        }

        foreach (var item in _menuMap)
        {
            foreach (var hk in item.Value.Hotkeys)
            {
                AppHotkeysMap.TryAdd(hk.KeyString, item.Value);
            }
        }

        foreach (var tool in Core.Config.Tools)
        {
            if (tool.Hotkeys.Length == 0) continue;

            var toolAction = new SingleAction(API.IG_OpenTool, tool.ToolId);

            foreach (var hk in tool.Hotkeys)
            {
                if (hk is null) continue;
                AppHotkeysMap.TryAdd(hk.KeyString, toolAction);
            }
        }
    }

    public async Task HandleKeyDownAsync(KeyEventArgs e)
    {
        var hotkey = new Hotkey(e);
        var action = AppHotkeysMap.GetValueOrDefault(hotkey.KeyString);
        if (action is null) return;

        if (FeatureManager.IsLocked(action.Executable))
        {
            e.Handled = true;
            return;
        }

        var isHotkeyPressMultiTimes = hotkey.IsSame(_lastHotkeyPressed);
        var executable = action.Executable ?? string.Empty;

        _lastHotkeyPressed = hotkey;

        e.Handled = action != null;

        if (executable == nameof(API.IG_ZoomIn))
        {
            if (Viewer.ZoomLevels.Length == 0)
            {
                Viewer.StartDrawingAnimation(AnimationSources.ZoomIn);
                return;
            }
        }
        else if (executable == nameof(API.IG_ZoomOut))
        {
            if (Viewer.ZoomLevels.Length == 0)
            {
                Viewer.StartDrawingAnimation(AnimationSources.ZoomOut);
                return;
            }
        }

        if (executable == nameof(API.IG_PanLeft))
        {
            Viewer.StartDrawingAnimation(AnimationSources.PanLeft);
            return;
        }
        if (executable == nameof(API.IG_PanRight))
        {
            Viewer.StartDrawingAnimation(AnimationSources.PanRight);
            return;
        }
        if (executable == nameof(API.IG_PanUp))
        {
            Viewer.StartDrawingAnimation(AnimationSources.PanUp);
            return;
        }
        if (executable == nameof(API.IG_PanDown))
        {
            Viewer.StartDrawingAnimation(AnimationSources.PanDown);
            return;
        }

        var isBrowsingAction = executable.Equals(nameof(API.IG_ViewByStep), StringComparison.Ordinal)
            || executable.Equals(nameof(API.IG_ViewNext), StringComparison.Ordinal)
            || executable.Equals(nameof(API.IG_ViewPrevious), StringComparison.Ordinal);
        _isQuickBrowsingPhotos.Set(isBrowsingAction && isHotkeyPressMultiTimes
            && Core.Config.BrowsingMode == BrowsingMode.Turbo);
        if (_isQuickBrowsingPhotos)
        {
            Viewer.ShouldLoadFullResolution.SetFalse();
        }

        await RunActionAsync(action);
    }

    public async Task HandleKeyUpAsync(KeyEventArgs e)
    {
        _lastHotkeyPressed = null;

        if (Viewer.AnimationSource.HasFlag(AnimationSources.ZoomIn))
        {
            Viewer.StopDrawingAnimation(AnimationSources.ZoomIn);
            return;
        }
        if (Viewer.AnimationSource.HasFlag(AnimationSources.ZoomOut))
        {
            Viewer.StopDrawingAnimation(AnimationSources.ZoomOut);
            return;
        }

        if (Viewer.AnimationSource.HasFlag(AnimationSources.PanLeft))
        {
            Viewer.StopDrawingAnimation(AnimationSources.PanLeft);
            return;
        }
        if (Viewer.AnimationSource.HasFlag(AnimationSources.PanRight))
        {
            Viewer.StopDrawingAnimation(AnimationSources.PanRight);
            return;
        }
        if (Viewer.AnimationSource.HasFlag(AnimationSources.PanUp))
        {
            Viewer.StopDrawingAnimation(AnimationSources.PanUp);
            return;
        }
        if (Viewer.AnimationSource.HasFlag(AnimationSources.PanDown))
        {
            Viewer.StopDrawingAnimation(AnimationSources.PanDown);
            return;
        }

        if (_isQuickBrowsingPhotos)
        {
            _isQuickBrowsingPhotos.SetFalse();
            Viewer.Photo?.CancelLoading();
            Viewer.ShouldLoadFullResolution.SetTrue();

            await Task.Delay(50);
            await Viewer.LoadPhotoAsync(false, true);

            Core.Photos.RequestCacheAround(Core.Photos.CurrentIndex);
        }
    }

    public static HotkeySingleAction? GetMenuAction(LangId? langKey)
    {
        if (langKey is null) return null;

        var action = _menuMap.GetValueOrDefault(langKey.Value);

        return action;
    }

    public static string GetMenuHotkeyText(LangId? langKey)
    {
        var action = GetMenuAction(langKey);
        if (action is null) return string.Empty;

        var hotkeyText = String.Join(", ", action.Hotkeys);
        return hotkeyText;
    }

    public static string GetMenuTooltipText(LangId? langKey)
        => GetMenuTooltipText(langKey, langKey);

    public static string GetMenuTooltipText(LangId? textKey, LangId? hotkeyKey)
    {
        var text = Core.Lang[textKey];
        var hotkeyText = GetMenuHotkeyText(hotkeyKey);

        return string.IsNullOrEmpty(hotkeyText) ? text : $"{text} ({hotkeyText})";
    }

}
