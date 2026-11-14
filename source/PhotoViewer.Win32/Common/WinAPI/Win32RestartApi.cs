using System;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PhotoViewer.Win32.Common.WinAPI;

internal static class Win32RestartApi
{

    internal static unsafe bool RegisterForRestart()
    {
        try
        {
            var hr = PInvoke.RegisterApplicationRestart(null, 0);
            return hr.Succeeded;
        }
        catch { return false; }
    }
}
