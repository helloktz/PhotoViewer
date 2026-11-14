using PhotoViewer.Common.Types;
using PhotoViewer.Common.Types.JsonTypeConverters;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Localization;

[JsonSerializable(typeof(Lang))]
public partial class LangJsonContext : JsonSerializerContext { }

public class Lang
{

    public static float SPEC_VERSION => 10;

    [JsonPropertyName("_Metadata")]
    public LangMetadata Metadata { get; set; } = new();

    [JsonConverter(typeof(JsonLangItemsConverter))]
    public IDictionary<LangId, string> Items { get; set; } = FrozenDictionary<LangId, string>.Empty;

    [JsonIgnore]
    public string FilePath { get; private set; } = "English";

    [JsonIgnore]
    public string DisplayFilePath => BHelper.GetRealPlatformPath(FilePath);

    [JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);

    [JsonIgnore]
    public bool IsBuiltIn => !Path.IsPathRooted(FilePath);

    [JsonIgnore]
    public string this[string? key, params object?[] args] => Get(key, args);

    [JsonIgnore]
    public string this[LangId? key, params object?[] args] => Get(key, args);

    public Lang() { }

    public Lang(string filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            FilePath = filePath;
        }
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(FilePath)) return;

        var jsonOptions = BHelper.CreateJsonOptions();
        var jsonContext = new LangJsonContext(jsonOptions);

        try
        {
            var lang = await BHelper.ReadJsonFromFileAsync(FilePath, jsonContext.Lang);
            if (lang == null) return;

            Metadata = lang.Metadata;
            Items = lang.Items.ToFrozenDictionary();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lang.LoadAsync] failed for '{FilePath}': {ex.Message}");
        }
    }

    public async Task SaveAsFileAsync(string filePath)
    {
        var lang = new Lang()
        {
            Metadata = Metadata,
            Items = Items,
        };

        if (Metadata.EnglishName.Equals("English", StringComparison.OrdinalIgnoreCase))
        {
            lang.Metadata.EnglishName = "<Your_language_name_in_English>";
            lang.Metadata.LocalName = "<Local_name_of_your_language>";
        }

        var jsonOptions = BHelper.CreateJsonOptions();
        var jsonContext = new LangJsonContext(jsonOptions);

        await BHelper.WriteJsonToFileAsync(filePath, lang, jsonContext.Lang);
    }

    public static LangId? GetKey(string? key)
    {
        if (Enum.TryParse<LangId>(key, out var langKey))
        {
            return langKey;
        }

        return null;
    }

    public string Get(string? key, params object?[] args)
    {
        if (GetKey(key) is LangId langKey)
        {
            return Get(langKey, args);
        }

        return key ?? string.Empty;
    }

    public string Get(LangId? key, params object?[] args)
    {
        if (key is null) return string.Empty;
        string? value = null;

        if (Items.TryGetValue(key.Value, out value))
        {
        }

        else if (DefaultLangMap.TryGetValue(key.Value, out value))
        {
        }
        else
        {
            return string.Empty;
        }

        if (args.Length > 0)
        {
            return string.Format(value, args);
        }

        return value;
    }

    public string Get(string? key) => Get(key, []);

    public string Get(LangId? key) => Get(key, []);

    public static string ResolveFilePath(string language)
    {
        if (string.Equals(language, "zh-CN.iglang.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "Chinese Simplified.iglang.json", StringComparison.OrdinalIgnoreCase))
        {
            return BHelper.BaseDir(Dir.Language, "zh-CN.iglang.json");
        }

        return string.Empty;
    }

    public static async Task<List<Lang>> LoadAllLanguagePacksAsync()
    {
        var path = ResolveFilePath("zh-CN");
        if (GetPackMinVersion(path) < SPEC_VERSION) return [];

        var lang = new Lang(path);
        await lang.LoadAsync().ConfigureAwait(false);
        return string.Equals(lang.Metadata.Code, "zh-CN", StringComparison.OrdinalIgnoreCase) ? [lang] : [];
    }

    public static bool IsPackFileCompatible(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return true;
        return GetPackMinVersion(filePath) >= SPEC_VERSION;
    }

    private static float GetPackMinVersion(string filePath)
    {
        try
        {
            using var doc = BHelper.ReadJsonDocFromFile(filePath);
            if (doc?.RootElement is not { ValueKind: JsonValueKind.Object } root
                || !root.TryGetProperty("_Metadata", out var meta)
                || !meta.TryGetProperty(nameof(LangMetadata.MinVersion), out var ver))
                return 0;

            if (ver.ValueKind == JsonValueKind.Number && ver.TryGetSingle(out var num)) return num;
            if (ver.ValueKind == JsonValueKind.String
                && float.TryParse(ver.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var str))
                return str;
        }
        catch { }

        return 0;
    }

    private static string GetPackFileName(string filePath)
    {
        var name = Path.GetFileName(filePath);
        const string suffix = ".iglang.json";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
        return name;
    }

    public static FrozenDictionary<LangId, string> KeysMap { get; }

    public static FrozenDictionary<LangId, string> DefaultLangMap { get; }

    static Lang()
    {
        var keys = new Dictionary<LangId, string>();
        foreach (var name in Enum.GetNames<LangId>())
        {
            keys[Enum.Parse<LangId>(name)] = name;
        }
        KeysMap = keys.ToFrozenDictionary();

        DefaultLangMap = new Dictionary<LangId, string>(_defaultLangList).ToFrozenDictionary();
    }

    private static IReadOnlyCollection<KeyValuePair<LangId, string>> _defaultLangList = [

        new(LangId._OK, "OK"),
        new(LangId._Cancel, "Cancel"),
        new(LangId._Apply, "Apply"),
        new(LangId._Close, "Close"),
        new(LangId._Yes, "Yes"),
        new(LangId._No, "No"),
        new(LangId._LearnMore, "Learn more…"),
        new(LangId._Continue, "Continue"),
        new(LangId._Quit, "Quit"),
        new(LangId._Back, "Back"),
        new(LangId._Next, "Next"),
        new(LangId._Save, "Save"),
        new(LangId._Warning, "Warning"),
        new(LangId._Copy, "Copy"),
        new(LangId._Browse, "Browse…"),
        new(LangId._Reset, "Reset"),
        new(LangId._ResetToDefault, "Reset to default"),
        new(LangId._Default, "Default"),
        new(LangId._BackgroundColor, "Background color"),
        new(LangId._Update, "Update"),
        new(LangId._Download, "Download"),
        new(LangId._RestartNow, "Restart now"),
        new(LangId._Website, "Website"),
        new(LangId._TypeToFilter, "Type to filter…"),
        new(LangId._Delete, "Delete"),
        new(LangId._Add, "Add"),
        new(LangId._Edit, "Edit"),
        new(LangId._ID, "ID"),
        new(LangId._Name, "Name"),
        new(LangId._Hotkeys, "Hotkeys"),
        new(LangId._Executable, "Executable"),
        new(LangId._Argument, "Argument"),
        new(LangId._CommandPreview, "Command preview"),
        new(LangId._FileExtension, "File extension"),
        new(LangId._Codec, "Codec"),
        new(LangId._Decoder, "Decoder"),
        new(LangId._Encoder, "Encoder"),
        new(LangId._Empty, "(empty)"),
        new(LangId._Separator, "Separator"),
        new(LangId._Icon, "Icon"),
        new(LangId._Description, "Description"),
        new(LangId._Type, "Type"),
        new(LangId._Version, "Version"),
        new(LangId._View, "View"),
        new(LangId._Start, "Start"),
        new(LangId._Register, "Register"),
        new(LangId._Unregister, "Unregister"),
        new(LangId._ClearCache, "Clear cache"),
        new(LangId._ClearCache_Success, "Cache are cleared"),
        new(LangId._ClearCache_Error, "Could not clear the cache"),

        new(LangId._UnhandledException, "Unhandled exception"),
        new(LangId._UnhandledException_Description, "Unhandled exception has occurred. If you click Continue, the application will ignore this error and attempt to continue. If you click Quit, the application will close immediately."),
        new(LangId._DoNotShowThisMessageAgain, "Do not show this message again"),
        new(LangId._IncompatibleConfig, "Incompatible settings file"),
        new(LangId._IncompatibleConfig_Description, "Your settings file was created by an older version of Photo Viewer and is not compatible with this version. It will be reset to the default settings.\r\n\r\nDo you want to continue?"),
        new(LangId._IncompatibleConfig_BackupNote, "Please manually back up your settings file before continuing if you want to keep it. Choosing No will quit Photo Viewer without changing the file."),
        new(LangId._IncompatibleTheme, "Incompatible theme packs"),
        new(LangId._IncompatibleTheme_Description, "Your theme packs were created for the previous version of Photo Viewer and are not compatible with this version."),
        new(LangId._IncompatibleLanguage, "Incompatible language packs"),
        new(LangId._IncompatibleLanguage_Description, "Your language packs were created for the previous version of Photo Viewer and are not compatible with this version."),
        new(LangId._CreatingFile, "Creating a temporary image file…"),
        new(LangId._CreatingFileError, "Could not create temporary image file"),
        new(LangId._NotSupported, "Unsupported format"),

        new(LangId._InvalidAction, "Invalid action"),
        new(LangId._InvalidAction_Transformation, "Photo Viewer does not support rotation, flipping for this image."),

        new(LangId._UserAction_Win32ExeError, "Cannot execute command '{0}'. Make sure the name is correct."),

        new(LangId._Metadata_FileSize, "File size"),
        new(LangId._Metadata_FileLastWriteTime, "Date modified"),
        new(LangId._Metadata_FrameCount, "Frames"),
        new(LangId._Metadata_ExifRatingPercent, "Rating"),
        new(LangId._Metadata_ColorSpace, "Color space"),
        new(LangId._Metadata_ColorProfile, "Color profile"),
        new(LangId._Metadata_ExifDateTime, "EXIF: DateTime"),
        new(LangId._Metadata_ExifDateTimeOriginal, "EXIF: DateTimeOriginal"),

        new(LangId._ImageInfo_ListCount, "{0} file(s)"),
        new(LangId._ImageInfo_FrameCount, "{0} frame(s)"),

        new(LangId._Position_Left, "Left"),
        new(LangId._Position_Right, "Right"),
        new(LangId._Position_Top, "Top"),
        new(LangId._Position_Bottom, "Bottom"),

        new(LangId._Validation_Required, "Required"),
        new(LangId._Validation_RegexPattern, "Invalid value"),
        new(LangId._Validation_IntValueOnly, "Must be an integer"),
        new(LangId._Validation_UnsignedIntValueOnly, "Must be a non-negative integer"),
        new(LangId._Validation_FloatValueOnly, "Must be a number"),
        new(LangId._Validation_UnsignedFloatValueOnly, "Must be a non-negative number"),
        new(LangId._Validation_FileNameValueOnly, "Invalid filename"),
        new(LangId._Validation_FilePathValueOnly, "Invalid file path"),
        new(LangId._Validation_FileExtensionValueOnly, "Must be a single file extension (e.g. .psd)"),
        new(LangId._Validation_FileExtensionsValueOnly, "Must be file extensions separated by ';' (e.g. .jpg;.png)"),

        new(LangId._PrintOrientation_Auto, "Auto" ),
        new(LangId._PrintOrientation_Portrait, "Portrait" ),
        new(LangId._PrintOrientation_Landscape, "Landscape" ),
        new(LangId._PrintFitMode_Fit, "Fit" ),
        new(LangId._PrintFitMode_Fill, "Fill" ),
        new(LangId._PrintFitMode_ActualSize, "Actual size" ),
        new(LangId._PrintMarginPreset_None, "None" ),
        new(LangId._PrintMarginPreset_Narrow, "Narrow" ),
        new(LangId._PrintMarginPreset_Normal, "Normal" ),
        new(LangId._PrintMarginPreset_Wide, "Wide" ),
        new(LangId._PrintColorMode_Color, "Color" ),
        new(LangId._PrintColorMode_Grayscale, "Grayscale" ),
        new(LangId._PrintDuplex_None, "Off" ),
        new(LangId._PrintDuplex_LongEdge, "Flip on long edge" ),
        new(LangId._PrintDuplex_ShortEdge, "Flip on short edge" ),
        new(LangId._PrintPageScope_Current, "Current" ),
        new(LangId._PrintPageScope_All, "All ({0})" ),
        new(LangId._PrintPageScope_Range, "Range" ),
        new(LangId._PrintRegion_WholeImage, "Whole image" ),
        new(LangId._PrintRegion_Selection, "Selection" ),
        new(LangId._PrintRegion_VisibleArea, "Visible area" ),
        new(LangId._PrinterState_Ready, "Ready" ),
        new(LangId._PrinterState_Busy, "Busy" ),
        new(LangId._PrinterState_Offline, "Offline" ),
        new(LangId._PrinterState_Paused, "Paused" ),
        new(LangId._PrinterState_Error, "Error" ),

        new(LangId._PicMain_ErrorText, "Could not load this image"),
        new(LangId._OpenFileDialog, "All supported files"),
        new(LangId._Loading, "Loading…"),
        new(LangId._ReachedFirstImage, "Reached the first image"),
        new(LangId._ReachedLastImage, "Reached the last image"),
        new(LangId._SwitchedToNextFolder, "Switched to next folder\r\n{0}"),
        new(LangId._SwitchedToPreviousFolder, "Switched to previous folder\r\n{0}"),
        new(LangId._ClipboardImage, "Clipboard image"),
        new(LangId._PlayMotionVideo, "Play motion video"),

        new(LangId._Slogan, "A Fast, Seamless Photo Viewer"),
        new(LangId._AboutVersion, "Version:"),
        new(LangId._License, "Software license"),
        new(LangId._Credits, "Credits"),

        new(LangId._PauseSlideshow, "Slideshow is paused." ),
        new(LangId._ResumeSlideshow, "Slideshow is resumed." ),
        new(LangId._MnuPauseResumeSlideshow, "Pause/resume slideshow" ),
        new(LangId._MnuToggleCountdown, "Show slideshow countdown" ),
        new(LangId._MnuExitSlideshow, "Exit slideshow" ),

        new(LangId._Title, "Export image frames" ),
        new(LangId._FolderPickerTitle, "Select output folder for exporting image frames" ),
        new(LangId._Exporting, "Exporting {0}/{1} frames \r\n{2}…" ),
        new(LangId._ExportDone, "Exported {0} frames successfully to \r\n{1}" ),
        new(LangId._OpenOutputFolder, "Open output folder" ),

        new(LangId.ImageOrderBy_Name, "Name (default)"),
        new(LangId.ImageOrderBy_Random, "Random"),
        new(LangId.ImageOrderBy_FileSize, "File size"),
        new(LangId.ImageOrderBy_Extension, "Extension"),
        new(LangId.ImageOrderBy_DateCreated, "Date created"),
        new(LangId.ImageOrderBy_DateAccessed, "Date accessed"),
        new(LangId.ImageOrderBy_DateModified, "Date modified"),
        new(LangId.ImageOrderBy_ExifDateTaken, "EXIF: Date taken"),
        new(LangId.ImageOrderBy_ExifRating, "EXIF: Rating"),

        new(LangId.ImageOrderType_Asc, "Ascending"),
        new(LangId.ImageOrderType_Desc, "Descending"),

        new(LangId.BrowsingMode_Turbo, "Turbo"),
        new(LangId.BrowsingMode_Turbo_Description, "Fastest. Images may be skipped while you browse quickly."),
        new(LangId.BrowsingMode_Sequential, "Sequential"),
        new(LangId.BrowsingMode_Sequential_Description, "Never skips. Each image is fully shown before the next."),

        new(LangId.TransitionEffect_None, "None"),
        new(LangId.TransitionEffect_Random, "Random"),

        new(LangId.AfterEditAppAction_Nothing, "Nothing"),
        new(LangId.AfterEditAppAction_Minimize, "Minimize"),
        new(LangId.AfterEditAppAction_Close, "Close"),

        new(LangId.ColorProfileOption_None, "None"),
        new(LangId.ColorProfileOption_CurrentMonitorProfile, "Current monitor profile"),
        new(LangId.ColorProfileOption_Custom, "Custom…"),

        new(LangId.BackdropStyle_None, "None"),

        new(LangId.MouseWheelEvent_Scroll, "Scroll"),
        new(LangId.MouseWheelEvent_CtrlAndScroll, "Hold Ctrl and scroll"),
        new(LangId.MouseWheelEvent_ShiftAndScroll, "Hold Shift and scroll"),
        new(LangId.MouseWheelEvent_AltAndScroll, "Hold Alt and scroll"),

        new(LangId.MouseWheelAction_DoNothing, "Do nothing"),
        new(LangId.MouseWheelAction_Zoom, "Zoom in / out"),
        new(LangId.MouseWheelAction_PanVertically, "Pan up / down"),
        new(LangId.MouseWheelAction_PanHorizontally, "Pan left / right"),
        new(LangId.MouseWheelAction_BrowseImages, "View next / previous Image"),

        new(LangId.MouseClickEvent_LeftClick, "Left click"),
        new(LangId.MouseClickEvent_LeftDoubleClick, "Left double-click"),
        new(LangId.MouseClickEvent_RightClick, "Right click"),
        new(LangId.MouseClickEvent_WheelClick, "Wheel click"),
        new(LangId.MouseClickEvent_XButton1Click, "Back button (XButton1) click"),
        new(LangId.MouseClickEvent_XButton2Click, "Forward button (XButton2) click"),

        new(LangId.CheckerboardType_None, "None"),
        new(LangId.CheckerboardType_Client, "Entire viewer"),
        new(LangId.CheckerboardType_Image, "Image region only"),

        new(LangId.ScrollbarMode_Never, "Never"),
        new(LangId.ScrollbarMode_AutoHide, "Auto-hide"),
        new(LangId.ScrollbarMode_Always, "Always"),

        new(LangId.ImageInfoOverlayMode_Hidden, "Hidden"),
        new(LangId.ImageInfoOverlayMode_Always, "Always visible"),
        new(LangId.ImageInfoOverlayMode_AlwaysAutoHide, "Auto-hide"),
        new(LangId.ImageInfoOverlayMode_Borderless, "Borderless window only"),
        new(LangId.ImageInfoOverlayMode_BorderlessAutoHide, "Borderless window only, auto-hide"),

        new(LangId.Menu_MnuMain, "Main menu"),
        new(LangId.Menu_MnuToolbarOverflow, "View more buttons"),

        new(LangId.Menu_MnuFile, "File"),
        new(LangId.Menu_MnuOpenFile, "Open file…"),
        new(LangId.Menu_MnuNewWindow, "Open new window"),
        new(LangId.Menu_MnuNewWindow_Error, "Cannot open new window because only one instance is allowed"),
        new(LangId.Menu_MnuSave, "Save"),
        new(LangId.Menu_MnuSave_Confirm, "Are you sure you want to override this image?"),
        new(LangId.Menu_MnuSave_ConfirmDescription, "Photo Viewer is not a professional photo editor, please be aware of losing quality, metadata, layers,… when saving your image."),
        new(LangId.Menu_MnuSave_Saving, "Saving image…"),
        new(LangId.Menu_MnuSave_Success, "Image is saved"),
        new(LangId.Menu_MnuSave_Error, "Could not save image"),
        new(LangId.Menu_MnuSaveAs, "Save as…"),
        new(LangId.Menu_MnuExportFrames, "Export image frames…"),

        new(LangId.Menu_MnuOpenWith, "Open with…"),
        new(LangId.Menu_MnuEdit, "Edit image {0}…"),
        new(LangId.Menu_MnuEdit_AppNotFound, "Could not find the associated app for editing. You can assign an app for editing this format in Photo Viewer Settings > Edit."),
        new(LangId.Menu_MnuPrint, "Print…"),
        new(LangId.Menu_MnuPrint_Error, "Could not print image"),
        new(LangId.Menu_MnuOpenLocation, "Open image location"),

        new(LangId.Menu_MnuRename, "Rename image…"),
        new(LangId.Menu_MnuRename_Description, "Enter a new filename:"),
        new(LangId.Menu_MnuMoveToRecycleBin, "Move to Recycle Bin"),
        new(LangId.Menu_MnuMoveToRecycleBin_Description, "Do you want to move this file to Recycle bin?"),
        new(LangId.Menu_MnuDeleteFromHardDisk, "Delete permanently"),
        new(LangId.Menu_MnuDeleteFromHardDisk_Description, "Are you sure you want to permanently delete this file?"),

        new(LangId.Menu_MnuNavigation, "Navigation"),
        new(LangId.Menu_MnuViewNext, "View next image"),
        new(LangId.Menu_MnuViewPrevious, "View previous image"),

        new(LangId.Menu_MnuGoTo, "Go to…"),
        new(LangId.Menu_MnuGoTo_Description, "Type image number to view, and then press ENTER"),
        new(LangId.Menu_MnuGoToFirst, "Go to first image"),
        new(LangId.Menu_MnuGoToLast, "Go to last image"),

        new(LangId.Menu_MnuViewNextFrame, "View next frame"),
        new(LangId.Menu_MnuViewPreviousFrame, "View previous frame"),
        new(LangId.Menu_MnuViewFirstFrame, "View first frame"),
        new(LangId.Menu_MnuViewLastFrame, "View last frame"),

        new(LangId.Menu_MnuZoom, "Zoom"),
        new(LangId.Menu_MnuZoomIn, "Zoom in"),
        new(LangId.Menu_MnuZoomOut, "Zoom out"),
        new(LangId.Menu_MnuCustomZoom, "Custom zoom…"),
        new(LangId.Menu_MnuCustomZoom_Description, "Enter a new zoom value"),
        new(LangId.Menu_MnuScaleToFit, "Scale to fit"),
        new(LangId.Menu_MnuScaleToFill, "Scale to fill"),
        new(LangId.Menu_MnuActualSize, "Actual size"),
        new(LangId.Menu_MnuLockZoom, "Lock zoom ratio"),
        new(LangId.Menu_MnuAutoZoom, "Auto zoom"),
        new(LangId.Menu_MnuScaleToWidth, "Scale to width"),
        new(LangId.Menu_MnuScaleToHeight, "Scale to height"),

        new(LangId.Menu_MnuPanning, "Panning"),

        new(LangId.Menu_MnuPanLeft, "Pan image left"),
        new(LangId.Menu_MnuPanRight, "Pan image right"),
        new(LangId.Menu_MnuPanUp, "Pan image up"),
        new(LangId.Menu_MnuPanDown, "Pan image down"),

        new(LangId.Menu_MnuPanToLeftSide, "Pan image to left edge"),
        new(LangId.Menu_MnuPanToRightSide, "Pan image to right edge"),
        new(LangId.Menu_MnuPanToTop, "Pan image to top"),
        new(LangId.Menu_MnuPanToBottom, "Pan image to bottom"),

        new(LangId.Menu_MnuImage, "Image"),

        new(LangId.Menu_MnuRefresh, "Refresh"),
        new(LangId.Menu_MnuReload, "Reload image"),
        new(LangId.Menu_MnuReloadImageList, "Reload image list"),
        new(LangId.Menu_MnuUnload, "Unload image"),

        new(LangId.Menu_MnuViewChannels, "View channels"),
        new(LangId.Menu_MnuLoadingOrders, "Loading orders"),
        new(LangId.Menu_MnuInvertColors, "Invert colors"),
        new(LangId.Menu_MnuToggleImageAnimation, "Start / stop animating image"),

        new(LangId.Menu_MnuRotateLeft, "Rotate left"),
        new(LangId.Menu_MnuRotateRight, "Rotate right"),
        new(LangId.Menu_MnuFlipHorizontal, "Flip Horizontal"),
        new(LangId.Menu_MnuFlipVertical, "Flip Vertical"),

        new(LangId.Menu_MnuSetDesktopBackground, "Set as Desktop background"),
        new(LangId.Menu_MnuSetDesktopBackground_Error, "Could not set image as desktop background"),
        new(LangId.Menu_MnuSetDesktopBackground_Success, "Desktop background is updated"),
        new(LangId.Menu_MnuSetLockScreen, "Set as Lock screen image"),
        new(LangId.Menu_MnuSetLockScreen_Error, "Could not set image as lock screen image"),
        new(LangId.Menu_MnuSetLockScreen_Success, "Lock screen image is updated"),

        new(LangId.Menu_MnuImageProperties, "Image properties"),

        new(LangId.Menu_MnuClipboard, "Clipboard"),
        new(LangId.Menu_MnuCopyFile, "Copy file"),
        new(LangId.Menu_MnuCopyFile_Success, "Copied {0} file(s)"),
        new(LangId.Menu_MnuCopyImagePixels, "Copy image pixels"),
        new(LangId.Menu_MnuCopyImagePixels_Copying, "Copying image pixels. It's going to take a while…"),
        new(LangId.Menu_MnuCopyImagePixels_Success, "Copied image pixels"),
        new(LangId.Menu_MnuCutFile, "Cut file"),
        new(LangId.Menu_MnuCutFile_Success, "Cut {0} file(s)"),
        new(LangId.Menu_MnuCopyPath, "Copy image path"),
        new(LangId.Menu_MnuCopyPath_Success, "Copied image path"),
        new(LangId.Menu_MnuPasteImage, "Paste image"),
        new(LangId.Menu_MnuClearClipboard, "Clear clipboard"),
        new(LangId.Menu_MnuClearClipboard_Success, "Cleared clipboard"),
        new(LangId.Menu_MnuWindowFit, "Window Fit"),
        new(LangId.Menu_MnuFullScreen, "Full Screen"),
        new(LangId.Menu_MnuFrameless, "Frameless"),
        new(LangId.Menu_MnuFrameless_EnableDescription, "Drag the top area to move the window"),
        new(LangId.Menu_MnuSlideshow, "Slideshow"),

        new(LangId.Menu_MnuLayout, "Layout"),
        new(LangId.Menu_MnuToggleToolbar, "Toolbar"),
        new(LangId.Menu_MnuToggleGallery, "Gallery panel"),
        new(LangId.Menu_MnuToggleCheckerboard, "Checkerboard background"),
        new(LangId.Menu_MnuToggleTopMost, "Keep window always on top"),
        new(LangId.Menu_MnuToggleTopMost_Enable, "Enabled window always on top"),
        new(LangId.Menu_MnuToggleTopMost_Disable, "Disabled window always on top"),
        new(LangId.Menu_MnuChangeBackgroundColor, "Change background color…"),

        new(LangId.Menu_MnuTools, "Tools"),
        new(LangId.Menu_MnuColorPicker, "Color picker"),
        new(LangId.Menu_MnuCropTool, "Crop image"),
        new(LangId.Menu_MnuResizeTool, "Resize image"),
        new(LangId.Menu_MnuFrameNav, "Frame navigation"),
        new(LangId.Menu_MnuHdrToneMapper, "HDR tone mapper"),
        new(LangId.Menu_MnuProjector, "Projector"),
        new(LangId.Menu_MnuToolsSettings, "Tools settings…"),

        new(LangId.Menu_MnuLosslessCompression, "Magick.NET Lossless Compression"),
        new(LangId.Menu_MnuLosslessCompression_Confirm, "Are you sure you want to proceed?"),
        new(LangId.Menu_MnuLosslessCompression_Description, "This tool uses Magick.NET library for lossless compression, optimizing file size. Overwrites only if the compressed file is smaller than the original."),
        new(LangId.Menu_MnuLosslessCompression_Compressing, "Performing lossless compression…"),
        new(LangId.Menu_MnuLosslessCompression_Done, "Done lossless compression."),
        new(LangId.Menu_MnuLosslessCompression_Error, "Could not compress the file."),
        new(LangId.Menu_MnuPlugins, "Plugins"),
        new(LangId.Menu_MnuSettings, "Settings"),

        new(LangId.Menu_MnuHelp, "Help"),
        new(LangId.Menu_MnuAbout, "About"),

        new(LangId.Menu_MnuQuickSetup, "Open Photo Viewer Quick Setup"),

        new(LangId.Menu_MnuSetDefaultPhotoViewer, "Set default photo viewer"),
        new(LangId.Menu_MnuSetDefaultPhotoViewer_Success, "You have successfully set Photo Viewer as default photo viewer."),
        new(LangId.Menu_MnuSetDefaultPhotoViewer_Error, "Could not set Photo Viewer as default photo viewer."),

        new(LangId.Menu_MnuRemoveDefaultPhotoViewer, "Remove default photo viewer"),
        new(LangId.Menu_MnuRemoveDefaultPhotoViewer_Success, "Photo Viewer is no longer the default photo viewer."),
        new(LangId.Menu_MnuRemoveDefaultPhotoViewer_Error, "Could not remove Photo Viewer as the default photo viewer."),
        new(LangId.Menu_MnuExit, "Exit"),

        new(LangId.Settings_ResetSettings, "Reset settings"),
        new(LangId.Settings_UnmanagedSettingReminder, "This is an unmanaged setting, it writes registry entries or files that are not cleaned up automatically. You will need to manually disable it before you uninstall the app."),
        new(LangId.Settings_SearchPlaceholder, "Search settings…"),
        new(LangId.Settings_AdminLockWarning, "Some settings are managed by your administrator and can't be changed here."),

        new(LangId.Settings_Nav_General, "General"),
        new(LangId.Settings_Nav_Image, "Image"),
        new(LangId.Settings_Nav_Slideshow, "Slideshow"),
        new(LangId.Settings_Nav_Edit, "Edit"),
        new(LangId.Settings_Nav_Viewer, "Viewer"),
        new(LangId.Settings_Nav_Toolbar, "Toolbar"),
        new(LangId.Settings_Nav_Gallery, "Gallery"),
        new(LangId.Settings_Nav_Layout, "Layout"),
        new(LangId.Settings_Nav_Mouse, "Mouse"),
        new(LangId.Settings_Nav_Keyboard, "Keyboard"),
        new(LangId.Settings_Nav_FileTypeAssociations, "File type associations"),
        new(LangId.Settings_Nav_Tools, "Tools"),
        new(LangId.Settings_Nav_Plugins, "Plugins"),
        new(LangId.Settings_Nav_Language, "Language"),
        new(LangId.Settings_Nav_Appearance, "Appearance"),

        new(LangId.Settings_StartupDir, "Startup location"),
        new(LangId.Settings_ConfigDir, "Configuration location"),
        new(LangId.Settings_UserConfigFile, "User settings file (igconfig.json)"),

        new(LangId.Settings_Startup, "Startup"),
        new(LangId.Settings_EnableWelcomeImage, "Show welcome image"),
        new(LangId.Settings_EnableLastSeenImage, "Open the last seen image"),

        new(LangId.Settings_EnableFileWatcher, "Monitor file changes in the viewing folder and update in realtime"),
        new(LangId.Settings_EnableAutoOpenNewAddedImage, "Open the new added image automatically"),

        new(LangId.Settings_Others, "Others"),
        new(LangId.Settings_EnableMultiInstances, "Allow multiple instances of the program"),
        new(LangId.Settings_ShowAppIcon, "Show app icon on the title bar"),
        new(LangId.Settings_InAppMessageDuration, "In-app message duration (milliseconds)"),
        new(LangId.Settings_ImageInfoTags, "Image information tags"),
        new(LangId.Settings_AvailableImageInfoTags, "Available tags:"),

        new(LangId.Settings_Browsing, "Browsing"),
        new(LangId.Settings_ImageLoadingOrder, "Image loading order"),
        new(LangId.Settings_EnableExplorerSortOrder, "Use Explorer sort order"),
        new(LangId.Settings_EnableSubfoldersLoading, "Load images in subfolders"),
        new(LangId.Settings_EnableImageFolderGrouping, "Group images by directory"),
        new(LangId.Settings_EnableHiddenImagesLoading, "Load hidden images"),
        new(LangId.Settings_EnableLoopBackNavigation, "Loop back to the first image when reaching the end of the image list"),
        new(LangId.Settings_EnableAutoSwitchSiblingDir, "Switch to the sibling folder at the start/end of the list"),
        new(LangId.Settings_BrowsingMode, "Browsing mode"),
        new(LangId.Settings_EnableImagePreview, "Display image preview while it's being loaded"),

        new(LangId.Settings_ImagePreview, "Image preview"),
        new(LangId.Settings_EnableOnlyLoadRawPreview, "Load only the embedded thumbnail for RAW formats"),
        new(LangId.Settings_EnableOnlyLoadNonRawPreview, "Load only the embedded thumbnail for other formats"),
        new(LangId.Settings_MinEmbeddedThumbnailSize, "Minimum size of the embedded thumbnail to be loaded"),
        new(LangId.Settings_MinEmbeddedThumbnailSize_Width, "Width"),
        new(LangId.Settings_MinEmbeddedThumbnailSize_Height, "Height"),

        new(LangId.Settings_FileWatcher, "File watcher"),

        new(LangId.Settings_Caching, "Caching"),
        new(LangId.Settings_ImageBoosterCacheMaxFiles, "Maximum number of images to be cached: {0}"),
        new(LangId.Settings_ImageBoosterCacheMaxMemoryInMb, "Maximum memory used for caching images (in megabytes)"),
        new(LangId.Settings_ImageBoosterCacheMaxDimension, "Maximum image dimension to be cached (in pixels)"),
        new(LangId.Settings_ImageBoosterCacheMaxFileSizeInMb, "Maximum image file size to be cached (in megabytes)"),

        new(LangId.Settings_ColorManagement, "Color management"),
        new(LangId.Settings_EnableHdrToneMapping, "Apply HDR tone mapping to HDR images"),
        new(LangId.Settings_EnableAlwaysApplyColorProfile, "Always apply for image without embedded color profile"),
        new(LangId.Settings_ColorProfile, "Color profile"),
        new(LangId.Settings_CurrentMonitorProfile_Description, "Photo Viewer does not auto-update the color when moving its window between monitors"),

        new(LangId.Settings_Slideshow_Appearance, "Appearance"),
        new(LangId.Settings_EnableSlideshowCountdown, "Show slideshow countdown"),
        new(LangId.Settings_EnableFullscreenSlideshow, "Start slideshow in Full Screen mode"),

        new(LangId.Settings_Slideshow_Playback, "Playback"),
        new(LangId.Settings_EnableLoopSlideshow, "Loop back to the first image when reaching the end of the slideshow"),
        new(LangId.Settings_EnableSlideshowRandomInterval, "Use random interval"),
        new(LangId.Settings_SlideshowInterval, "Slideshow interval:"),
        new(LangId.Settings_SlideshowInterval_From, "From"),
        new(LangId.Settings_SlideshowInterval_To, "To"),

        new(LangId.Settings_SlideshowImagesToNotifySound, "Number of images to trigger a notification sound"),

        new(LangId.Settings_TransitionEffect, "Transition effect"),
        new(LangId.Settings_TransitionDuration, "Duration (milliseconds)"),
        new(LangId.Settings_TransitionCompileError, "This effect cannot be used:"),
        new(LangId.Settings_OpenTransitionsFolder, "Open transition effects folder"),

        new(LangId.Settings_Edit_Saving, "Saving"),
        new(LangId.Settings_EnableDeleteConfirmation, "Show confirmation dialog when deleting file"),
        new(LangId.Settings_EnableSaveConfirmation, "Show confirmation dialog when overriding file"),
        new(LangId.Settings_EnablePreserveModifiedDate, "Preserve the image's modified date on save"),
        new(LangId.Settings_EnableOpenSaveAsInCurrentFolder, "Open the Save As dialog in the current image directory"),
        new(LangId.Settings_ImageEditQuality, "Image quality: {0}"),

        new(LangId.Settings_Clipboard, "Clipboard"),
        new(LangId.Settings_EnableCopyMultipleFiles, "Enable the copying of multiple files at once"),
        new(LangId.Settings_EnableCutMultipleFiles, "Enable the cutting of multiple files at once"),

        new(LangId.Settings_AfterEditingAction, "After opening editing app"),
        new(LangId.Settings_EditApps, "Image editing apps"),
        new(LangId.Settings_EditApps_AppName, "App name"),
        new(LangId.Settings_EditAppDialog_AddApp, "Add an app for editing"),
        new(LangId.Settings_EditAppDialog_EditApp, "Edit app"),

        new(LangId.Settings_Window, "Window"),

        new(LangId.Settings_Controls, "Controls"),
        new(LangId.Settings_Layout_ArrangeHint, "Drag the toolbar and gallery onto a slot to rearrange them in the app window."),
        new(LangId.Settings_Layout_Viewer, "Viewer"),

        new(LangId.Settings_Layout_Toolbar, "Toolbar"),
        new(LangId.Settings_Layout_Gallery, "Gallery"),
        new(LangId.Settings_Layout_ToolbarPosition, "Toolbar position"),
        new(LangId.Settings_Layout_GalleryPosition, "Gallery position"),

        new(LangId.Settings_ImageInfo, "Image information"),
        new(LangId.Settings_ImageInfoOverlay, "Image information overlay"),

        new(LangId.Settings_Appearance, "Appearance"),
        new(LangId.Settings_EnableNavigationButtons, "Show navigation arrow buttons"),
        new(LangId.Settings_ViewerScrollbarMode, "Show scrollbars"),
        new(LangId.Settings_EnableCenterWindowFit, "Automatically center the window in Window Fit mode"),
        new(LangId.Settings_EnableVectorRenderer, "Use the vector renderer for SVG images"),
        new(LangId.Settings_CheckerboardMode, "Checkerboard background"),

        new(LangId.Settings_Panning, "Panning"),
        new(LangId.Settings_EnableFreePan, "Enable free panning"),
        new(LangId.Settings_PanMargin, "Panning margin: {0}"),
        new(LangId.Settings_PanSpeed, "Panning speed: {0}"),

        new(LangId.Settings_Zooming, "Zooming"),
        new(LangId.Settings_ImageInterpolation, "Image interpolation"),
        new(LangId.Settings_ImageInterpolation_ScaleDown, "When zoom < 100%"),
        new(LangId.Settings_ImageInterpolation_ScaleUp, "When zoom > 100%"),
        new(LangId.Settings_ZoomSpeed, "Zoom speed: {0}"),
        new(LangId.Settings_ZoomLevels, "Zoom levels"),
        new(LangId.Settings_UseSmoothZooming, "Use smooth zooming"),
        new(LangId.Settings_LoadDefaultZoomLevels, "Load default zoom levels"),

        new(LangId.Settings_Toolbar_ShowToolbarInFullscreen, "Show toolbar in Full Screen mode"),
        new(LangId.Settings_Toolbar_ToolbarIconHeight, "Toolbar icon size: {0}"),

        new(LangId.Settings_Toolbar_AddNewButton, "Add a custom toolbar button"),
        new(LangId.Settings_Toolbar_EditButton, "Edit toolbar button"),

        new(LangId.Settings_Toolbar_ToolbarButtons, "Toolbar buttons"),
        new(LangId.Settings_Toolbar_AddCustomButton, "Add a custom button…"),
        new(LangId.Settings_Toolbar_AvailableButtons, "Available buttons"),
        new(LangId.Settings_Toolbar_CurrentButtons, "Current buttons"),
        new(LangId.Settings_Toolbar_Errors_ButtonIdDuplicated, "A button with the ID '{0}' has already been defined. Please choose a different and unique ID for your button to avoid conflicts."),

        new(LangId.Settings_Toolbar_ButtonText, "Button text"),
        new(LangId.Settings_Toolbar_ShowButtonText, "Show text beside the icon"),
        new(LangId.Settings_Toolbar_AlignRight, "Place on the right side of the toolbar"),
        new(LangId.Settings_Toolbar_CustomIcon, "Custom…"),
        new(LangId.Settings_Toolbar_ConfigBinding, "Enable toggle binding"),
        new(LangId.Settings_Toolbar_ConfigBindingName, "Config name"),
        new(LangId.Settings_Toolbar_ConfigBindingValue, "Config value"),
        new(LangId.Settings_Toolbar_RecordHotkeyHint, "Click here, then press a hotkey"),
        new(LangId.Settings_Toolbar_BuiltInReadonly, "This is a built-in button, so its properties can't be changed."),

        new(LangId.Settings_Toolbar_ArrangeHint, "Drag to reorder items, or move them between lists to add and remove. Arrow keys can also be used for positioning."),

        new(LangId.Settings_ShowGalleryInFullscreen, "Show gallery in Full Screen mode"),
        new(LangId.Settings_ShowGalleryFileName, "Show thumbnail filename"),
        new(LangId.Settings_EnableGalleryShellThumbnail, "Use system shell for thumbnails"),
        new(LangId.Settings_ThumbnailSize, "Thumbnail size (in pixels): {0}"),
        new(LangId.Settings_GalleryCacheSizeInMb, "Maximum gallery cache size (in megabytes)"),
        new(LangId.Settings_GalleryColumns, "Number of thumbnail columns in vertical gallery layout: {0}"),

        new(LangId.Settings_MouseWheelAction, "Mouse wheel action"),
        new(LangId.Settings_MouseClickAction, "Mouse click action"),

        new(LangId.Settings_Keyboard_MenuHotkeys, "Menu hotkeys"),
        new(LangId.Settings_Keyboard_Action, "Action"),
        new(LangId.Settings_Keyboard_NoResults, "No matching actions"),
        new(LangId.Settings_Keyboard_EditTitle, "Edit hotkeys"),
        new(LangId.Settings_Keyboard_Conflict, "This hotkey is assigned to more than one action."),

        new(LangId.Settings_FileExtensionIcons, "File extension icons"),
        new(LangId.Settings_FileExtensionIcons_Description, "For customizing file extension icons, download an icon pack, place all .ICO files in the extension icon folder, and click the '{0}' button."),
        new(LangId.Settings_OpenExtensionIconFolder, "Open extension icon folder"),

        new(LangId.Settings_DefaultPhotoViewer, "Default photo viewer"),
        new(LangId.Settings_AppMenuEntry, "Applications menu"),
        new(LangId.Settings_AppMenuEntry_Description, "Register Photo Viewer in your applications menu, so you can launch it from there and set it as your default image viewer."),
        new(LangId.Settings_AppMenuEntry_Success, "Photo Viewer has been registered in your applications menu."),
        new(LangId.Settings_AppMenuEntry_RemoveSuccess, "Photo Viewer is no longer in your applications menu."),
        new(LangId.Settings_AppMenuEntry_Error, "Could not update the applications menu."),
        new(LangId.Settings_DefaultPhotoViewer_Description, "Register the supported formats of Photo Viewer with Windows. You might need to open the Default apps settings and manually select Photo Viewer from the list for it to take effect."),
        new(LangId.Settings_DefaultPhotoViewer_ScopePerMachine, "Scope: all user accounts on this computer (per-machine)."),
        new(LangId.Settings_DefaultPhotoViewer_ScopePerUser, "Scope: your user account only (per-user)."),
        new(LangId.Settings_OpenDefaultAppsSetting, "Open Default apps setting"),

        new(LangId.Settings_FileFormats, "File formats"),
        new(LangId.Settings_TotalSupportedFormats, "Total supported formats: {0}"),
        new(LangId.Settings_AddNewFileExtension, "Add new file extension"),

        new(LangId.Settings_Tools_AddNewTool, "Add an external tool"),
        new(LangId.Settings_Tools_EditTool, "Edit external tool"),
        new(LangId.Settings_Tools_ToolLaunchFailed, "Could not launch \"{0}\""),
        new(LangId.Settings_Tools_ToolLaunchFailed_Description, "Do you want to update the tool again?"),
        new(LangId.Settings_Tools_Integrated, "Integrated"),
        new(LangId.Settings_Tools_IntegratedWith, "Integrated with {0}"),
        new(LangId.Settings_Tools_Errors_ToolIdDuplicated, "The tool ID \"{0}\" is already in use."),

        new(LangId.Settings_Plugins_OpenPluginFolder, "Open plugin folder"),
        new(LangId.Settings_Plugins_SupportedExtensions, "Supported extensions"),
        new(LangId.Settings_Plugins_ViewMetadata, "Plugin information"),
        new(LangId.Settings_Plugins_FolderPath, "Folder"),
        new(LangId.Settings_Plugins_InstallSuccess, "Plugin installed successfully"),
        new(LangId.Settings_Plugins_DeleteConfirm, "Delete this plugin? This permanently removes it from Photo Viewer."),
        new(LangId.Settings_Plugins_Status, "Status"),
        new(LangId.Settings_Plugins_Enable, "Enable"),
        new(LangId.Settings_Plugins_TrustAndEnable, "Trust and enable"),
        new(LangId.Settings_Plugins_StatusEnabled, "Enabled"),
        new(LangId.Settings_Plugins_StatusDisabled, "Disabled"),
        new(LangId.Settings_Plugins_StatusUntrusted, "Not enabled"),
        new(LangId.Settings_Plugins_StatusChanged, "File changed"),
        new(LangId.Settings_Plugins_StatusNotLoaded, "Not loaded"),
        new(LangId.Settings_Plugins_TrustTitle, "Enable this plugin?"),
        new(LangId.Settings_Plugins_TrustPrompt, "\"{0}\" is a native plugin that runs inside Photo Viewer with full access to your files and system. Only enable plugins you obtained from a source you trust."),
        new(LangId.Settings_Plugins_TrustChangedWarning, "This plugin's file has changed since it was last enabled. Only re-enable it if you updated the plugin yourself."),
        new(LangId.Settings_Plugins_EnableToLoad, "Enable the plugin below to load it."),
        new(LangId.Settings_Plugins_ExtensionsHint, "This plugin can open and save the file types below. Turn off any you would rather leave to Photo Viewer or another plugin."),
        new(LangId.Settings_Plugins_FormatsAfterEnable, "Enable this plugin to see and choose the file types it supports."),
        new(LangId.Settings_Plugins_NoFormats, "No file types supported."),

        new(LangId.Settings_DisplayLanguage, "Display language"),
        new(LangId.Settings_Refresh, "Refresh"),
        new(LangId.Settings_ExportLanguagePack, "Export language pack…"),

        new(LangId.Settings_WindowBackdrop, "Window backdrop"),

        new(LangId.Settings_Theme, "Theme"),
        new(LangId.Settings_DarkTheme, "Dark"),
        new(LangId.Settings_LightTheme, "Light"),
        new(LangId.Settings_Theme_OpenThemeFolder, "Open theme folder"),
        new(LangId.Settings_Theme_InstallTheme, "Install theme packs"),

        new(LangId.Settings_UseThemeForDarkMode, "Use this theme for dark mode"),
        new(LangId.Settings_UseThemeForLightMode, "Use this theme for light mode"),

        new(LangId.Tool_Crop_LblAspectRatio, "Aspect ratio"),
        new(LangId.Tool_Crop_LblLocation, "Location"),
        new(LangId.Tool_Crop_LblSize, "Size"),

        new(LangId.Tool_Crop_SelectionAspectRatio_FreeRatio, "Free ratio"),
        new(LangId.Tool_Crop_SelectionAspectRatio_Custom, "Custom…"),
        new(LangId.Tool_Crop_SelectionAspectRatio_Original, "Original"),

        new(LangId.Tool_Crop_BtnReset, "Reset"),
        new(LangId.Tool_Crop_BtnCrop, "Crop"),
        new(LangId.Tool_Crop_BtnCopy, "Copy"),

        new(LangId.Tool_Crop_Title, "Crop settings"),
        new(LangId.Tool_Crop_ChkCloseToolAfterSaving, "Close Crop tool after saving"),
        new(LangId.Tool_Crop_LblDefaultSelection, "Default selection"),
        new(LangId.Tool_Crop_ChkAutoCenterSelection, "Auto-center selection"),

        new(LangId.Tool_Crop_DefaultSelectionType_UseTheLastSelection, "Use the last selection"),
        new(LangId.Tool_Crop_DefaultSelectionType_SelectNone, "Select none"),
        new(LangId.Tool_Crop_DefaultSelectionType_SelectX, "Select {0}"),
        new(LangId.Tool_Crop_DefaultSelectionType_SelectAll, "Select all"),
        new(LangId.Tool_Crop_DefaultSelectionType_CustomArea, "Custom area…"),

        new(LangId.Tool_Hdr_LblMode, "Mode"),
        new(LangId.Tool_Hdr_LblExposure, "Exposure: {0}"),
        new(LangId.Tool_Hdr_LblReferenceWhite, "Reference white: {0} nits"),
        new(LangId.Tool_Hdr_LblHighlightCompression, "Highlight compression: {0}"),
        new(LangId.Tool_Hdr_LblSaturation, "Saturation: {0}"),
        new(LangId.Tool_Hdr_BtnReset, "Reset"),

        new(LangId.Tool_Projector_WindowTitle, "Projector {0}"),
        new(LangId.Tool_Projector_BtnAdd, "Add projector"),
        new(LangId.Tool_Projector_ChkSyncView, "Sync projectors with main window"),
        new(LangId.Tool_Projector_ChkShowPointer, "Highlight cursor on projectors"),
        new(LangId.Tool_Projector_ModeFullScreen, "Full screen"),
        new(LangId.Tool_Projector_ModeMaximized, "Maximized"),
        new(LangId.Tool_Projector_ModeNormal, "Window"),
        new(LangId.Tool_Projector_ModeTiled, "Tiled"),
        new(LangId.Tool_Projector_LblLayout, "Layout on screen {0}"),
        new(LangId.Tool_Projector_Monitor, "Monitor {0}"),
        new(LangId.Tool_Projector_DragHint, "Drag onto a monitor to move the projector there"),
        new(LangId.Tool_Projector_ScreenInfo, "Screen {0}: {1}×{2} pixels, {3}% scale"),
        new(LangId.Tool_Projector_MainWindow, "Main window"),

        new(LangId.Tool_ColorPicker_Title, "Color picker settings"),
        new(LangId.Tool_ColorPicker_ChkShowRgbA, "Use RGB format with alpha value"),
        new(LangId.Tool_ColorPicker_ChkShowHexA, "Use HEX format with alpha value"),
        new(LangId.Tool_ColorPicker_ChkShowHslA, "Use HSL format with alpha value"),
        new(LangId.Tool_ColorPicker_ChkShowHsvA, "Use HSV format with alpha value"),
        new(LangId.Tool_ColorPicker_ChkShowCmykA, "Use CMYK format with alpha value"),
        new(LangId.Tool_ColorPicker_ChkShowCIELabA, "Use CIELAB format with alpha value"),

        new(LangId.Tool_Resizer_RadResizeByPixels, "Pixels" ),
        new(LangId.Tool_Resizer_RadResizeByPercentage, "Percentage" ),
        new(LangId.Tool_Resizer_ChkKeepRatio, "Keep ratio proportional" ),
        new(LangId.Tool_Resizer_LblResample, "Resample:" ),
        new(LangId.Tool_Resizer_LblCurrentSize, "Current Size:" ),
        new(LangId.Tool_Resizer_LblNewSize, "New Size:" ),
        new(LangId.Tool_Resizer_Errors_SizeTooLarge, "Each side must be {0} px or less." ),

        new(LangId.Print_Title, "Print" ),
        new(LangId.Print_LblPrinter, "Printer" ),
        new(LangId.Print_LblCopies, "Copies" ),
        new(LangId.Print_ChkCollate, "Collate" ),
        new(LangId.Print_LblPaper, "Paper size" ),
        new(LangId.Print_LblOrientation, "Orientation" ),
        new(LangId.Print_LblLayout, "Layout" ),
        new(LangId.Print_LblFit, "Fit" ),
        new(LangId.Print_ChkAutoRotate, "Rotate photos to fit" ),
        new(LangId.Print_LblMargins, "Margins" ),
        new(LangId.Print_LblPrintsEach, "Prints of each page" ),
        new(LangId.Print_ChkFillPage, "Fill each page" ),
        new(LangId.Print_LblRegion, "What to print" ),
        new(LangId.Print_LblPages, "Pages" ),
        new(LangId.Print_LblMoreSettings, "More settings" ),
        new(LangId.Print_LblColor, "Color" ),
        new(LangId.Print_LblQuality, "Quality" ),
        new(LangId.Print_LblTwoSided, "Two-sided" ),
        new(LangId.Print_ChkCaptions, "Show file names" ),
        new(LangId.Print_BtnProperties, "Printer properties…" ),
        new(LangId.Print_BtnSystemDialog, "Use the system dialog…" ),
        new(LangId.Print_BtnAddPrinter, "Add a printer" ),
        new(LangId.Print_SaveAsPdf, "Save as PDF" ),
        new(LangId.Print_Summary, "{0} page(s), {1} print(s)" ),
        new(LangId.Print_PageOf, "{0} of {1}" ),
        new(LangId.Print_Printing, "Printing page {0} of {1}…" ),
        new(LangId.Print_Sent, "Sent to {0}" ),
        new(LangId.Print_Saved, "Saved as PDF" ),
        new(LangId.Print_LoadingPrinters, "Loading printers…" ),
        new(LangId.Print_NoPrinters, "No printers were found." ),
        new(LangId.Print_CupsMissing, "CUPS is not installed." ),
        new(LangId.Print_PrinterUnavailable, "This printer is not responding." ),
        new(LangId.Print_WarnPrintShrunk, "The print size is larger than the paper, so it was scaled down." ),
        new(LangId.Print_WarnManyPages, "This will print {0} pages. Do you want to continue?" ),
        new(LangId.Print_RangeInvalid, "Enter pages like 1-3, 5" ),
        new(LangId.Print_PreviousPage, "Previous page" ),
        new(LangId.Print_NextPage, "Next page" ),
        new(LangId.Print_Dpi, "{0} dpi" ),
        new(LangId.Print_Layout_FullPage, "Full page" ),
        new(LangId.Print_Layout_Grid, "{0} per page" ),
        new(LangId.Print_Layout_Wallet, "Wallet" ),
        new(LangId.Print_Layout_Passport, "Passport {0}" ),
        new(LangId.Print_Layout_ContactSheet, "Contact sheet" ),

        new(LangId.QuickSetup_Title, "Photo Viewer Quick Setup" ),
        new(LangId.QuickSetup_StepInfo, "Step {0} of {1}" ),
        new(LangId.QuickSetup_SkipAndLaunch, "Skip this and launch Photo Viewer" ),

        new(LangId.QuickSetup_SelectLanguage, "Display language" ),
        new(LangId.QuickSetup_SelectProfile, "Select a profile" ),
        new(LangId.QuickSetup_StandardUser, "Standard user" ),
        new(LangId.QuickSetup_ProfessionalUser, "Professional user" ),
        new(LangId.QuickSetup_SettingsWillBeApplied, "Settings will be applied:" ),
        new(LangId.QuickSetup_SettingProfileDescription, "To modify these settings later, simply open the app Settings." ),

        new(LangId.QuickSetup_RegisterImageFormats, "Do you want to register the supported image formats of Photo Viewer with Windows? You can unregister it later in Settings > File type associations." ),

        new(LangId.QuickSetup_ConfirmCloseProcess, "Are you ready to apply the settings?" ),
        new(LangId.QuickSetup_ConfirmCloseProcess_Description, "All other running instances of Photo Viewer will be closed before the settings are saved." ),

    ];

}
