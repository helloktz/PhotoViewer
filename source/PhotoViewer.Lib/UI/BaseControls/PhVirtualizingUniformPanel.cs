using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace PhotoViewer.UI;

public class PhVirtualizingUniformPanel : VirtualizingPanel, IScrollSnapPointsInfo
{
    private readonly List<RealizedElement> _realizedElements = [];
    private readonly Stack<Control> _recyclePool = new();

    private int _columnsPerRow = 1;
    private int _totalRows;
    private double _itemWidth;
    private double _itemHeight;

    private ScrollViewer? _scrollViewer;
    private Vector _lastViewport;
    private double _lastAvailableWidth;
    private bool _isAttached;

    public PhVirtualizingUniformPanelViewMode ViewMode
    {
        get => GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }
    public static readonly StyledProperty<PhVirtualizingUniformPanelViewMode> ViewModeProperty =
        AvaloniaProperty.Register<PhVirtualizingUniformPanel, PhVirtualizingUniformPanelViewMode>(nameof(ViewMode), PhVirtualizingUniformPanelViewMode.FilmStrip);

    public double ItemSize
    {
        get => GetValue(ItemSizeProperty);
        set => SetValue(ItemSizeProperty, value);
    }
    public static readonly StyledProperty<double> ItemSizeProperty =
        AvaloniaProperty.Register<PhVirtualizingUniformPanel, double>(nameof(ItemSize), 80d);

    public Thickness ItemMargin
    {
        get => GetValue(ItemMarginProperty);
        set => SetValue(ItemMarginProperty, value);
    }
    public static readonly StyledProperty<Thickness> ItemMarginProperty =
        AvaloniaProperty.Register<PhVirtualizingUniformPanel, Thickness>(nameof(ItemMargin));

    public int ColumnsPerRow => _columnsPerRow;

    public int TotalRows => _totalRows;

    public bool AreHorizontalSnapPointsRegular
    {
        get => ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip;
        set { }
    }

    public bool AreVerticalSnapPointsRegular
    {
        get => ViewMode == PhVirtualizingUniformPanelViewMode.Gallery;
        set { }
    }

#pragma warning disable CS0067
    public event EventHandler<RoutedEventArgs>? HorizontalSnapPointsChanged;
    public event EventHandler<RoutedEventArgs>? VerticalSnapPointsChanged;
#pragma warning restore CS0067

    public IReadOnlyList<double> GetIrregularSnapPoints(Orientation orientation, SnapPointsAlignment snapPointsAlignment) => [];

    public double GetRegularSnapPoints(Orientation orientation, SnapPointsAlignment snapPointsAlignment, out double offset)
    {
        offset = 0;

        if (ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip && orientation == Orientation.Horizontal)
            return _itemWidth;
        if (ViewMode == PhVirtualizingUniformPanelViewMode.Gallery && orientation == Orientation.Vertical)
            return _itemHeight;

        return 0;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ViewModeProperty
            || change.Property == ItemSizeProperty
            || change.Property == ItemMarginProperty)
        {
            RecycleAllElements();
            InvalidateMeasure();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        EffectiveViewportChanged += OnEffectiveViewportChanged;

        if (_scrollViewer is not null)
        {
            _scrollViewer.PropertyChanged += OnScrollViewerPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        EffectiveViewportChanged -= OnEffectiveViewportChanged;

        if (_scrollViewer is not null)
        {
            _scrollViewer.PropertyChanged -= OnScrollViewerPropertyChanged;
            _scrollViewer = null;
        }

        RecycleAllElements();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);
        RecycleAllElements();
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items;
        if (items is null) return default;

        var itemCount = items.Count;
        if (itemCount == 0)
        {
            RecycleAllElements();
            return default;
        }

        var margin = ItemMargin;
        _itemWidth = ItemSize + margin.Left + margin.Right;
        _itemHeight = ItemSize + margin.Top + margin.Bottom;

        if (ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip)
        {
            return MeasureFilmStrip(availableSize, items, itemCount);
        }
        else
        {
            return MeasureGallery(availableSize, items, itemCount);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip)
        {
            return ArrangeFilmStrip(finalSize);
        }
        else
        {
            return ArrangeGallery(finalSize);
        }
    }

    protected override Control? ContainerFromIndex(int index)
    {
        for (var i = 0; i < _realizedElements.Count; i++)
        {
            if (_realizedElements[i].Index == index)
                return _realizedElements[i].Element;
        }
        return null;
    }

    protected override int IndexFromContainer(Control container)
    {
        for (var i = 0; i < _realizedElements.Count; i++)
        {
            if (_realizedElements[i].Element == container)
                return _realizedElements[i].Index;
        }
        return -1;
    }

    protected override IEnumerable<Control> GetRealizedContainers()
    {
        for (var i = 0; i < _realizedElements.Count; i++)
        {
            yield return _realizedElements[i].Element;
        }
    }

    protected override Control ScrollIntoView(int index)
    {
        var items = Items;
        if (items is null || index < 0 || index >= items.Count)
            return null!;

        var element = ContainerFromIndex(index);
        if (element is null)
        {
            element = GetOrCreateElement(items, index);
            element.Measure(new Size(_itemWidth, _itemHeight));
        }

        if (_scrollViewer is not null)
        {
            if (ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip)
            {
                var x = index * _itemWidth;
                var viewMid = _scrollViewer.Viewport.Width / 2;
                var target = x + _itemWidth / 2 - viewMid;
                var maxX = Math.Max(0, _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width);
                _scrollViewer.Offset = new Vector(Math.Clamp(target, 0, maxX), 0);
            }
            else
            {
                var row = _columnsPerRow > 0 ? index / _columnsPerRow : 0;
                var y = row * _itemHeight;
                var viewMid = _scrollViewer.Viewport.Height / 2;
                var target = y + _itemHeight / 2 - viewMid;
                var maxY = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
                _scrollViewer.Offset = new Vector(0, Math.Clamp(target, 0, maxY));
            }

            InvalidateMeasure();
        }

        return element;
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        if (from is not Control fromControl) return null;
        var fromIndex = IndexFromContainer(fromControl);
        if (fromIndex < 0) return null;

        var itemCount = Items.Count;
        var targetIndex = -1;

        if (ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip)
        {
            targetIndex = direction switch
            {
                NavigationDirection.Left or NavigationDirection.Previous => fromIndex - 1,
                NavigationDirection.Right or NavigationDirection.Next => fromIndex + 1,
                _ => -1,
            };
        }
        else
        {
            targetIndex = direction switch
            {
                NavigationDirection.Left or NavigationDirection.Previous => fromIndex - 1,
                NavigationDirection.Right or NavigationDirection.Next => fromIndex + 1,
                NavigationDirection.Up => fromIndex - _columnsPerRow,
                NavigationDirection.Down => fromIndex + _columnsPerRow,
                _ => -1,
            };
        }

        if (targetIndex < 0 || targetIndex >= itemCount)
        {
            if (wrap)
            {
                targetIndex = (targetIndex % itemCount + itemCount) % itemCount;
            }
            else
            {
                return null;
            }
        }

        return ContainerFromIndex(targetIndex) ?? ScrollIntoView(targetIndex);
    }

    private Size MeasureFilmStrip(Size availableSize, IReadOnlyList<object?> items, int itemCount)
    {
        _columnsPerRow = itemCount;
        _totalRows = 1;

        double viewportStart = 0;
        double viewportEnd = availableSize.Width;
        if (_scrollViewer is not null)
        {
            viewportStart = _scrollViewer.Offset.X;
            viewportEnd = viewportStart + _scrollViewer.Viewport.Width;
        }

        var bufferSize = viewportEnd - viewportStart;
        var extStart = Math.Max(0, viewportStart - bufferSize);
        var extEnd = viewportEnd + bufferSize;

        var firstVisible = Math.Max(0, (int)(viewportStart / _itemWidth));
        var lastVisible = Math.Min(itemCount - 1, (int)(viewportEnd / _itemWidth));
        var firstRealized = Math.Max(0, (int)(extStart / _itemWidth));
        var lastRealized = Math.Min(itemCount - 1, (int)(extEnd / _itemWidth));

        RealizeRange(items, firstRealized, lastRealized, firstVisible, lastVisible);

        var constraint = new Size(_itemWidth, _itemHeight);
        for (var i = 0; i < _realizedElements.Count; i++)
        {
            _realizedElements[i].Element.Measure(constraint);
        }

        return new Size(itemCount * _itemWidth, _itemHeight);
    }

    private Size ArrangeFilmStrip(Size finalSize)
    {
        for (var i = 0; i < _realizedElements.Count; i++)
        {
            ref var re = ref CollectionsMarshalHelper.GetRef(_realizedElements, i);
            var x = re.Index * _itemWidth;
            re.Element.Arrange(new Rect(x, 0, _itemWidth, _itemHeight));
        }

        return finalSize;
    }

    private Size MeasureGallery(Size availableSize, IReadOnlyList<object?> items, int itemCount)
    {
        var availWidth = !double.IsInfinity(availableSize.Width)
            ? availableSize.Width
            : (_scrollViewer is not null && _scrollViewer.Viewport.Width > 0
                ? _scrollViewer.Viewport.Width
                : 800);

        _columnsPerRow = Math.Max(1, (int)(availWidth / _itemWidth));
        _totalRows = (itemCount + _columnsPerRow - 1) / _columnsPerRow;

        var totalHeight = _totalRows * _itemHeight;

        double viewportStart = 0;
        double viewportEnd = availableSize.Height;
        if (_scrollViewer is not null)
        {
            viewportStart = _scrollViewer.Offset.Y;
            viewportEnd = viewportStart + _scrollViewer.Viewport.Height;
        }

        var bufferSize = Math.Max(1, viewportEnd - viewportStart);
        var extStart = Math.Max(0, viewportStart - bufferSize);
        var extEnd = viewportEnd + bufferSize;

        var firstVisibleRow = Math.Max(0, (int)(viewportStart / _itemHeight));
        var lastVisibleRow = Math.Min(_totalRows - 1, (int)(viewportEnd / _itemHeight));
        var firstRealizedRow = Math.Max(0, (int)(extStart / _itemHeight));
        var lastRealizedRow = Math.Min(_totalRows - 1, (int)(extEnd / _itemHeight));

        var firstVisible = firstVisibleRow * _columnsPerRow;
        var lastVisible = Math.Min(itemCount - 1, (lastVisibleRow + 1) * _columnsPerRow - 1);
        var firstRealized = firstRealizedRow * _columnsPerRow;
        var lastRealized = Math.Min(itemCount - 1, (lastRealizedRow + 1) * _columnsPerRow - 1);

        RealizeRange(items, firstRealized, lastRealized, firstVisible, lastVisible);

        var constraint = new Size(_itemWidth, _itemHeight);
        for (var i = 0; i < _realizedElements.Count; i++)
        {
            _realizedElements[i].Element.Measure(constraint);
        }

        var desiredWidth = Math.Min(_columnsPerRow * _itemWidth, availWidth);

        return new Size(desiredWidth, totalHeight);
    }

    private Size ArrangeGallery(Size finalSize)
    {
        for (var i = 0; i < _realizedElements.Count; i++)
        {
            ref var re = ref CollectionsMarshalHelper.GetRef(_realizedElements, i);
            var row = re.Index / _columnsPerRow;
            var col = re.Index % _columnsPerRow;
            var x = col * _itemWidth;
            var y = row * _itemHeight;

            re.Element.Arrange(new Rect(x, y, _itemWidth, _itemHeight));
        }

        return finalSize;
    }

    private void RealizeRange(IReadOnlyList<object?> items, int firstIndex, int lastIndex,
        int visibleFirstIndex, int visibleLastIndex)
    {
        for (var i = _realizedElements.Count - 1; i >= 0; i--)
        {
            var idx = _realizedElements[i].Index;
            if (idx < firstIndex || idx > lastIndex)
            {
                RecycleElement(_realizedElements[i]);
                _realizedElements.RemoveAt(i);
            }
        }

        var realizedIndices = new HashSet<int>(_realizedElements.Count);
        for (var i = 0; i < _realizedElements.Count; i++)
        {
            realizedIndices.Add(_realizedElements[i].Index);
        }

        void RealizeMissingRange(int start, int end)
        {
            for (var idx = start; idx <= end; idx++)
            {
                if (idx < 0 || idx >= items.Count || realizedIndices.Contains(idx)) continue;

                var element = GetOrCreateElement(items, idx);
                _realizedElements.Add(new RealizedElement(idx, element));
            }
        }

        RealizeMissingRange(visibleFirstIndex, visibleLastIndex);
        RealizeMissingRange(firstIndex, visibleFirstIndex - 1);
        RealizeMissingRange(visibleLastIndex + 1, lastIndex);
    }

    private Control GetOrCreateElement(IReadOnlyList<object?> items, int index)
    {
        var item = items[index]!;
        var generator = ItemContainerGenerator!;

        if (_recyclePool.TryPop(out var recycled))
        {
            generator.PrepareItemContainer(recycled, item, index);
            generator.ItemContainerPrepared(recycled, item, index);

            if (!Children.Contains(recycled))
            {
                AddInternalChild(recycled);
            }
            recycled.IsVisible = true;

            return recycled;
        }

        var needsContainer = generator.NeedsContainer(item, index, out var recycleKey);
        Control element;

        if (needsContainer)
        {
            element = generator.CreateContainer(item, index, recycleKey);
            generator.PrepareItemContainer(element, item, index);
            generator.ItemContainerPrepared(element, item, index);
        }
        else
        {
            element = (Control)item;
            generator.PrepareItemContainer(element, item, index);
            generator.ItemContainerPrepared(element, item, index);
        }

        AddInternalChild(element);
        return element;
    }

    private void RecycleElement(RealizedElement re)
    {
        var element = re.Element;
        ItemContainerGenerator?.ClearItemContainer(element);
        element.IsVisible = false;
        _recyclePool.Push(element);
    }

    private void RecycleAllElements()
    {
        for (var i = _realizedElements.Count - 1; i >= 0; i--)
        {
            RecycleElement(_realizedElements[i]);
        }
        _realizedElements.Clear();
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (!_isAttached) return;

        var newViewport = new Vector(e.EffectiveViewport.X, e.EffectiveViewport.Y);
        if (newViewport != _lastViewport)
        {
            _lastViewport = newViewport;
            InvalidateMeasure();
        }
    }

    private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_isAttached) return;
        if (e.Property != ScrollViewer.ViewportProperty) return;
        if (ViewMode != PhVirtualizingUniformPanelViewMode.Gallery) return;

        var newViewport = (Size)e.NewValue!;
        if (Math.Abs(newViewport.Width - _lastAvailableWidth) > 0.5)
        {
            _lastAvailableWidth = newViewport.Width;
            InvalidateMeasure();
        }
    }

}

public enum PhVirtualizingUniformPanelViewMode
{

    Gallery,

    FilmStrip,
}

record struct RealizedElement(int Index, Control Element);

static class CollectionsMarshalHelper
{
    public static ref RealizedElement GetRef(List<RealizedElement> list, int index)
    {
        return ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list)[index];
    }
}
