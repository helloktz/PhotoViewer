using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;

namespace PhotoViewer.Common.ServiceProviders.FileSearchService;

public class FileSearchOptions
{

    public IEnumerable<string>? AllowedExtensions { get; set; } = null;

    public ImageOrderBy OrderBy { get; set; } = ImageOrderBy.Name;

    public ImageOrderType OrderType { get; set; } = ImageOrderType.Asc;

    public StringComparison CompareMode { get; set; } = StringComparison.OrdinalIgnoreCase;

    public bool GroupByDir { get; set; } = true;

    public bool SearchSubDirectories { get; set; } = false;

    public bool IncludeHidden { get; set; } = false;

    public bool UseExplorerSortOrder { get; set; } = false;

    public object? ForegroundShell { get; set; } = null;

}
