using PhotoViewer.Common.ServiceProviders.FileSearchService;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.Common.Photoing;

public partial class PhotoManager
{
    protected volatile int _currentIndex = -1;

    public int CurrentIndex => _currentIndex;

    public string CurrentFilePath => GetFilePath(CurrentIndex);

    public PhotoMetadata? CurrentMetadata => Get(CurrentIndex)?.Metadata;

    public Photo? Current => Get(CurrentIndex);

    public bool IsCurrentError => Current?.Error != null;

    public Photo? InitPhoto { get; set; } = null;

    public Photo? StartLoadingFiles(ICollection<string> paths, string? currentFilePath,
        FileSearchOptions searchOptions, Action<FileSearchingEventArgs> progressFn,
        bool reloadInitPhoto)
    {
        Core.FileSearchProvider.CancelSearching();

        var absPaths = paths.Select(BHelper.ResolvePath).ToList();
        currentFilePath = BHelper.ResolvePath(currentFilePath);

        var inputPaths = BHelper.GetDistinctDirsFromPaths(absPaths);

        Photo? preservedPhoto = null;
        if (!reloadInitPhoto)
        {
            preservedPhoto = Current;
        }

        Clear(excludeFromDisposal: preservedPhoto);
        DistinctDirs = inputPaths.DirPaths;

        var initFilePath = currentFilePath;
        if (string.IsNullOrWhiteSpace(initFilePath))
        {
            initFilePath = inputPaths.FilePaths.FirstOrDefault() ?? "";
        }

        if (preservedPhoto is not null
            && string.Equals(preservedPhoto.FilePath, initFilePath, StringComparison.OrdinalIgnoreCase))
        {
            InitPhoto = preservedPhoto;
        }
        else if (!string.IsNullOrWhiteSpace(initFilePath))
        {
            preservedPhoto?.Dispose();
            InitPhoto = new Photo(initFilePath);
        }

        _ = Core.FileSearchProvider.SearchAsync(DistinctDirs, searchOptions, progressFn);

        return InitPhoto;
    }

}
