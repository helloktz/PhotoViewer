using Microsoft.Win32;
using System;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PhotoViewer.Win32.Common;

public enum WallpaperStyle : int
{

    Current = -1,

    Fill,
    Fit,
    Stretch,
    Tile,
    Center,
    Span,
}

public static partial class Win32DesktopApi
{

    public static unsafe void SetWallpaper(string filePath, WallpaperStyle style)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
        if (key is null)
        {
            throw new InvalidOperationException($"IGE: Cannot open registry key: {key}");
        }

        (string bgStyle, string tileStyle) = style switch
        {
            WallpaperStyle.Fill => ("10", "0"),
            WallpaperStyle.Fit => ("6", "0"),
            WallpaperStyle.Stretch => ("2", "0"),
            WallpaperStyle.Tile => ("0", "1"),
            WallpaperStyle.Center => ("0", "0"),
            WallpaperStyle.Span => ("22", "0"),
            _ => ("-1", "0"),
        };

        if (bgStyle == "-1")
        {
            bgStyle = key.GetValue("WallpaperStyle")?.ToString() ?? "";
            tileStyle = key.GetValue("TileWallpaper")?.ToString() ?? "";
        }

        key.SetValue("WallpaperStyle", bgStyle);
        key.SetValue("TileWallpaper", tileStyle);
        key.SetValue("Wallpaper", filePath);

        fixed (char* pathPtr = filePath)
        {
            _ = PInvoke.SystemParametersInfo(
                SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETDESKWALLPAPER,
                0, pathPtr,
                SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE | SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDWININICHANGE);
        }
    }

}
