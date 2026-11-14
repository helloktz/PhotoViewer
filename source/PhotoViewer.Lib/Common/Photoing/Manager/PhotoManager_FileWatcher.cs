using D2Phap.FileWatcherEx;
using PhotoViewer.Common.ServiceProviders.FileSearchService;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PhotoViewer.Common.Photoing;

public class FileWatcherChangedEventArgs(
    ChangeType changeType,
    IReadOnlyList<string> filePaths,
    IReadOnlyList<string>? oldFilePaths = null,
    string? affectedCurrentFilePath = null) : EventArgs
{

    public ChangeType ChangeType => changeType;

    public IReadOnlyList<string> FilePaths => filePaths;

    public IReadOnlyList<string>? OldFilePaths => oldFilePaths;

    public string AffectedCurrentFilePath => affectedCurrentFilePath ?? string.Empty;
}

public partial class PhotoManager
{
    private FileSystemWatcherEx _fileWatcher = new();

    private readonly ConcurrentQueue<string> _deleteQueue = new();
    private Thread? _deleteWorkerThread;
    private volatile bool _deleteWorkerRunning;

    private readonly Action _processAddedFilesAction;
    private readonly Action _processChangedFilesAction;

    private readonly ConcurrentQueue<string> _pendingAdds = new();
    private readonly ConcurrentQueue<string> _pendingChanges = new();

    public event TEventHandler<PhotoManager, FileWatcherChangedEventArgs>? FileWatcherChanged;

    public string FileWatcherFolderPath => _fileWatcher.FolderPath;

    public void StartFileWatcher(string dirPath)
    {
        StopFileWatcher();

        if (string.IsNullOrWhiteSpace(dirPath)) return;

        _fileWatcher.FolderPath = dirPath;
        _fileWatcher.IncludeSubdirectories = Core.Config.EnableSubfoldersLoading;

        _fileWatcher.OnCreated += FileWatcher_OnCreated;
        _fileWatcher.OnDeleted += FileWatcher_OnDeleted;
        _fileWatcher.OnChanged += FileWatcher_OnChanged;
        _fileWatcher.OnRenamed += FileWatcher_OnRenamed;

        _deleteWorkerRunning = true;
        _deleteWorkerThread = new Thread(ProcessDeleteQueue)
        {
            Priority = ThreadPriority.BelowNormal,
            IsBackground = true,
            Name = "PhotoManager.DeleteWorker",
        };
        _deleteWorkerThread.Start();

        try
        {
            _fileWatcher.Start();
        }
        catch (ArgumentException)
        {
            _fileWatcher.Stop();
            _fileWatcher = new FileSystemWatcherEx
            {
                FolderPath = dirPath,
                IncludeSubdirectories = false,
            };

            _fileWatcher.OnCreated += FileWatcher_OnCreated;
            _fileWatcher.OnDeleted += FileWatcher_OnDeleted;
            _fileWatcher.OnChanged += FileWatcher_OnChanged;
            _fileWatcher.OnRenamed += FileWatcher_OnRenamed;

            _fileWatcher.Start();
        }
    }

    public void StopFileWatcher()
    {
        _fileWatcher.Stop();

        _fileWatcher.OnCreated -= FileWatcher_OnCreated;
        _fileWatcher.OnDeleted -= FileWatcher_OnDeleted;
        _fileWatcher.OnChanged -= FileWatcher_OnChanged;
        _fileWatcher.OnRenamed -= FileWatcher_OnRenamed;

        _deleteWorkerRunning = false;

        _fileWatcher = new FileSystemWatcherEx();
    }

    private void DisposeFileWatcher()
    {
        StopFileWatcher();
        _fileWatcher.Dispose();
    }

    private static bool IsSupportedFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return false;

        if (SaveStaging.IsStagingFile(filePath)) return false;

        return Core.GetSupportedFileExtensions().Contains(ext);
    }

    private bool IsExcludedFile(string? filePath)
    {
        return BHelper.IsPathSkippedUnderRoot(filePath, FileWatcherFolderPath,
            Core.Config.EnableHiddenImagesLoading);
    }

    private void FileWatcher_OnCreated(object? sender, FileChangedEvent e)
    {
        if (!IsSupportedFile(e.FullPath)) return;
        if (IsExcludedFile(e.FullPath)) return;
        if (IndexOf(e.FullPath) >= 0) return;

        _pendingAdds.Enqueue(e.FullPath);
        BHelper.Debounce(300, _processAddedFilesAction);
    }

    private void FileWatcher_OnDeleted(object? sender, FileChangedEvent e)
    {
        if (!IsSupportedFile(e.FullPath)) return;

        _deleteQueue.Enqueue(e.FullPath);
    }

    private void FileWatcher_OnChanged(object? sender, FileChangedEvent e)
    {
        if (!IsSupportedFile(e.FullPath)) return;

        if (IndexOf(e.FullPath) < 0)
        {
            if (IsExcludedFile(e.FullPath)) return;

            _pendingAdds.Enqueue(e.FullPath);
            BHelper.Debounce(300, _processAddedFilesAction);
            return;
        }

        _pendingChanges.Enqueue(e.FullPath);
        BHelper.Debounce(300, _processChangedFilesAction);
    }

    private void FileWatcher_OnRenamed(object? sender, FileChangedEvent e)
    {
        var newFilePath = e.FullPath ?? "";
        var oldFilePath = e.OldFullPath ?? "";

        if (SaveStaging.IsStagingFile(oldFilePath))
        {
            FileWatcher_OnChanged(sender, e);
            return;
        }

        var oldSupported = IsSupportedFile(oldFilePath);
        var newSupported = IsSupportedFile(newFilePath);

        if (!oldSupported && !newSupported) return;

        if (oldSupported && !newSupported)
        {
            _deleteQueue.Enqueue(oldFilePath);
            return;
        }

        var newExcluded = IsExcludedFile(newFilePath);

        if (!oldSupported && newSupported)
        {
            if (newExcluded) return;

            _pendingAdds.Enqueue(newFilePath);
            BHelper.Debounce(300, _processAddedFilesAction);
            return;
        }

        var imgIndex = IndexOf(oldFilePath);
        if (imgIndex >= 0)
        {
            if (newExcluded)
            {
                _deleteQueue.Enqueue(oldFilePath);
                return;
            }

            SetFilePath(imgIndex, newFilePath);

            FileWatcherChanged?.Invoke(this, new FileWatcherChangedEventArgs(
                ChangeType.RENAMED,
                [newFilePath],
                [oldFilePath]));
        }
        else if (!newExcluded)
        {
            _pendingAdds.Enqueue(newFilePath);
            BHelper.Debounce(300, _processAddedFilesAction);
        }
    }

    private void ProcessPendingAdds()
    {
        var newFilesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_pendingAdds.TryDequeue(out var path))
        {
            if (IndexOf(path) < 0)
            {
                newFilesSet.Add(path);
            }
        }

        if (newFilesSet.Count == 0) return;
        var newFiles = new List<string>(newFilesSet);

        var options = new FileSearchOptions()
        {
            AllowedExtensions = Core.GetSupportedFileExtensions(),
            UseExplorerSortOrder = Core.Config.EnableExplorerSortOrder,
            SearchSubDirectories = Core.Config.EnableSubfoldersLoading,
            GroupByDir = Core.Config.EnableImageFolderGrouping,
            IncludeHidden = Core.Config.EnableHiddenImagesLoading,
            OrderBy = Core.Config.ImageLoadingOrder,
            OrderType = Core.Config.ImageLoadingOrderType,
            ForegroundShell = Core.ShellProvider?.ForegroundShell,
        };

        List<string> currentPaths;
        string currentFilePath;
        int oldCurrentIndex;
        lock (_lock)
        {
            oldCurrentIndex = CurrentIndex;
            currentFilePath = CurrentFilePath;

            currentPaths = new List<string>(Items.Count);
            for (var i = 0; i < Items.Count; i++)
            {
                currentPaths.Add(Items[i].FilePath);
            }
        }

        var combinedList = new List<string>(currentPaths.Count + newFiles.Count);
        combinedList.AddRange(currentPaths);
        combinedList.AddRange(newFiles);

        var sortedList = FileSearchProvider.SortFiles(combinedList, options).ToList();

        foreach (var filePath in newFiles)
        {
            var insertIndex = sortedList.IndexOf(filePath);

            lock (_lock)
            {
                if (insertIndex < 0 || insertIndex > (int)Count)
                {
                    insertIndex = (int)Count;
                }
            }

            Add(filePath, insertIndex);
        }

        lock (_cacheLock)
        {
            _cachedIndexes.Clear();
        }

        _currentIndex = IndexOf(currentFilePath);
        var affectedCurrentFilePath = CurrentIndex != oldCurrentIndex ? currentFilePath : null;

        FileWatcherChanged?.Invoke(this, new FileWatcherChangedEventArgs(
            ChangeType.CREATED,
            newFiles,
            null,
            affectedCurrentFilePath));
    }

    private void ProcessPendingChanges()
    {
        var changedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_pendingChanges.TryDequeue(out var path))
        {
            changedSet.Add(path);
        }

        if (changedSet.Count == 0) return;
        var changedList = new List<string>(changedSet);

        foreach (var path in changedList)
        {
            InvalidateCacheAt(path);
            ThumbnailDiskCache.Invalidate(path);
        }

        string currentFilePath;
        lock (_lock)
        {
            currentFilePath = CurrentFilePath;
        }

        var currentWasChanged = changedList.Contains(currentFilePath, StringComparer.OrdinalIgnoreCase);
        var affectedCurrentFilePath = currentWasChanged ? currentFilePath : null;

        FileWatcherChanged?.Invoke(this, new FileWatcherChangedEventArgs(
            ChangeType.CHANGED,
            changedList,
            null,
            affectedCurrentFilePath));
    }

    private void ProcessDeleteQueue()
    {
        while (_deleteWorkerRunning)
        {
            if (_deleteQueue.IsEmpty)
            {
                Thread.Sleep(200);
                continue;
            }

            Thread.Sleep(100);

            var deletedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (_deleteQueue.TryDequeue(out var filePath))
            {
                deletedSet.Add(filePath);
            }
            var deletedList = new List<string>(deletedSet);

            if (deletedList.Count == 0) continue;

            string currentFilePath;
            int currentIndex;
            lock (_lock)
            {
                currentIndex = CurrentIndex;
                currentFilePath = CurrentFilePath;
            }

            var currentWasDeleted = deletedSet.Contains(currentFilePath);
            var affectedCurrentFilePath = currentWasDeleted ? currentFilePath : null;

            var deletedBeforeCurrent = 0;
            if (currentWasDeleted)
            {
                foreach (var filePath in deletedList)
                {
                    var index = IndexOf(filePath);
                    if (index >= 0 && index < currentIndex) deletedBeforeCurrent++;
                }
            }

            var removedAny = false;
            foreach (var filePath in deletedList)
            {
                if (IndexOf(filePath) < 0) continue;

                Remove(filePath);
                removedAny = true;
            }

            if (removedAny)
            {
                lock (_lock)
                {
                    _currentIndex = currentWasDeleted
                        ? Math.Min(currentIndex - deletedBeforeCurrent, (int)Count)
                        : IndexOf(currentFilePath);
                }

                lock (_cacheLock)
                {
                    _cachedIndexes.Clear();
                }
            }

            FileWatcherChanged?.Invoke(this, new FileWatcherChangedEventArgs(
                ChangeType.DELETED,
                deletedList,
                null,
                affectedCurrentFilePath));
        }
    }

}
