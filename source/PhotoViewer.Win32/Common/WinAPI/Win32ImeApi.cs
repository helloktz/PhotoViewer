using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.Ime;

namespace PhotoViewer.Win32.Common;

public static class Win32ImeApi
{

    public static void DetachIme(nint wndHandle)
    {
        var hWnd = new HWND(wndHandle);
        if (hWnd.IsNull) return;

        _ = PInvoke.ImmAssociateContext(hWnd, HIMC.Null);
    }
}
