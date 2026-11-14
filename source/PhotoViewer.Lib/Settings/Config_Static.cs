using PhotoViewer.Common.Actions;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.Tools;
using PhotoViewer.UI;
using PhotoViewer.UI.Viewer;
using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
namespace PhotoViewer.Common;

public partial class Config
{

    [JsonIgnore]
    public static float SPEC_VERSION => 10f;

    [JsonIgnore]
    public static string CONFIG_USER => "igconfig.json";

    [JsonIgnore]
    public static string CONFIG_DEFAULT => "igconfig.default.json";

    [JsonIgnore]
    public static string CONFIG_ADMIN => "igconfig.admin.json";

    [JsonIgnore]
    public static Exception? LoadingException { get; private set; } = null;

    [JsonIgnore]
    public static Exception? SavingException { get; private set; } = null;

    [JsonIgnore]
    public static string? IncompatibleUserConfigPath { get; private set; } = null;

    [JsonIgnore]
    public static FrozenSet<ConfigId> AdminLockedConfigs { get; private set; } = FrozenSet<ConfigId>.Empty;

    [JsonIgnore]
    public static FrozenSet<string> LockedFeatures { get; private set; } = FrozenSet<string>.Empty;

    [JsonIgnore]
    public static ReadOnlyCollection<string> DefaultFileFormats { get; } = new(
        Const.IMAGE_FORMATS.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    );

    [JsonIgnore]
    public static ReadOnlyCollection<string> DefaultImageInfoTags { get; } = new([
        nameof(AppStatusInfo.Name),
        nameof(AppStatusInfo.ListCount),
        nameof(AppStatusInfo.FrameCount),
        nameof(AppStatusInfo.Zoom),
        nameof(AppStatusInfo.Dimension),
        nameof(AppStatusInfo.FileSize),
        nameof(AppStatusInfo.ColorSpace),
        nameof(AppStatusInfo.HdrInfo),
        nameof(AppStatusInfo.ExifRating),
        nameof(AppStatusInfo.DateTimeAuto),
        nameof(AppStatusInfo.AppName),
    ]);

    [JsonIgnore]
    public static Dictionary<MouseWheelEvent, MouseWheelAction> DefaultMouseWheelActions { get; } = new()
    {
        [MouseWheelEvent.Scroll] = MouseWheelAction.Zoom,
        [MouseWheelEvent.CtrlAndScroll] = MouseWheelAction.PanVertically,
        [MouseWheelEvent.ShiftAndScroll] = MouseWheelAction.PanHorizontally,
        [MouseWheelEvent.AltAndScroll] = MouseWheelAction.BrowseImages,
    };

    [JsonIgnore]
    public static Dictionary<MouseClickEvent, SingleAction> DefaultMouseClickActions { get; } = new()
    {
        [MouseClickEvent.LeftDoubleClick] = new SingleAction(API.IG_SetZoomForMouseClick),
        [MouseClickEvent.RightClick] = new SingleAction(API.IG_OpenContextMenu),
        [MouseClickEvent.WheelClick] = new SingleAction(API.IG_Refresh),
        [MouseClickEvent.XButton1Click] = new SingleAction(API.IG_ViewPrevious),
        [MouseClickEvent.XButton2Click] = new SingleAction(API.IG_ViewNext),
    };

    [JsonIgnore]
    public static ReadOnlyCollection<ToolbarItemModel> DefaultToolbarItems =>
    [
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.OpenFile) }",
            Image = nameof(IgThemeIcon.OpenFile),
            Text = Lang.KeysMap[LangId.Menu_MnuOpenFile],
            Alignment = ToolbarItemAlignment.Right,
            OnClick = new(LangId.Menu_MnuOpenFile, API.IG_OpenFile),
        },

        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ViewPreviousImage)}",
            Image = nameof(IgThemeIcon.ViewPreviousImage),
            Text = Lang.KeysMap[LangId.Menu_MnuViewPrevious],
            OnClick = new(LangId.Menu_MnuViewPrevious, API.IG_ViewPrevious),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ViewNextImage)}",
            Image = nameof(IgThemeIcon.ViewNextImage),
            Text = Lang.KeysMap[LangId.Menu_MnuViewNext],
            OnClick = new(LangId.Menu_MnuViewNext, API.IG_ViewNext),
        },
        ToolbarItemModel.Separator,

        new() {
            Id = $"Btn_{nameof(IgThemeIcon.RotateLeft)}",
            Image = nameof(IgThemeIcon.RotateLeft),
            Text = Lang.KeysMap[LangId.Menu_MnuRotateLeft],
            OnClick = new(LangId.Menu_MnuRotateLeft, API.IG_Rotate, nameof(RotateOption.Left)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.RotateRight)}",
            Image = nameof(IgThemeIcon.RotateRight),
            Text = Lang.KeysMap[LangId.Menu_MnuRotateRight],
            OnClick = new(LangId.Menu_MnuRotateRight, API.IG_Rotate, nameof(RotateOption.Right)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.FlipHorz)}",
            Image = nameof(IgThemeIcon.FlipHorz),
            Text = Lang.KeysMap[LangId.Menu_MnuFlipHorizontal],
            OnClick = new(LangId.Menu_MnuFlipHorizontal, API.IG_FlipImage, nameof(FlipOptions.Horizontal)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.FlipVert)}",
            Image = nameof(IgThemeIcon.FlipVert),
            Text = Lang.KeysMap[LangId.Menu_MnuFlipVertical],
            OnClick = new(LangId.Menu_MnuFlipVertical, API.IG_FlipImage, nameof(FlipOptions.Vertical)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Crop)}",
            Image = nameof(IgThemeIcon.Crop),
            Text = Lang.KeysMap[LangId.Menu_MnuCropTool],
            ConfigBinding = nameof(Config.LastOpenedTool),
            ConfigBindingValue = CropImageToolControl.TOOL_ID,
            OnClick = new(LangId.Menu_MnuCropTool, API.IG_ToggleTool, CropImageToolControl.TOOL_ID),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ColorPicker)}",
            Image = nameof(IgThemeIcon.ColorPicker),
            Text = Lang.KeysMap[LangId.Menu_MnuColorPicker],
            ConfigBinding = nameof(Config.LastOpenedTool),
            ConfigBindingValue = ColorPickerToolControl.TOOL_ID,
            OnClick = new(LangId.Menu_MnuColorPicker, API.IG_ToggleTool, ColorPickerToolControl.TOOL_ID),
        },
        ToolbarItemModel.Separator,

        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Refresh)}",
            Image = nameof(IgThemeIcon.Refresh),
            Text = Lang.KeysMap[LangId.Menu_MnuRefresh],
            OnClick = new(LangId.Menu_MnuRefresh, API.IG_Refresh),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Gallery)}",
            Image = nameof(IgThemeIcon.Gallery),
            Text = Lang.KeysMap[LangId.Menu_MnuToggleGallery],
            ConfigBinding = nameof(Config.ShowGallery),
            ConfigBindingValue = "True",
            OnClick = new(LangId.Menu_MnuToggleGallery, API.IG_ToggleGallery),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Checkerboard)}",
            Image = nameof(IgThemeIcon.Checkerboard),
            Text = Lang.KeysMap[LangId.Menu_MnuToggleCheckerboard],
            ConfigBinding = nameof(Config.CheckerboardMode),
            ConfigBindingValue = $"!{nameof(CheckerboardType.None)}",
            OnClick = new(LangId.Menu_MnuToggleCheckerboard, API.IG_ToggleCheckerboard),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.FullScreen)}",
            Image = nameof(IgThemeIcon.FullScreen),
            Text = Lang.KeysMap[LangId.Menu_MnuFullScreen],
            ConfigBinding = nameof(Config.EnableFullScreen),
            ConfigBindingValue = "True",
            OnClick = new(LangId.Menu_MnuFullScreen, API.IG_ToggleFullScreen),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Slideshow)}",
            Image = nameof(IgThemeIcon.Slideshow),
            Text = Lang.KeysMap[LangId.Menu_MnuSlideshow],
            ConfigBinding = nameof(Config.EnableSlideshow),
            ConfigBindingValue = "True",
            OnClick = new(LangId.Menu_MnuSlideshow, API.IG_ToggleSlideshow),
        },
        ToolbarItemModel.Separator,

        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Delete)}",
            Image = nameof(IgThemeIcon.Delete),
            Text = Lang.KeysMap[LangId.Menu_MnuMoveToRecycleBin],
            OnClick = new(LangId.Menu_MnuMoveToRecycleBin, API.IG_Delete),
        }
    ];

    [JsonIgnore]
    public static ReadOnlyCollection<ToolbarItemModel> BuiltInToolbarItems =>
    [
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.OpenFile)}",
            Image = nameof(IgThemeIcon.OpenFile),
            Text = Lang.KeysMap[LangId.Menu_MnuOpenFile],
            OnClick = new(LangId.Menu_MnuOpenFile, API.IG_OpenFile),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Save)}",
            Image = nameof(IgThemeIcon.Save),
            Text = Lang.KeysMap[LangId.Menu_MnuSave],
            OnClick = new(LangId.Menu_MnuSave, API.IG_Save),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Print)}",
            Image = nameof(IgThemeIcon.Print),
            Text = Lang.KeysMap[LangId.Menu_MnuPrint],
            OnClick = new(LangId.Menu_MnuPrint, API.IG_Print),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Export)}",
            Image = nameof(IgThemeIcon.Export),
            Text = Lang.KeysMap[LangId.Menu_MnuExportFrames],
            OnClick = new(LangId.Menu_MnuExportFrames, API.IG_ExportImageFrames),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Edit)}",
            Image = nameof(IgThemeIcon.Edit),
            Text = Lang.KeysMap[LangId.Menu_MnuEdit],
            OnClick = new(LangId.Menu_MnuEdit, API.IG_OpenEditingApp),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Delete)}",
            Image = nameof(IgThemeIcon.Delete),
            Text = Lang.KeysMap[LangId.Menu_MnuMoveToRecycleBin],
            OnClick = new(LangId.Menu_MnuMoveToRecycleBin, API.IG_Delete, "true"),
        },

        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ViewPreviousImage)}",
            Image = nameof(IgThemeIcon.ViewPreviousImage),
            Text = Lang.KeysMap[LangId.Menu_MnuViewPrevious],
            OnClick = new(LangId.Menu_MnuViewPrevious, API.IG_ViewPrevious),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ViewNextImage)}",
            Image = nameof(IgThemeIcon.ViewNextImage),
            Text = Lang.KeysMap[LangId.Menu_MnuViewNext],
            OnClick = new(LangId.Menu_MnuViewNext, API.IG_ViewNext),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ViewFirstImage)}",
            Image = nameof(IgThemeIcon.ViewFirstImage),
            Text = Lang.KeysMap[LangId.Menu_MnuGoToFirst],
            OnClick = new(LangId.Menu_MnuGoToFirst, API.IG_GotoFirst),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ViewLastImage)}",
            Image = nameof(IgThemeIcon.ViewLastImage),
            Text = Lang.KeysMap[LangId.Menu_MnuGoToLast],
            OnClick = new(LangId.Menu_MnuGoToLast, API.IG_GotoLast),
        },

        new() {
            Id = $"Btn_{nameof(IgThemeIcon.AutoZoom)}",
            Image = nameof(IgThemeIcon.AutoZoom),
            Text = Lang.KeysMap[LangId.Menu_MnuAutoZoom],
            ConfigBinding = nameof(Config.ZoomMode),
            ConfigBindingValue = ZoomMode.AutoZoom.ToString(),
            OnClick = new(LangId.Menu_MnuAutoZoom, API.IG_SetZoomMode, nameof(ZoomMode.AutoZoom)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.LockZoom)}",
            Image = nameof(IgThemeIcon.LockZoom),
            Text = Lang.KeysMap[LangId.Menu_MnuLockZoom],
            ConfigBinding = nameof(Config.ZoomMode),
            ConfigBindingValue = ZoomMode.LockZoom.ToString(),
            OnClick = new(LangId.Menu_MnuLockZoom, API.IG_SetZoomMode, nameof(ZoomMode.LockZoom)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ScaleToWidth)}",
            Image = nameof(IgThemeIcon.ScaleToWidth),
            Text = Lang.KeysMap[LangId.Menu_MnuScaleToWidth],
            ConfigBinding = nameof(Config.ZoomMode),
            ConfigBindingValue = ZoomMode.ScaleToWidth.ToString(),
            OnClick = new(LangId.Menu_MnuScaleToWidth, API.IG_SetZoomMode, nameof(ZoomMode.ScaleToWidth)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ScaleToHeight)}",
            Image = nameof(IgThemeIcon.ScaleToHeight),
            Text = Lang.KeysMap[LangId.Menu_MnuScaleToHeight],
            ConfigBinding = nameof(Config.ZoomMode),
            ConfigBindingValue = ZoomMode.ScaleToHeight.ToString(),
            OnClick = new(LangId.Menu_MnuScaleToHeight, API.IG_SetZoomMode, nameof(ZoomMode.ScaleToHeight)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ScaleToFit)}",
            Image = nameof(IgThemeIcon.ScaleToFit),
            Text = Lang.KeysMap[LangId.Menu_MnuScaleToFit],
            ConfigBinding = nameof(Config.ZoomMode),
            ConfigBindingValue = ZoomMode.ScaleToFit.ToString(),
            OnClick = new(LangId.Menu_MnuScaleToFit, API.IG_SetZoomMode, nameof(ZoomMode.ScaleToFit)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ScaleToFill)}",
            Image = nameof(IgThemeIcon.ScaleToFill),
            Text = Lang.KeysMap[LangId.Menu_MnuScaleToFill],
            ConfigBinding = nameof(Config.ZoomMode),
            ConfigBindingValue = ZoomMode.ScaleToFill.ToString(),
            OnClick = new(LangId.Menu_MnuScaleToFill, API.IG_SetZoomMode, nameof(ZoomMode.ScaleToFill)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ActualSize)}",
            Image = nameof(IgThemeIcon.ActualSize),
            Text = Lang.KeysMap[LangId.Menu_MnuActualSize],
            OnClick = new(LangId.Menu_MnuActualSize, API.IG_SetZoom, "1"),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ZoomIn)}",
            Image = nameof(IgThemeIcon.ZoomIn),
            Text = Lang.KeysMap[LangId.Menu_MnuZoomIn],
            OnClick = new(LangId.Menu_MnuZoomIn, API.IG_ZoomIn),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ZoomOut)}",
            Image = nameof(IgThemeIcon.ZoomOut),
            Text = Lang.KeysMap[LangId.Menu_MnuZoomOut],
            OnClick = new(LangId.Menu_MnuZoomOut, API.IG_ZoomOut),
        },

        new() {
            Id = $"Btn_{nameof(IgThemeIcon.RotateLeft)}",
            Image = nameof(IgThemeIcon.RotateLeft),
            Text = Lang.KeysMap[LangId.Menu_MnuRotateLeft],
            OnClick = new(LangId.Menu_MnuRotateLeft, API.IG_Rotate, nameof(RotateOption.Left)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.RotateRight)}",
            Image = nameof(IgThemeIcon.RotateRight),
            Text = Lang.KeysMap[LangId.Menu_MnuRotateRight],
            OnClick = new(LangId.Menu_MnuRotateRight, API.IG_Rotate, nameof(RotateOption.Right)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.FlipHorz)}",
            Image = nameof(IgThemeIcon.FlipHorz),
            Text = Lang.KeysMap[LangId.Menu_MnuFlipHorizontal],
            OnClick = new(LangId.Menu_MnuFlipHorizontal, API.IG_FlipImage, nameof(FlipOptions.Horizontal)),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.FlipVert)}",
            Image = nameof(IgThemeIcon.FlipVert),
            Text = Lang.KeysMap[LangId.Menu_MnuFlipVertical],
            OnClick = new(LangId.Menu_MnuFlipVertical, API.IG_FlipImage, nameof(FlipOptions.Vertical)),
        },

        new() {
            Id = $"Btn_{nameof(IgThemeIcon.ColorPicker)}",
            Image = nameof(IgThemeIcon.ColorPicker),
            Text = Lang.KeysMap[LangId.Menu_MnuColorPicker],
            OnClick = new(LangId.Menu_MnuColorPicker, API.IG_ToggleTool, ColorPickerToolControl.TOOL_ID),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Crop)}",
            Image = nameof(IgThemeIcon.Crop),
            Text = Lang.KeysMap[LangId.Menu_MnuCropTool],
            OnClick = new(LangId.Menu_MnuCropTool, API.IG_ToggleTool, CropImageToolControl.TOOL_ID),
        },

        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Refresh)}",
            Image = nameof(IgThemeIcon.Refresh),
            Text = Lang.KeysMap[LangId.Menu_MnuRefresh],
            OnClick = new(LangId.Menu_MnuRefresh, API.IG_Refresh),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Gallery)}",
            Image = nameof(IgThemeIcon.Gallery),
            Text = Lang.KeysMap[LangId.Menu_MnuToggleGallery],
            ConfigBinding = nameof(Config.ShowGallery),
            ConfigBindingValue = "True",
            OnClick = new(LangId.Menu_MnuToggleGallery, API.IG_ToggleGallery),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Checkerboard)}",
            Image = nameof(IgThemeIcon.Checkerboard),
            Text = Lang.KeysMap[LangId.Menu_MnuToggleCheckerboard],
            ConfigBinding = nameof(Config.CheckerboardMode),
            ConfigBindingValue = $"!{nameof(CheckerboardType.None)}",
            OnClick = new(LangId.Menu_MnuToggleCheckerboard, API.IG_ToggleCheckerboard),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.WindowFit)}",
            Image = nameof(IgThemeIcon.WindowFit),
            Text = Lang.KeysMap[LangId.Menu_MnuWindowFit],
            ConfigBinding = nameof(Config.EnableWindowFit),
            ConfigBindingValue = "True",
            OnClick = new(LangId.Menu_MnuWindowFit, API.IG_ToggleWindowFit),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.FullScreen)}",
            Image = nameof(IgThemeIcon.FullScreen),
            Text = Lang.KeysMap[LangId.Menu_MnuFullScreen],
            ConfigBinding = nameof(Config.EnableFullScreen),
            ConfigBindingValue = "True",
            OnClick = new(LangId.Menu_MnuFullScreen, API.IG_ToggleFullScreen),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Slideshow)}",
            Image = nameof(IgThemeIcon.Slideshow),
            Text = Lang.KeysMap[LangId.Menu_MnuSlideshow],
            ConfigBinding = nameof(Config.EnableSlideshow),
            ConfigBindingValue = "True",
            OnClick = new(LangId.Menu_MnuSlideshow, API.IG_ToggleSlideshow),
        },
        new() {
            Id = $"Btn_{nameof(IgThemeIcon.Exit)}",
            Image = nameof(IgThemeIcon.Exit),
            Text = Lang.KeysMap[LangId.Menu_MnuExit],
            OnClick = new(LangId.Menu_MnuExit, API.IG_Exit),
        },
    ];

    public static Config Load(string configFileName, string[]? cliArgs = null)
    {
        Config? appConfig = null;

        LoadAdminPolicies();

        try
        {
            var jsonOptions = BHelper.CreateJsonOptions();
            var jsonContext = new ConfigJsonContext(jsonOptions);

            using var defaultDoc = ReadDefaultConfigDocument();

            var userConfigPath = BHelper.ConfigDir(configFileName);
            using var userDoc = BHelper.ReadJsonDocFromFile(userConfigPath);

            var cliOverrides = ParseCliConfigArgs(cliArgs);

            using var adminDoc = ReadAdminConfigDocument();

            var effectiveDefaultDoc = IsCompatibleConfigLayer(defaultDoc) ? defaultDoc : null;
            var effectiveAdminDoc = IsCompatibleConfigLayer(adminDoc) ? adminDoc : null;
            var effectiveUserDoc = userDoc;
            if (!IsCompatibleConfigLayer(userDoc))
            {
                effectiveUserDoc = null;
                IncompatibleUserConfigPath = userConfigPath;
            }

            var mergedJson = MergeJsonLayers(effectiveDefaultDoc, effectiveUserDoc, cliOverrides, effectiveAdminDoc);

            var config = JsonSerializer.Deserialize(mergedJson, jsonContext.Config)
                ?? throw new FileLoadException("IGE: Could not parse merged config.");

            appConfig = MigrateUserConfigFile(config);

            ApplyPersistedToolConfigs(appConfig);
        }
        catch (Exception ex)
        {
            LoadingException = ex;
        }

        appConfig ??= new();

        EnsurePredefinedTools(appConfig);

        return appConfig;
    }

    public static bool IsConfigLocked(ConfigId id) => AdminLockedConfigs.Contains(id);

    private static void EnsurePredefinedTools(Config config)
    {
        if (IsConfigLocked(ConfigId.Tools)) return;

        var tools = config.Tools;
        if (!PredefinedExternalTools.Seed(tools)) return;

        config.Tools = tools;
    }

    private static void ApplyPersistedToolConfigs(Config config)
    {
        if (config.ToolSettings.TryGetValue(HdrToneMapperToolControl.TOOL_ID, out var hdrEl))
        {
            var opts = hdrEl.Deserialize(HdrToneMappingOptionsJsonContext.Default.HdrToneMappingOptions);
            if (opts is not null) Core.HdrToneMappingConfig = opts;
        }
    }

    private static bool IsCompatibleConfigLayer(JsonDocument? doc)
    {
        return doc == null || GetConfigLayerVersion(doc) >= SPEC_VERSION;
    }

    private static float GetConfigLayerVersion(JsonDocument doc)
    {
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object
            && TryGetPropertyIgnoreCase(root, nameof(_Metadata), out var meta)
            && meta.ValueKind == JsonValueKind.Object
            && TryGetPropertyIgnoreCase(meta, nameof(ConfigMetadata.Version), out var ver)
            && ver.ValueKind == JsonValueKind.Number
            && ver.TryGetSingle(out var version))
        {
            return version;
        }

        return SPEC_VERSION;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static void ApplyCliOverrides(Config config, string[]? cliArgs)
    {
        var overrides = ParseCliConfigArgs(cliArgs);
        if (overrides.Count == 0) return;

        try
        {
            var jsonOptions = BHelper.CreateJsonOptions();
            var jsonContext = new ConfigJsonContext(jsonOptions);

            var currentJson = JsonSerializer.SerializeToUtf8Bytes(config, jsonContext.Config);
            using var currentDoc = JsonDocument.Parse(currentJson);

            var mergedJson = MergeJsonLayers(null, currentDoc, overrides, null);

            var updated = JsonSerializer.Deserialize(mergedJson, jsonContext.Config);
            if (updated == null) return;

            foreach (var kvp in updated._values)
            {
                if (IsConfigLocked(kvp.Key)) continue;
                config.Set(kvp.Key, kvp.Value);
            }
        }
        catch { }
    }

    public static IReadOnlyList<ConfigId> ApplyJsonOverrides(Config config, string? json)
    {
        using var overrideDoc = ParseSettingsJson(json);

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targetIds = new List<ConfigId>();
        var unknownKeys = new List<string>();

        foreach (var prop in overrideDoc.RootElement.EnumerateObject())
        {
            if (string.Equals(prop.Name, nameof(_Metadata), StringComparison.OrdinalIgnoreCase)) continue;

            if (!Enum.TryParse<ConfigId>(prop.Name, ignoreCase: true, out var id))
            {
                unknownKeys.Add(prop.Name);
                continue;
            }

            if (_fileOnlyKeys.Contains(prop.Name))
            {
                System.Diagnostics.Debug.WriteLine($"[Config] Setting '{id}' ignored (not allowed outside a config file).");
                continue;
            }

            if (IsConfigLocked(id))
            {
                System.Diagnostics.Debug.WriteLine($"[Config] Setting '{id}' ignored (locked by {CONFIG_ADMIN}).");
                continue;
            }

            overrides[id.ToString()] = prop.Value.GetRawText();
            targetIds.Add(id);
        }

        if (unknownKeys.Count > 0)
        {
            throw new ArgumentException($"Unknown setting name(s): {string.Join(", ", unknownKeys)}.");
        }

        if (targetIds.Count == 0) return [];

        var jsonOptions = BHelper.CreateJsonOptions();
        var jsonContext = new ConfigJsonContext(jsonOptions);

        var currentJson = JsonSerializer.SerializeToUtf8Bytes(config, jsonContext.Config);
        using var currentDoc = JsonDocument.Parse(currentJson);
        var mergedJson = MergeJsonLayers(null, currentDoc, overrides, null);

        var updated = JsonSerializer.Deserialize(mergedJson, jsonContext.Config)
            ?? throw new JsonException("IGE: Could not parse the merged config.");

        var changedIds = new List<ConfigId>(targetIds.Count);
        foreach (var id in targetIds)
        {
            if (!updated._values.TryGetValue(id, out var value)) continue;

            config.Set(id, value);
            changedIds.Add(id);
        }

        return changedIds;
    }

    private static JsonDocument ParseSettingsJson(string? json)
    {
        const string shape = "The settings JSON must be an object of setting names.";

        if (string.IsNullOrWhiteSpace(json)) throw new JsonException($"No settings were given. {shape}");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new JsonException($"The settings JSON could not be parsed. {ex.Message}", ex);
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            throw new JsonException(shape);
        }

        return doc;
    }

    private static Config MigrateUserConfigFile(Config config)
    {
        var configVersion = config._Metadata.Version;

        config._Metadata.Version = SPEC_VERSION;

        if (SPEC_VERSION <= configVersion) return config;

        if (configVersion < 10)
        {
        }

        return config;
    }

    [JsonIgnore]
    public static string ThemePacksDir => BHelper.ConfigDir(Dir.Themes);

    public static async Task<List<IgTheme>> LoadAllThemePacksAsync()
    {
        var found = new Dictionary<string, IgTheme>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootDir in new[] { BHelper.BaseDir(Dir.BundledThemes), BHelper.ConfigDir(Dir.Themes) })
        {
            if (!Directory.Exists(rootDir)) continue;

            foreach (var themeDir in Directory.EnumerateDirectories(rootDir))
            {
                var th = await new IgTheme().LoadAsync(themeDir).ConfigureAwait(false);
                if (th.IsValid) found[th.FolderName] = th;
            }
        }

        return found.Values
            .OrderBy(t => t.Info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool IsBuiltInThemePack(IgTheme theme)
    {
        return BHelper.IsPathContainedIn(theme.FolderPath, BHelper.BaseDir(Dir.BundledThemes));
    }

    public static async Task<ThemePackInstallResult> InstallThemePacksAsync(IEnumerable<string> igThemeFilePaths)
    {
        var themesRoot = ThemePacksDir;
        Directory.CreateDirectory(themesRoot);

        return await Task.Run(() =>
        {
            var installed = 0;
            var incompatible = new List<string>();

            foreach (var file in igThemeFilePaths)
            {
                if (!File.Exists(file)) continue;

                if (InstallOneThemePack(file, themesRoot)) installed++;
                else incompatible.Add(GetThemePackFileName(file));
            }

            return new ThemePackInstallResult(installed, incompatible);
        }).ConfigureAwait(false);
    }

    public static bool UninstallThemePack(IgTheme theme)
    {
        if (IsBuiltInThemePack(theme)) return false;
        if (string.IsNullOrEmpty(theme.FolderPath) || !Directory.Exists(theme.FolderPath)) return false;

        try
        {
            Directory.Delete(theme.FolderPath, true);
            return true;
        }
        catch { }

        return false;
    }

    public async Task<bool> SaveAsync()
    {
        if (ConfigMode.PortableError is not null) return false;

        var jsonFilePath = BHelper.ConfigDir(CONFIG_USER);
        var jsonOptions = BHelper.CreateJsonOptions();
        var jsonContext = new ConfigJsonContext(jsonOptions);

        try
        {
            await BHelper.WriteJsonToFileAsync(jsonFilePath, this, jsonContext.Config);
            SavingException = null;
            return true;
        }
        catch (Exception ex)
        {
            SavingException = ex;
            System.Diagnostics.Debug.WriteLine($"[Config] Could not save '{jsonFilePath}': {ex.Message}");
            return false;
        }
    }

    public async Task LoadCurrentLanguageAsync()
    {
        var langPath = Lang.ResolveFilePath(Language);
        var isInvalid = !Lang.IsPackFileCompatible(langPath);

        if (isInvalid) langPath = string.Empty;

        var lang = new Lang(langPath);

        await lang.LoadAsync();

        Core.Lang = lang;
        Core.Config.Language = lang.FileName;
    }

    public async Task<bool> LoadCurrentThemeAsync(bool darkMode,
        bool useFallBackTheme, bool throwIfThemeInvalid, bool forceUpdateBackground)
    {
        var themeFolderName = darkMode ? DarkTheme : LightTheme;
        if (string.IsNullOrEmpty(themeFolderName))
        {
            themeFolderName = Const.DEFAULT_THEME;
        }

        if (themeFolderName.Equals(Core.Theme.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var th = await FindAndLoadThemePackAsync(themeFolderName, useFallBackTheme, throwIfThemeInvalid);

        if (darkMode) DarkTheme = th.FolderName;
        else LightTheme = th.FolderName;

        var hasStoredBg = HasValue(ConfigId.BackgroundColor) && !string.IsNullOrWhiteSpace(BackgroundColor);
        var currentBg = BHelper.ColorFromHex(BackgroundColor);
        var isThemePackBg = currentBg == BHelper.ColorFromHex(Core.Theme.Colors.BgColor);
        var isFollowingTheme = !hasStoredBg
            || (Core.Theme.IsValid && isThemePackBg)
            || currentBg == BHelper.ColorFromHex(th.Colors.BgColor);

        if (!isFollowingTheme && !forceUpdateBackground)
        {
            var otherName = darkMode ? LightTheme : DarkTheme;
            if (!string.IsNullOrEmpty(otherName)
                && !otherName.Equals(th.FolderName, StringComparison.OrdinalIgnoreCase))
            {
                var other = await FindAndLoadThemePackAsync(otherName, useFallBackTheme: false,
                    throwIfThemeInvalid: false);
                isFollowingTheme = other.IsValid && currentBg == BHelper.ColorFromHex(other.Colors.BgColor);
            }
        }

        if (isFollowingTheme || forceUpdateBackground)
        {
            BackgroundColor = th.Colors.BgColor;
        }

        var success = Core.SetTheme(th);
        return success;
    }

    private static async Task<IgTheme> FindAndLoadThemePackAsync(string themeFolderName,
        bool useFallBackTheme, bool throwIfThemeInvalid)
    {
        var themeConfigPath = BHelper.ConfigDir(Dir.Themes, themeFolderName);
        var th = await new IgTheme().LoadAsync(themeConfigPath);

        if (!th.IsValid)
        {
            var baseThemeConfigPath = BHelper.BaseDir(Dir.BundledThemes, themeFolderName);
            th = await new IgTheme().LoadAsync(baseThemeConfigPath);

            if (!th.IsValid && useFallBackTheme)
            {
                baseThemeConfigPath = BHelper.BaseDir(Dir.BundledThemes, Const.DEFAULT_THEME);
                th = await new IgTheme().LoadAsync(baseThemeConfigPath);
            }
        }

        if (!th.IsValid && throwIfThemeInvalid)
        {
            throw new ArgumentException($"IGE: Unable to load '{themeFolderName}' theme pack. " +
                $"Please make sure '{themeConfigPath}' file is valid.", nameof(themeFolderName));
        }

        return th;
    }

    public static LayoutPosition GetControlLayout(LayoutControl control)
    {
        var defaultPos = control == LayoutControl.Toolbar
            ? LayoutPosition.Top
            : LayoutPosition.Bottom;

        var pos = Core.Config.Layout.GetValueOrDefault(control, defaultPos);

        if (control == LayoutControl.Toolbar)
        {
            if (pos is LayoutPosition.Left or LayoutPosition.Right)
            {
                pos = LayoutPosition.Top;
            }
        }

        return pos;
    }

    private static readonly HashSet<string> _fileOnlyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Tools),
        nameof(PluginTrust),
    };

    private static string? ResolveConfigJsonPath(string primaryPath, string? fallbackPath = null)
    {
        return File.Exists(primaryPath) ? primaryPath
            : (!string.IsNullOrEmpty(fallbackPath) && File.Exists(fallbackPath)) ? fallbackPath
            : null;
    }

    private static JsonDocument? ReadConfigJsonDocument(string primaryPath, string? fallbackPath = null)
    {
        var path = ResolveConfigJsonPath(primaryPath, fallbackPath);
        if (string.IsNullOrEmpty(path)) return null;

        return BHelper.ReadJsonDocFromFile(path);
    }

    private static JsonDocument? ReadDeploymentConfigDocument(string configFileName)
    {
        var primaryPath = BHelper.BaseDir(configFileName);
        var fallbackPath = BHelper.ConfigDir(configFileName);

        return ReadConfigJsonDocument(primaryPath, fallbackPath);
    }

    private static JsonDocument? ReadDefaultConfigDocument() => ReadDeploymentConfigDocument(CONFIG_DEFAULT);

    private static JsonDocument? ReadAdminConfigDocument() => ReadDeploymentConfigDocument(CONFIG_ADMIN);

    private static Dictionary<string, string> ParseCliConfigArgs(string[]? args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (args == null) return result;

        foreach (var arg in args)
        {
            if (!arg.StartsWith(Const.CONFIG_CMD_PREFIX, StringComparison.OrdinalIgnoreCase))
                continue;

            var kvPart = arg[Const.CONFIG_CMD_PREFIX.Length..];
            var eqIdx = kvPart.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = kvPart[..eqIdx].Trim();
            var value = kvPart[(eqIdx + 1)..].Trim();
            if (key.Length == 0) continue;

            if (_fileOnlyKeys.Contains(key))
            {
                System.Diagnostics.Debug.WriteLine($"[Config] CLI override for '{key}' ignored (not allowed from the command line).");
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static byte[] MergeJsonLayers(
        JsonDocument? defaultDoc,
        JsonDocument? userDoc,
        Dictionary<string, string> rawOverrides,
        JsonDocument? adminDoc)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
        });

        writer.WriteStartObject();

        var defaultMap = IndexProperties(defaultDoc);
        var userMap = IndexProperties(userDoc);
        var adminMap = IndexProperties(adminDoc);

        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allKeys.UnionWith(defaultMap.Keys);
        allKeys.UnionWith(userMap.Keys);
        foreach (var k in rawOverrides.Keys) allKeys.Add(k);
        allKeys.UnionWith(adminMap.Keys);

        foreach (var key in allKeys)
        {
            if (adminMap.TryGetValue(key, out var adminVal))
            {
                writer.WritePropertyName(key);
                adminVal.WriteTo(writer);
            }
            else if (rawOverrides.TryGetValue(key, out var raw))
            {
                WriteRawValue(writer, key, raw);
            }
            else if (userMap.TryGetValue(key, out var userVal))
            {
                writer.WritePropertyName(key);
                userVal.WriteTo(writer);
            }
            else if (defaultMap.TryGetValue(key, out var defVal))
            {
                writer.WritePropertyName(key);
                defVal.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }

    private static Dictionary<string, JsonElement> IndexProperties(JsonDocument? doc)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (doc == null) return map;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            map.TryAdd(prop.Name, prop.Value);
        }

        return map;
    }

    private static void LoadAdminPolicies()
    {
        try
        {
            using var adminDoc = ReadAdminConfigDocument();
            var effectiveAdminDoc = IsCompatibleConfigLayer(adminDoc) ? adminDoc : null;

            AdminLockedConfigs = BuildAdminLockedConfigs(effectiveAdminDoc);
            LockedFeatures = BuildLockedFeatures(effectiveAdminDoc);
        }
        catch
        {
            AdminLockedConfigs = FrozenSet<ConfigId>.Empty;
            LockedFeatures = FrozenSet<string>.Empty;
        }
    }

    private static FrozenSet<ConfigId> BuildAdminLockedConfigs(JsonDocument? adminDoc)
    {
        if (adminDoc is null || adminDoc.RootElement.ValueKind != JsonValueKind.Object)
            return FrozenSet<ConfigId>.Empty;

        var ids = new HashSet<ConfigId>();
        foreach (var prop in adminDoc.RootElement.EnumerateObject())
        {
            if (Enum.TryParse<ConfigId>(prop.Name, ignoreCase: true, out var id)) ids.Add(id);
        }

        return ids.ToFrozenSet();
    }

    private static FrozenSet<string> BuildLockedFeatures(JsonDocument? adminDoc)
    {
        if (adminDoc is null || adminDoc.RootElement.ValueKind != JsonValueKind.Object)
            return FrozenSet<string>.Empty;

        if (!TryGetPropertyIgnoreCase(adminDoc.RootElement, nameof(LockedFeatures), out var value)
            || value.ValueKind != JsonValueKind.Array)
            return FrozenSet<string>.Empty;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;

            var name = item.GetString()?.Trim();
            if (!string.IsNullOrEmpty(name)) names.Add(name);
        }

        return names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteRawValue(Utf8JsonWriter writer, string key, string rawValue)
    {
        writer.WritePropertyName(key);

        try
        {
            using var doc = JsonDocument.Parse(rawValue);
            doc.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteStringValue(rawValue);
        }
    }

    private static bool InstallOneThemePack(string packageFile, string themesRoot)
    {
        var staging = Path.Combine(Path.GetTempPath(), "ig_theme_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(packageFile, staging, overwriteFiles: true);

            var srcDir = FindThemePackDir(staging);
            if (srcDir is null) return false;

            var theme = new IgTheme().Load(srcDir);
            if (!theme.IsValid || theme._Metadata.Version < IgTheme.SPEC_VERSION) return false;

            var folderName = string.Equals(srcDir, staging, StringComparison.OrdinalIgnoreCase)
                ? MakeSafeThemeFolderName(GetThemePackFileName(packageFile))
                : Path.GetFileName(srcDir);

            MoveThemeDirectory(srcDir, Path.Combine(themesRoot, folderName));
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static string? FindThemePackDir(string root)
    {
        if (File.Exists(Path.Combine(root, IgTheme.CONFIG_FILE))) return root;

        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            if (File.Exists(Path.Combine(sub, IgTheme.CONFIG_FILE))) return sub;
        }
        return null;
    }

    private static void MoveThemeDirectory(string src, string dest)
    {
        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);

        try
        {
            Directory.Move(src, dest);
            return;
        }
        catch { }

        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(src, dir)));
        }
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            File.Copy(f, Path.Combine(dest, Path.GetRelativePath(src, f)), overwrite: true);
        }
    }

    private static string GetThemePackFileName(string packageFile)
    {
        var name = Path.GetFileName(packageFile);
        const string suffix = ".igtheme.zip";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
        return name;
    }

    private static string MakeSafeThemeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
        }
        return new string(chars);
    }

}

public readonly record struct ThemePackInstallResult(
    int InstalledCount,
    IReadOnlyList<string> IncompatiblePackNames);
