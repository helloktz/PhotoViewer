using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace PhotoViewer.Win32.Common;

public static class Win32PrintApi
{
    private const uint SEE_MASK_NOASYNC = 0x00000100;
    private const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    private const uint SEE_MASK_FLAG_LOG_USAGE = 0x04000000;

    public static unsafe void OpenPrintDialog(string filePath)
    {
        fixed (char* verbPtr = "print")
        fixed (char* filePtr = filePath)
        {
            var sei = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI | SEE_MASK_FLAG_LOG_USAGE,
                lpVerb = verbPtr,
                lpFile = filePtr,
                nShow = 1,
            };

            if (!PInvoke.ShellExecuteEx(ref sei))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }

}
