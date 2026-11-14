using Avalonia.Collections;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace PhotoViewer.Common.Photoing;

public partial class PhotoManager : PhDisposable
{
    protected AvaloniaList<Photo> _items = [];
    protected readonly ConcurrentDictionary<string, int> _dict = new(StringComparer.OrdinalIgnoreCase);
    protected readonly Lock _lock = new();

    public uint Count => (uint)Items.Count;

    public AvaloniaList<Photo> Items
    {
        get => _items;
        set
        {
            if (_items != value)
            {
                _items = value;
                _ = OnPropertyChanged();
                _ = OnPropertyChanged(nameof(Count));
            }
        }
    }

    public List<string> DistinctDirs { get; set; } = [];

    public PhotoManager(IEnumerable<string>? list = null)
    {
        _processAddedFilesAction = ProcessPendingAdds;
        _processChangedFilesAction = ProcessPendingChanges;

        if (list is not null) Add(list);
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        CancelCaching();
        DisposeFileWatcher();
        Clear();
    }

    public bool GetByStep(int step, bool loopBackNavigation, out Photo? outputPhoto)
    {
        outputPhoto = null;

        var newIndex = CurrentIndex + step;
        var safeIndex = BHelper.ComputeIndexInRange(newIndex, Count, loopBackNavigation);
        if (safeIndex == CurrentIndex) return false;

        outputPhoto = Select(safeIndex);

        return true;
    }

    public Photo? SelectReplacementOfCurrent(bool loopBackNavigation)
    {
        int targetIndex;

        lock (_lock)
        {
            if (Count == 0)
            {
                _currentIndex = -1;
                return null;
            }

            targetIndex = CurrentIndex;

            if (targetIndex >= Count)
            {
                targetIndex = loopBackNavigation ? 0 : (int)Count - 1;
            }
            else if (targetIndex < 0)
            {
                targetIndex = 0;
            }
        }

        return Select(targetIndex);
    }

}
