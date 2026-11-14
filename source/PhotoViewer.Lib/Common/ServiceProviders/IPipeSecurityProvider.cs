using Microsoft.Win32.SafeHandles;

namespace PhotoViewer.Common.ServiceProviders;

public interface IPipeSecurityProvider
{

    bool VerifyClientProcess(SafePipeHandle serverPipeHandle, int expectedProcessId);
}
