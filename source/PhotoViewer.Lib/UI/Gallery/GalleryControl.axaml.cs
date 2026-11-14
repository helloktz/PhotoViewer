using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoViewer.Common;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.UI;

public partial class GalleryControl : PhControl
{
    private const int TOOLTIP_RESUME_DELAY_MS = 250;

    private CancellationTokenSource? _cancelScrollAnimation;
    private ScrollViewer? _cachedScrollViewerEl;
    private DispatcherTimer? _tooltipResumeTimer;
    public static readonly Thickness GalleryItemMargin = new(1);
    public static readonly Thickness GalleryPadding = new(4, 4, 4, 8);

    public event TEventHandler<GalleryItem, GalleryItemClickEventArgs>? ItemClicked;

    public IEnumerable<Photo> ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
    public static readonly StyledProperty<IEnumerable<Photo>> ItemsSourceProperty =
        AvaloniaProperty.Register<GalleryControl, IEnumerable<Photo>>(nameof(ItemsSource), []);

    public PhVirtualizingUniformPanelViewMode ViewMode
    {
        get => GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }
    public static readonly StyledProperty<PhVirtualizingUniformPanelViewMode> ViewModeProperty =
        AvaloniaProperty.Register<GalleryControl, PhVirtualizingUniformPanelViewMode>(nameof(ViewMode), PhVirtualizingUniformPanelViewMode.FilmStrip);

    public PlacementMode ItemTooltipPlacement
    {
        get => GetValue(ItemTooltipPlacementProperty);
        set => SetValue(ItemTooltipPlacementProperty, value);
    }
    public static readonly StyledProperty<PlacementMode> ItemTooltipPlacementProperty =
        AvaloniaProperty.Register<GalleryControl, PlacementMode>(nameof(ItemTooltipPlacement), PlacementMode.Pointer);

    public Size MinContentSize
    {
        get
        {
            var totalItemWidth = CalculateWidthForGalleryView(1);
            var totalItemHeight = Core.Config.ThumbnailSize
                + GalleryItemMargin.Top + GalleryItemMargin.Bottom
                + GalleryPadding.Top + GalleryPadding.Bottom;

            if (ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip)
            {
                return new(0, totalItemHeight);
            }

            return new(totalItemWidth, 0);
        }
    }
    public static readonly DirectProperty<GalleryControl, Size> MinContentSizeProperty =
        AvaloniaProperty.RegisterDirect<GalleryControl, Size>(nameof(MinContentSize), i => i.MinContentSize);

    public GalleryControl()
    {
        InitializeComponent();

        AddHandler(PointerWheelChangedEvent, GalleryControl_PointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(ScrollViewer.ScrollChangedEvent, GalleryControl_ScrollChanged);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        UpdateReservedSize();
        Core.Config.PropertyChanged += Config_PropertyChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        _cachedScrollViewerEl = null;
        Core.Config.PropertyChanged -= Config_PropertyChanged;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        ScrollToItem(Core.Photos.CurrentIndex, false);
    }

    protected override void OnIgDpiChanged()
    {
        base.OnIgDpiChanged();
        RefreshRealizedThumbnails();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == ViewModeProperty)
        {
            RaisePropertyChanged(MinContentSizeProperty, default, MinContentSize);
            UpdateReservedSize();
        }
        else if (e.Property == IsContentVisibleProperty)
        {
            RefreshRealizedThumbnails();
        }
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Config.ThumbnailSize))
        {
            RaisePropertyChanged(MinContentSizeProperty, default, MinContentSize);
            UpdateReservedSize();
            RefreshRealizedThumbnails();
        }
    }

    private void GalleryControl_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.X == 0 && e.Delta.Y == 0) return;

        var svEl = FindScrollViewer();
        if (svEl is null) return;

        var cellSize = GetCellSize();

        if (ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip)
        {
            if (svEl.Extent.Width <= svEl.Viewport.Width) return;

            var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            var step = GetWheelScrollStep(cellSize.Width, svEl.Viewport.Width);
            var maxOffset = Math.Max(0, svEl.Extent.Width - svEl.Viewport.Width);

            var targetX = svEl.Offset.X - (delta * step);
            svEl.Offset = new Vector(Math.Clamp(targetX, 0, maxOffset), 0);
        }
        else
        {
            if (svEl.Extent.Height <= svEl.Viewport.Height) return;

            var step = GetWheelScrollStep(cellSize.Height, svEl.Viewport.Height);
            var maxOffset = Math.Max(0, svEl.Extent.Height - svEl.Viewport.Height);

            var targetY = svEl.Offset.Y - (e.Delta.Y * step);
            svEl.Offset = new Vector(svEl.Offset.X, Math.Clamp(targetY, 0, maxOffset));
        }

        e.Handled = true;
    }

    private void GalleryControl_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta == default || !IsPointerOver) return;

        SuspendItemTooltips();
    }

    private void GalleryItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not GalleryItem itemEl) return;
        if (itemEl.VM.IsCurrent) return;

        ItemClicked?.Invoke(itemEl, new GalleryItemClickEventArgs(itemEl.VM));
    }

    public void LoadThumbnail(int index, bool useCache)
    {
        var photo = Core.Photos.Get(index);
        if (photo is null) return;

        if (!useCache) photo.CancelThumbnailLoading();

        var thumbSize = Core.Config.ThumbnailSize * Dpi * 2;
        _ = photo.LoadThumbnailAsync(thumbSize, useCache);
    }

    private void RefreshRealizedThumbnails()
    {
        foreach (var item in PART_ItemsControl.GetVisualDescendants().OfType<GalleryItem>())
        {
            if (IsContentVisible) item.LoadThumbnail();
            else item.CancelThumbnailLoading();
        }
    }

    private void SuspendItemTooltips()
    {
        if (ToolTip.GetServiceEnabled(this))
        {
            ToolTip.SetServiceEnabled(this, false);

            foreach (var item in PART_ItemsControl.GetVisualDescendants().OfType<GalleryItem>())
            {
                if (ToolTip.GetIsOpen(item)) ToolTip.SetIsOpen(item, false);
            }
        }

        _tooltipResumeTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(TOOLTIP_RESUME_DELAY_MS),
            DispatcherPriority.Background,
            (_, _) =>
            {
                _tooltipResumeTimer?.Stop();
                ToolTip.SetServiceEnabled(this, true);
            });

        _tooltipResumeTimer.Stop();
        _tooltipResumeTimer.Start();
    }

    public void ScrollToItem(int index, bool enableAnimation = false)
    {
        var svEl = FindScrollViewer();
        if (svEl is null || index < 0) return;

        var targetOffset = GetCenteredScrollOffset(svEl, index);
        if (enableAnimation)
        {
            _ = AnimateScrollAsync(svEl, targetOffset);
        }
        else
        {
            _cancelScrollAnimation?.Cancel();
            svEl.Offset = targetOffset;
        }
    }

    private static Size GetCellSize()
    {
        var itemSize = (double)Core.Config.ThumbnailSize;
        var cellWidth = itemSize + GalleryItemMargin.Left + GalleryItemMargin.Right;
        var cellHeight = itemSize + GalleryItemMargin.Top + GalleryItemMargin.Bottom;

        return new Size(cellWidth, cellHeight);
    }

    private static double GetWheelScrollStep(double cellLength, double viewportLength)
    {
        if (viewportLength <= 0) return cellLength;

        return Math.Min(cellLength, viewportLength);
    }

    private Vector GetCenteredScrollOffset(ScrollViewer svEl, int index)
    {
        var cellSize = GetCellSize();
        var cellWidth = cellSize.Width;
        var cellHeight = cellSize.Height;
        var padLeft = GalleryPadding.Left;
        var padTop = GalleryPadding.Top;

        if (ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip)
        {
            var itemCenter = padLeft + (index * cellWidth) + (cellWidth / 2);
            var target = itemCenter - (svEl.Viewport.Width / 2);
            var maxOffset = Math.Max(0, svEl.Extent.Width - svEl.Viewport.Width);

            return new Vector(Math.Clamp(target, 0, maxOffset), svEl.Offset.Y);
        }
        else
        {
            var availWidth = svEl.Viewport.Width - padLeft - GalleryPadding.Right;
            var columnsPerRow = Math.Max(1, (int)(availWidth / cellWidth));
            var row = index / columnsPerRow;

            var itemCenter = padTop + (row * cellHeight) + (cellHeight / 2);
            var target = itemCenter - (svEl.Viewport.Height / 2);
            var maxOffset = Math.Max(0, svEl.Extent.Height - svEl.Viewport.Height);

            return new Vector(svEl.Offset.X, Math.Clamp(target, 0, maxOffset));
        }
    }

    private async Task AnimateScrollAsync(ScrollViewer svEl, Vector targetOffset, int durationMs = 300)
    {
        _cancelScrollAnimation?.Cancel();
        _cancelScrollAnimation = new CancellationTokenSource();
        var token = _cancelScrollAnimation.Token;

        var startOffset = svEl.Offset;
        var startTime = Environment.TickCount64;

        while (!token.IsCancellationRequested)
        {
            var elapsed = Environment.TickCount64 - startTime;
            var progress = Math.Clamp((double)elapsed / durationMs, 0, 1);

            var eased = 1 - Math.Pow(1 - progress, 3);

            var x = startOffset.X + (targetOffset.X - startOffset.X) * eased;
            var y = startOffset.Y + (targetOffset.Y - startOffset.Y) * eased;
            svEl.Offset = new Vector(x, y);

            if (progress >= 1) break;

            await Task.Delay(16, token);
        }
    }

    public ScrollViewer? FindScrollViewer()
    {
        _cachedScrollViewerEl ??= PART_ItemsControl
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

        return _cachedScrollViewerEl;
    }

    public PhVirtualizingUniformPanel? FindVirtualPanel()
    {
        return PART_ItemsControl
            .GetVisualDescendants()
            .OfType<PhVirtualizingUniformPanel>()
            .FirstOrDefault();
    }

    private void UpdateReservedSize()
    {
        PART_ItemsControl.MinWidth = MinContentSize.Width;
        PART_ItemsControl.MinHeight = MinContentSize.Height;

        var svEl = FindScrollViewer();
        if (svEl is not null)
        {
            if (ViewMode == PhVirtualizingUniformPanelViewMode.FilmStrip)
            {
                svEl.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                svEl.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            else
            {
                svEl.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                svEl.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }
    }

    public static double CalculateWidthForGalleryView(uint itemColumns)
    {
        var itemWidth = Core.Config.ThumbnailSize
            + GalleryItemMargin.Left + GalleryItemMargin.Right
            + GalleryPadding.Left + GalleryPadding.Right;

        return itemWidth * itemColumns;
    }

}

public class GalleryItemClickEventArgs(Photo vm) : RoutedEventArgs
{
    public Photo VM => vm;
}
