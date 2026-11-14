using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PhotoViewer.Win32.Common;

public static class Win32WindowApi
{
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int BLANK_ICON_SIZE = 16;
    private const int BLANK_ICON_BYTES = BLANK_ICON_SIZE * BLANK_ICON_SIZE / 8;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    private const int BUILD_DARK_TITLE_BAR = 17763;
    private const int BUILD_DARK_TITLE_BAR_MOVED = 18985;

    private static HICON _blankIcon;

    public static void SetWindowBackdrop(nint wndHandle, SystemBackdropType type = SystemBackdropType.Auto)
    {
        unsafe
        {
            _ = PInvoke.DwmSetWindowAttribute(new HWND(wndHandle),
               DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
               &type, sizeof(uint));
        }
    }

    public unsafe static void SetTitleBarDarkMode(nint wndHandle, bool isDark)
    {
        var hWnd = new HWND(wndHandle);
        if (hWnd.IsNull) return;

        var build = Environment.OSVersion.Version.Build;
        if (build < BUILD_DARK_TITLE_BAR) return;

        var attribute = build >= BUILD_DARK_TITLE_BAR_MOVED
            ? DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE
            : (DWMWINDOWATTRIBUTE)DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;

        var value = isDark ? 1 : 0;
        var result = PInvoke.DwmSetWindowAttribute(hWnd, attribute, &value, sizeof(int));
        if (result.Failed) return;

        RedrawTitleBar(hWnd);
    }

    private static void RedrawTitleBar(HWND hWnd)
    {
        nuint isActive = PInvoke.GetForegroundWindow() == hWnd ? 1u : 0u;

        _ = PInvoke.SendMessage(hWnd, PInvoke.WM_NCACTIVATE, isActive ^ 1u, 0);
        _ = PInvoke.SendMessage(hWnd, PInvoke.WM_NCACTIVATE, isActive, 0);
    }

    public unsafe static void SetTitleBarIconVisible(nint wndHandle, bool visible)
    {
        var hWnd = new HWND(wndHandle);
        if (hWnd.IsNull) return;

        var smallIcon = PInvoke.SendMessage(hWnd, PInvoke.WM_GETICON, ICON_SMALL, 0).Value;
        var isHidden = !_blankIcon.IsNull && smallIcon == (nint)_blankIcon.Value;

        var newIcon = visible
            ? (isHidden ? PInvoke.SendMessage(hWnd, PInvoke.WM_GETICON, ICON_BIG, 0).Value : smallIcon)
            : (nint)GetBlankIcon().Value;

        if (newIcon == smallIcon)
        {
            _ = PInvoke.SendMessage(hWnd, PInvoke.WM_SETICON, ICON_SMALL, 0);
        }

        _ = PInvoke.SendMessage(hWnd, PInvoke.WM_SETICON, ICON_SMALL, newIcon);
    }

    private static HICON GetBlankIcon()
    {
        if (!_blankIcon.IsNull) return _blankIcon;

        unsafe
        {
            var maskBits = stackalloc byte[BLANK_ICON_BYTES];
            var colorBits = stackalloc byte[BLANK_ICON_BYTES];
            for (var i = 0; i < BLANK_ICON_BYTES; i++)
            {
                maskBits[i] = 0xFF;
                colorBits[i] = 0x00;
            }

            var hMask = PInvoke.CreateBitmap(BLANK_ICON_SIZE, BLANK_ICON_SIZE, 1, 1, maskBits);
            var hColor = PInvoke.CreateBitmap(BLANK_ICON_SIZE, BLANK_ICON_SIZE, 1, 1, colorBits);

            try
            {
                var iconInfo = new ICONINFO
                {
                    fIcon = true,
                    hbmMask = hMask,
                    hbmColor = hColor,
                };

                _blankIcon = PInvoke.CreateIconIndirect(&iconInfo);
            }
            finally
            {
                _ = PInvoke.DeleteObject(hMask);
                _ = PInvoke.DeleteObject(hColor);
            }
        }

        return _blankIcon;
    }

}

public enum SystemBackdropType
{

    Auto = 0,

    None = 1,

    Mica = 2,

    Acrylic = 3,

    MicaAlt = 4,
}
