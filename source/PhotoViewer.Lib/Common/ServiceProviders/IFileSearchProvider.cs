using PhotoViewer.Common.ServiceProviders.FileSearchService;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public interface IFileSearchProvider : IDisposable
{

    FileSearchOptions Options { get; }

    Task SearchAsync(IEnumerable<string> dirs, FileSearchOptions options, Action<FileSearchingEventArgs> progressFn);

    void CancelSearching();

}
