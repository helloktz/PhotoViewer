using System;
using System.Collections.Generic;

namespace PhotoViewer.Common.ServiceProviders.FileSearchService;

public class FileSearchingEventArgs(IReadOnlyList<FileSearchEntry> entries) : EventArgs
{

    public IReadOnlyList<FileSearchEntry> Results { get; } = entries;

}
