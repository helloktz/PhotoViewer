using PhotoViewer.Common.Types;
using System;

namespace PhotoViewer.Common;

public class ThemePackChangedEventArgs(string propName = "") : EventArgs
{

    public string PropertyName => propName;
}

public class PhotoUnloadedEventArgs : EventArgs
{
    public bool IsClipboardPhoto { get; set; } = false;
    public int Index { get; set; } = -1;
    public string FilePath { get; set; } = string.Empty;
}

public class DestColorProfileChangedEventArgs(bool requiresPhotoReload) : EventArgs
{

    public bool RequiresPhotoReload { get; init; } = requiresPhotoReload;
}

public class PhotoSaveEventArgs(string srcFilePath, string destFilePath, ImageSaveSource saveSource) : EventArgs
{
    public string SrcFilePath { get; init; } = srcFilePath;
    public string DestFilePath { get; init; } = destFilePath;
    public bool IsSaveAsNewFile => !SrcFilePath.Equals(DestFilePath, StringComparison.OrdinalIgnoreCase);
    public ImageSaveSource SaveSource { get; init; } = saveSource;
}
