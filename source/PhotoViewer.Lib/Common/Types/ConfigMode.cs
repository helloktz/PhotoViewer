using System;
using System.IO;

namespace PhotoViewer.Common.Types;

public static class ConfigMode
{
    private static readonly (bool IsPortable, Exception? Error) _state = Resolve();

    public static bool IsPortable => _state.IsPortable;

    public static Exception? PortableError => _state.Error;

    public static string PortableMarkerPath => BHelper.BaseDir(Const.PORTABLE_MARKER_FILE);

    private static (bool IsPortable, Exception? Error) Resolve()
    {
        if (!File.Exists(PortableMarkerPath)) return (false, null);

        return (true, GetStartupDirAccessError());
    }

    private static Exception? GetStartupDirAccessError()
    {
        var probeName = $"{Const.PORTABLE_MARKER_FILE}.{Environment.ProcessId}.tmp";
        var probePath = BHelper.BaseDir(probeName);

        try
        {
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose)) { }
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

}
