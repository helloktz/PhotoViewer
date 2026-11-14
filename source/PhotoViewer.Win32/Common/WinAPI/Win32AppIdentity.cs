using PhotoViewer.Common;
using System;
using System.IO;
using Windows.Win32;

namespace PhotoViewer.Win32.Common;

public static class Win32AppIdentity
{
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    private static readonly Lazy<bool> _isPackaged = new(DetectPackaged);
    private static readonly Lazy<bool> _isUnvirtualizedResources = new(DetectUnvirtualizedResources);

    public static bool IsPackaged => _isPackaged.Value;

    public static bool IsUnvirtualizedResources => _isUnvirtualizedResources.Value;

    private static bool DetectPackaged()
    {
        uint length = 0;
        return (int)PInvoke.GetCurrentPackageFullName(ref length, default) == ERROR_INSUFFICIENT_BUFFER;
    }

    private static bool DetectUnvirtualizedResources()
    {
        if (!_isPackaged.Value) return false;

        try
        {
            var manifest = Path.GetFullPath(BHelper.BaseDir("..", "AppxManifest.xml"));
            return File.Exists(manifest)
                && File.ReadAllText(manifest).Contains("unvirtualizedResources", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

}
