
using System;
using System.Collections.Generic;
using System.Linq;
using PhotoViewer.Common.ServiceProviders.FileSearchService;

namespace PhotoViewer.Common.Photoing;

public partial class PhotoManager
{

    public void Add(string filePath, int index = -1)
    {
        Add([filePath], index);
    }

    public void Add(IEnumerable<string> filePaths, int index = -1)
    {
        var newItems = filePaths.Select(path => new Photo(path)).ToList();

        AddPhotos(newItems, index);
    }

    public void Add(IEnumerable<FileSearchEntry> entries, int index = -1)
    {
        var newItems = entries.Select(entry => new Photo(entry)).ToList();

        AddPhotos(newItems, index);
    }

    private void AddPhotos(List<Photo> newItems, int index)
    {
        lock (_lock)
        {
            var addedIndex = index;

            if (index < 0)
            {
                addedIndex = (int)Count;

                Items.AddRange(newItems);

                for (int i = addedIndex; i < Count; i++)
                {
                    _dict[Items[i].FilePath] = i;
                }
            }
            else
            {
                Items.InsertRange(index, newItems);

                for (int i = addedIndex; i < Count; i++)
                {
                    _dict[Items[i].FilePath] = i;
                }
            }
        }

        _ = OnPropertyChanged(nameof(Count));
    }

    public Photo? Get(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= Count) return null;
            return Items[index];
        }
    }

    public Photo? Get(string filePath)
    {
        lock (_lock)
        {
            var index = _dict.GetValueOrDefault(filePath, -1);
            return Get(index);
        }
    }

    public Photo? Select(string filePath)
    {
        var newSelectionIndex = IndexOf(filePath);

        return Select(newSelectionIndex);
    }

    public Photo? Select(int index)
    {
        Photo? result;

        lock (_lock)
        {
            if (0 <= CurrentIndex && CurrentIndex < Count)
            {
                Items[CurrentIndex].IsCurrent = false;
            }

            if (index < 0 || index >= Count) return null;

            Items[index].IsCurrent = true;
            _currentIndex = index;
            result = Items[index];
        }

        _ = OnPropertyChanged(nameof(Current));
        _ = OnPropertyChanged(nameof(CurrentIndex));
        _ = OnPropertyChanged(nameof(CurrentFilePath));
        _ = OnPropertyChanged(nameof(CurrentMetadata));

        return result;
    }

    public bool IsSelected(string filePath)
    {
        var index = IndexOf(filePath);

        return IsSelected(index);
    }

    public bool IsSelected(int index)
    {
        return Get(index)?.IsCurrent ?? false;
    }

    public string GetFilePath(int index)
    {
        return Get(index)?.FilePath ?? string.Empty;
    }

    public void SetFilePath(int index, string filePath)
    {
        bool isCurrent;

        lock (_lock)
        {
            var photo = Get(index);
            if (photo is null) return;

            var oldFilePath = photo.FilePath;
            photo.FilePath = filePath;

            if (!string.Equals(oldFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                _dict.TryRemove(oldFilePath, out _);
            }
            _dict[filePath] = index;

            isCurrent = index == CurrentIndex;
        }

        if (isCurrent)
        {
            _ = OnPropertyChanged(nameof(Current));
            _ = OnPropertyChanged(nameof(CurrentFilePath));
            _ = OnPropertyChanged(nameof(CurrentMetadata));
        }
    }

    public int IndexOf(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return -1;

        if (_dict.TryGetValue(filePath, out var photoIndex))
        {
            return photoIndex;
        }

        return -1;
    }

    public bool IsCached(int index)
    {
        var photo = Get(index);
        return photo?.State == PhotoState.Loaded;
    }

    public void Unload(int index)
    {
        Get(index)?.Unload();
    }

    public void Remove(int index)
    {
        var photo = Get(index);
        if (photo is null) return;

        Remove(photo.FilePath);
    }

    public void Remove(string filePath)
    {
        InvalidateCacheAt(filePath);

        bool isCurrentPhoto;
        Photo? removed;

        lock (_lock)
        {
            var index = IndexOf(filePath);
            if (index < 0) return;

            isCurrentPhoto = CurrentIndex == index;

            for (int i = index + 1; i < Count; i++)
            {
                var newIndex = i - 1;
                var itemPath = GetFilePath(i);
                _dict[itemPath] = newIndex;
            }

            removed = Items[index];

            _dict.Remove(filePath, out var _);
            Items.RemoveAt(index);
        }

        removed?.Dispose();

        _ = OnPropertyChanged(nameof(Count));

        if (isCurrentPhoto)
        {
            _ = OnPropertyChanged(nameof(Current));
            _ = OnPropertyChanged(nameof(CurrentFilePath));
            _ = OnPropertyChanged(nameof(CurrentMetadata));
        }
    }

    public void Clear(Photo? excludeFromDisposal = null)
    {
        ClearCache(excludeFromDisposal);

        Photo? initPhoto;
        Photo[] snapshot;

        lock (_lock)
        {
            initPhoto = InitPhoto;
            InitPhoto = null;
            _currentIndex = -1;

            snapshot = [.. Items];
            Items.Clear();
            _dict.Clear();
            DistinctDirs.Clear();
        }

        if (initPhoto is not null && !ReferenceEquals(initPhoto, excludeFromDisposal))
        {
            initPhoto.Dispose();
        }
        foreach (var item in snapshot)
        {
            if (ReferenceEquals(item, excludeFromDisposal)) continue;
            item.WithNoReactive(() => item.Dispose());
        }
    }

}
