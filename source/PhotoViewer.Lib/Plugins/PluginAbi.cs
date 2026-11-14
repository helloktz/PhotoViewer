using ImageGlass.SDK.Plugins;

namespace PhotoViewer.Plugins;

internal static unsafe class PluginAbi
{

    internal static readonly int MinCodecApiSize = MeasureMinCodecApi();

    private static int MeasureMinCodecApi()
    {
        IGCodecApi probe = default;
        return (int)((byte*)&probe.FreePixelBuffer - (byte*)&probe) + sizeof(nint);
    }

    internal static bool HasEntryPoint(IGCodecApi* api, void* field)
    {
        if (api == null || field == null) return false;

        var fieldEnd = (byte*)field - (byte*)api + sizeof(nint);
        if (fieldEnd > api->StructSize) return false;

        return *(nint*)field != 0;
    }
}
