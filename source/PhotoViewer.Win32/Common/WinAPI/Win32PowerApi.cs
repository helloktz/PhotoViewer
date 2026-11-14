using Avalonia.Threading;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace PhotoViewer.Win32.Common;

public static class Win32PowerApi
{

    public static void PreventSleep()
    {
        SetExecutionState__(EXECUTION_STATE.ES_CONTINUOUS
            | EXECUTION_STATE.ES_SYSTEM_REQUIRED
            | EXECUTION_STATE.ES_DISPLAY_REQUIRED);
    }

    public static void AllowSleep()
    {
        SetExecutionState__(EXECUTION_STATE.ES_CONTINUOUS);
    }

    private static void SetExecutionState__(EXECUTION_STATE state)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = PInvoke.SetThreadExecutionState(state);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _ = PInvoke.SetThreadExecutionState(state));
        }
    }
}
