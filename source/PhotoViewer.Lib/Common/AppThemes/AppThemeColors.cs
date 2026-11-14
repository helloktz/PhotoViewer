using Avalonia.Media;
using PhotoViewer.Common.Extensions;

namespace PhotoViewer.Common.AppThemes;

public static class AppThemeColors
{
    public static ISolidColorBrush TextColorBrush { get; private set; } = Brushes.Transparent;
    public static ISolidColorBrush BgBrush { get; private set; } = Brushes.Transparent;
    public static ISolidColorBrush ToolbarBgBrush { get; private set; } = Brushes.Transparent;
    public static ISolidColorBrush GalleryBgBrush { get; private set; } = Brushes.Transparent;
    public static ISolidColorBrush MenuBgBrush { get; private set; } = Brushes.White;

    public static bool IsFullTransparent => AppThemeColors.BgBrush.A == 0
        && AppThemeColors.ToolbarBgBrush.A == 0
        && AppThemeColors.GalleryBgBrush.A == 0;

    public static Color BackgroundActivateDark { get; } = BHelper.ColorFromHex("#151b1f");
    public static Color BackgroundActivateLight { get; } = Colors.White;
    public static Color BackgroundInactivateDark { get; } = BHelper.ColorFromHex("#212121");
    public static Color BackgroundInactivateLight { get; } = BHelper.ColorFromHex("#f3f3f3");

    public static Color BackgroundInfoLight { get; } = BHelper.ColorFromHex("#C2DAECBB");
    public static Color BackgroundSuccessLight { get; } = BHelper.ColorFromHex("#DFF6DDBB");
    public static Color BackgroundWarningLight { get; } = BHelper.ColorFromHex("#FFF4CEBB");
    public static Color BackgroundDangerLight { get; } = BHelper.ColorFromHex("#FDE7E9BB");

    public static Color TextSuccessLight { get; } = BHelper.ColorFromHex("#0F7B0F");
    public static Color TextWarningLight { get; } = BHelper.ColorFromHex("#9D5D00");
    public static Color TextDangerLight { get; } = BHelper.ColorFromHex("#C42B1C");

    public static Color BackgroundInfoDark { get; } = BHelper.ColorFromHex("#1A3244BB");
    public static Color BackgroundSuccessDark { get; } = BHelper.ColorFromHex("#393D1BBB");
    public static Color BackgroundWarningDark { get; } = BHelper.ColorFromHex("#433519BB");
    public static Color BackgroundDangerDark { get; } = BHelper.ColorFromHex("#442726BB");

    public static Color TextSuccessDark { get; } = BHelper.ColorFromHex("#6CCB5F");
    public static Color TextWarningDark { get; } = BHelper.ColorFromHex("#FCE100");
    public static Color TextDangerDark { get; } = BHelper.ColorFromHex("#FF99A4");

    public static void Load(IgThemeColors colors, Color? accent = null)
    {
        var color = BHelper.ColorFromHex(colors.TextColor, accent);
        if (TextColorBrush.Color != color)
        {
            TextColorBrush = color.ToBrush();
        }
        color = BHelper.ColorFromHex(colors.BgColor, accent);
        if (BgBrush.Color != color)
        {
            BgBrush = color.ToBrush();
        }

        color = BHelper.ColorFromHex(colors.ToolbarBgColor, accent);
        if (ToolbarBgBrush.Color != color)
        {
            ToolbarBgBrush = color.ToBrush();
        }

        color = BHelper.ColorFromHex(colors.GalleryBgColor, accent);
        if (GalleryBgBrush.Color != color)
        {
            GalleryBgBrush = color.ToBrush();
        }

        color = BHelper.ColorFromHex(colors.MenuBgColor, accent);
        if (MenuBgBrush.Color != color)
        {
            MenuBgBrush = color.ToBrush();
        }
    }

}
