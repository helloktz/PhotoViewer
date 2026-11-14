using PhotoViewer.Common.ServiceProviders;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace PhotoViewer.Win32.Common.ServiceProviders;

public class Win32PipeSecurityProvider : IPipeSecurityProvider
{

    public bool VerifyClientProcess(SafePipeHandle serverPipeHandle, int expectedProcessId)
    {
        if (serverPipeHandle is null || serverPipeHandle.IsInvalid) return true;

        try
        {
            if (!PInvoke.GetNamedPipeClientProcessId(serverPipeHandle, out var clientPid))
            {
                return true;
            }

            return clientPid == (uint)expectedProcessId;
        }
        catch
        {
            return true;
        }
    }
}
