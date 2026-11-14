using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using PhotoViewer.Common.Photoing;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;

namespace PhotoViewer.Common.Types;

public static class Resx
{

    private static readonly FrozenDictionary<ResxId, string> _resxIdNameCache =
        Enum.GetValues<ResxId>().ToFrozenDictionary(v => v, v => Enum.GetName(v) ?? string.Empty);

    public static T Get<T>(ResxId resxId)
    {
        var resName = GetResxName(resxId);
        _ = App.Current!.TryGetResource(resName, out var value);

        return (T)value!;
    }

    public static Color GetBrushColor(ResxId resxId, Color fallback)
    {
        var brush = Get<IBrush?>(resxId) as ISolidColorBrush;

        return brush?.Color ?? fallback;
    }

    public static void Set(ResxId resxId, object resValue)
    {
        var resName = GetResxName(resxId);
        App.Current?.Resources[resName] = resValue;
    }

    public static string GetResxName(ResxId resxId)
    {
        return _resxIdNameCache.GetValueOrDefault(resxId, string.Empty);
    }

    public static DynamicResourceExtension CreateBinding(ResxId resxId)
    {
        var resName = GetResxName(resxId);
        return new DynamicResourceExtension(resName);
    }

    public static StreamGeometry? GetIcon(ResxIconId? id)
    {
        if (id is null) return null;

        var resName = Enum.GetName(id.Value) ?? string.Empty;
        return Application.Current is { } app && app.TryFindResource(resName, out var res)
            ? res as StreamGeometry
            : null;
    }

    public static Bitmap? GetStockIcon(StockIconId? id)
    {
        if (id is null) return null;

        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://PhotoViewer.Lib/Assets/{id}.png"));
            return Bitmap.DecodeToHeight(stream, 256);
        }
        catch { }

        return null;
    }

    public static SvgImage? GetSvg(ResxSvgId? id)
    {
        if (id is null) return null;

        try
        {
            var source = SvgSource.Load($"avares://PhotoViewer.Lib/Assets/Svg/{id}.svg", null, SvgCodec.LocalResources);
            if (source is not null) return new SvgImage { Source = source };
        }
        catch { }

        return null;
    }

    public static WindowIcon? GetDefaultWindowIcon()
    {
        try
        {
            using var stream = GetDefaultWindowIconAsStream();
            if (stream is null) return null;

            return new WindowIcon(stream);
        }
        catch { }

        return null;
    }

    public static Stream? GetDefaultWindowIconAsStream()
    {
        try
        {
            var stream = AssetLoader.Open(new Uri($"avares://PhotoViewer.Lib/Assets/icon256.ico"));
            return stream;
        }
        catch { }

        return null;
    }

}

public enum ResxId
{
    SystemAccentColor,
    SystemAccentColorLight1,
    SystemAccentColorLight2,
    SystemAccentColorLight3,
    SystemAccentColorDark1,
    SystemAccentColorDark2,
    SystemAccentColorDark3,
    IG_AccentBrush,

    AccentButtonForeground,
    AccentButtonForegroundPointerOver,
    AccentButtonForegroundPressed,
    AccentButtonForegroundDisabled,

    ControlCornerRadius,
    OverlayCornerRadius,
    ContentControlThemeFontFamily,

    IG_TextAccentColor,
    SystemControlForegroundBaseHighBrush,
    TextControlForeground,
    TextControlForegroundPointerOver,
    TextControlForegroundFocused,
    TextControlPlaceholderForeground,
    CheckBoxForegroundChecked,
    CheckBoxForegroundCheckedPointerOver,
    CheckBoxForegroundUnchecked,
    CheckBoxForegroundUncheckedPointerOver,

    TextControlBorderBrush,
    TextControlBorderBrushPointerOver,
    TextControlBorderBrushDisabled,
    ComboBoxBorderBrush,
    ComboBoxBorderBrushPointerOver,
    CheckBoxCheckBackgroundStrokeUnchecked,
    CheckBoxCheckBackgroundStrokeUncheckedPointerOver,

    MenuFlyoutPresenterBackground,
    MenuFlyoutPresenterBorderBrush,

    IG_MenuSeparatorBackground,
    MenuFlyoutItemBackground,
    MenuFlyoutItemBackgroundPointerOver,
    MenuFlyoutItemBackgroundPressed,

    MenuFlyoutItemForeground,
    MenuFlyoutItemForegroundPointerOver,
    MenuFlyoutItemForegroundPressed,
    MenuFlyoutItemForegroundDisabled,

    MenuFlyoutItemKeyboardAcceleratorTextForeground,
    MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver,
    MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed,
    MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled,

    MenuFlyoutSubItemChevron,
    MenuFlyoutSubItemChevronPointerOver,
    MenuFlyoutSubItemChevronPressed,
    MenuFlyoutSubItemChevronDisabled,
    MenuFlyoutSubItemChevronSubMenuOpened,

    ToolTipBackground,
    ToolTipForeground,
    ToolTipBorder,

    ComboBoxForeground,
    ComboBoxDropDownBackground,
    ComboBoxDropDownBorderBrush,

    ComboBoxItemForeground,
    ComboBoxItemForegroundPointerOver,
    ComboBoxItemForegroundPressed,
    ComboBoxItemForegroundDisabled,
    ComboBoxItemForegroundSelected,

    ComboBoxItemBackground,
    ComboBoxItemBackgroundPointerOver,
    ComboBoxItemBackgroundPressed,
    ComboBoxItemBackgroundSelected,

    IG_ThemeBackgroundBrush,
    IG_ViewerBackgroundBrush,
    IG_ToolHostBackgroundBrush,
    IG_ProjectorBackgroundBrush,
    IG_ThemeForegroundBrush,
    IG_ThemeToolbarBackgroundBrush,
    IG_ThemeGalleryBackgroundBrush,
    IG_ThemeMenuBackgroundBrush,

    IG_BackgroundInfoBrush,
    IG_BackgroundSuccessBrush,
    IG_BackgroundWarningBrush,
    IG_BackgroundDangerBrush,
    IG_BackgroundNeutralBrush,
    IG_BorderNeutralBrush,
    IG_BorderControlBrush,
    IG_OverlayBackgroundBrush,

    IG_TextSuccessBrush,
    IG_TextWarningBrush,
    IG_TextDangerBrush,

    IG_TextSuccessColor,
    IG_TextWarningColor,
    IG_TextDangerColor,

    IG_ToolButtonBackground,
    IG_ToolButtonBackgroundHover,
    IG_ToolButtonBackgroundPressed,
    IG_ToolButtonBackgroundChecked,
}

public enum ResxIconId
{
    IconEllipsis,
    IconClose,
    IconAdd,
    IconSearch,
    IconSettings,
    IconSave,
    IconSaveAs,
    IconCrop,
    IconCopy,
    IconReset,
    IconPrint,
    IconArrowPrevious,
    IconArrowNext,
    IconArrowLeft,
    IconArrowRight,
    IconChevronRight,
    IconChevronDown,
    IconPlay,
    IconPause,
    IconImageForward,
    IconLivePhoto,
    IconFolderOpen,
    IconEdit,
    IconIntegrated,
    IconInfo,
    IconVerify,
    IconPlaceholder,
    IconWeatherMoon,
    IconWeatherSunny,
    IconToolColorPicker,
    IconToolCropImage,
    IconToolFrameNav,
    IconToolHdrToneMapper,
    IconToolProjector,
    IconToolImageResizer,
    IconToolLosslessCompression,
}

public enum StockIconId
{
    Delete,
    Error,
    Find,
    Info,
    Lock,
    RecycleBin,
    Rename,
    Shield,
    Warning,
}

public enum ResxSvgId
{
    Cyclone,
    SmilingFaceWithSmilingEyes,
    StarStruck,
}
