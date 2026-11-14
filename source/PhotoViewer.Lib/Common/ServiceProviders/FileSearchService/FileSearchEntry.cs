using System;
using System.IO;

namespace PhotoViewer.Common.ServiceProviders.FileSearchService;

public sealed record FileSearchEntry(
    string FilePath,
    long FileSizeInBytes,
    DateTime FileCreationTimeUtc,
    DateTime FileLastWriteTimeUtc,
    DateTime FileLastAccessTimeUtc,
    FileAttributes Attributes)
{

    public static FileSearchEntry FromFileInfo(FileInfo file) => new(
        file.FullName,
        file.Length,
        file.CreationTimeUtc,
        file.LastWriteTimeUtc,
        file.LastAccessTimeUtc,
        file.Attributes);

    public static FileSearchEntry FromPath(string filePath) => FromFileInfo(new FileInfo(filePath));
}
