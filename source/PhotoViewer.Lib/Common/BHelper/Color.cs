using Avalonia.Media;
using PhotoViewer.Common.Types;
using System;
using System.Globalization;

namespace PhotoViewer.Common;

public partial class BHelper
{

    public static Color ColorFromHex(string colorStr, Color? accent = null, bool skipAlpha = false)
    {
        if (!colorStr.StartsWith(Const.THEME_SYSTEM_ACCENT, StringComparison.OrdinalIgnoreCase))
        {
            return ColorFromHex(colorStr, skipAlpha);
        }

        var valueArr = colorStr.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte accentR = 0;
        byte accentG = 0;
        byte accentB = 0;
        byte accentA = 255;

        if (accent != null)
        {
            accentR = accent.Value.R;
            accentG = accent.Value.G;
            accentB = accent.Value.B;
            accentA = accent.Value.A;
        }

        if (!skipAlpha && valueArr.Length > 1)
        {
            _ = byte.TryParse(valueArr[1], out byte accentAlpha);
            accentA = accentAlpha;
        }

        return Color.FromArgb(accentA, accentR, accentG, accentB);

    }

    public static Color ColorFromHex(string hex, bool skipAlpha)
    {
        if (string.IsNullOrWhiteSpace(hex)) return new Color();

        byte colorR = 0;
        byte colorG = 0;
        byte colorB = 0;
        byte colorA = 0;

        hex = hex.TrimStart('#');

        try
        {
            if (hex.Length == 8)
            {
                colorR = byte.Parse(hex.AsSpan(0, 2), NumberStyles.AllowHexSpecifier);
                colorG = byte.Parse(hex.AsSpan(2, 2), NumberStyles.AllowHexSpecifier);
                colorB = byte.Parse(hex.AsSpan(4, 2), NumberStyles.AllowHexSpecifier);
                colorA = byte.Parse(hex.AsSpan(6, 2), NumberStyles.AllowHexSpecifier);
            }
            else if (hex.Length == 6)
            {
                colorR = byte.Parse(hex.AsSpan(0, 2), NumberStyles.AllowHexSpecifier);
                colorG = byte.Parse(hex.AsSpan(2, 2), NumberStyles.AllowHexSpecifier);
                colorB = byte.Parse(hex.AsSpan(4, 2), NumberStyles.AllowHexSpecifier);
                colorA = 255;
            }
            else if (hex.Length == 4)
            {
                colorR = byte.Parse($"{hex[0]}{hex[0]}", NumberStyles.AllowHexSpecifier);
                colorG = byte.Parse($"{hex[1]}{hex[1]}", NumberStyles.AllowHexSpecifier);
                colorB = byte.Parse($"{hex[2]}{hex[2]}", NumberStyles.AllowHexSpecifier);
                colorA = byte.Parse($"{hex[3]}{hex[3]}", NumberStyles.AllowHexSpecifier);
            }
            else if (hex.Length == 3)
            {
                colorR = byte.Parse($"{hex[0]}{hex[0]}", NumberStyles.AllowHexSpecifier);
                colorG = byte.Parse($"{hex[1]}{hex[1]}", NumberStyles.AllowHexSpecifier);
                colorB = byte.Parse($"{hex[2]}{hex[2]}", NumberStyles.AllowHexSpecifier);
                colorA = 255;
            }
            else
            {
                return new Color();
            }

            if (skipAlpha) colorA = 255;
        }
        catch { }

        return Color.FromArgb(colorA, colorR, colorG, colorB);
    }

}
