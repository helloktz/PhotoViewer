using ImageGlass.SDK.Plugins;
using SkiaSharp;
using System;
using System.Diagnostics;

namespace PhotoViewer.Plugins;

internal sealed class PluginPixelBufferRelease
{

    public nint CodecApiPtr;

    public IGPixelBuffer Buffer;

    public string PluginId = string.Empty;

    public PluginLiveToken? LiveToken;

    private int _released;

    private long _pressureBytes;

    public static readonly SKImageRasterReleaseDelegate Release = OnRelease;

    public static readonly SKDataReleaseDelegate ReleaseData = OnRelease;

    private static unsafe void OnRelease(IntPtr pixels, object context)
    {
        if (context is PluginPixelBufferRelease carrier)
        {
            carrier.ReleaseFromHost();
        }
    }

    public void TrackNativeMemory(long byteCount)
    {
        if (byteCount <= 0 || _pressureBytes != 0) return;

        _pressureBytes = byteCount;
        GC.AddMemoryPressure(byteCount);
    }

    public unsafe void ReleaseFromHost()
    {
        if (System.Threading.Interlocked.Exchange(ref _released, 1) != 0) return;

        if (LiveToken is not null) LiveToken.RunIfAlive(FreeBuffer);
        else FreeBuffer();

        if (_pressureBytes > 0) GC.RemoveMemoryPressure(_pressureBytes);
    }

    private unsafe void FreeBuffer()
    {
        var apiPtr = CodecApiPtr;
        if (apiPtr == 0) return;

        try
        {
            var codecApi = (IGCodecApi*)apiPtr;
            if (!PluginAbi.HasEntryPoint(codecApi, &codecApi->FreePixelBuffer)) return;

            var localBuf = Buffer;
            codecApi->FreePixelBuffer(&localBuf);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginPixelBufferRelease] '{PluginId}' FreePixelBuffer threw: {ex.Message}");
        }
    }
}
