using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using System;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{
    private readonly ScrollBar _hScrollbar = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollBar _vScrollbar = new() { Orientation = Orientation.Vertical };
    private readonly Grid _scrollbarHost = new()
    {
        RowDefinitions = new("*, Auto"),
        ColumnDefinitions = new("*, Auto"),
        Cursor = Avalonia.Input.Cursor.Default,
        IsVisible = false,
    };

    public ScrollbarMode ScrollbarMode
    {
        get => GetValue(ScrollbarModeProperty);
        set => SetValue(ScrollbarModeProperty, value);
    }
    public static readonly StyledProperty<ScrollbarMode> ScrollbarModeProperty =
        AvaloniaProperty.Register<ViewerControl, ScrollbarMode>(nameof(ScrollbarMode));

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        VisualChildren.Add(_scrollbarHost);
    }

    private void InitScrollbars()
    {
        Grid.SetColumn(_vScrollbar, 1);
        Grid.SetRow(_hScrollbar, 1);
        _scrollbarHost.Children.Add(_vScrollbar);
        _scrollbarHost.Children.Add(_hScrollbar);
        _scrollbarHost.Bind(MarginProperty, this.GetObservable(PaddingProperty));
        LogicalChildren.Add(_scrollbarHost);

        _hScrollbar.Scroll += Scrollbar_Scroll;
        _vScrollbar.Scroll += Scrollbar_Scroll;
        _hScrollbar.PointerWheelChanged += Scrollbar_PointerWheelChanged;
        _vScrollbar.PointerWheelChanged += Scrollbar_PointerWheelChanged;

        _scrollbarHost.AddHandler(PointerPressedEvent, ScrollbarHost_PointerPressed, handledEventsToo: true);
        _scrollbarHost.AddHandler(PointerMovedEvent, ScrollbarHost_PointerMoved);
        _scrollbarHost.AddHandler(PointerReleasedEvent, ScrollbarHost_PointerReleased);
        _scrollbarHost.AddHandler(TappedEvent, ScrollbarHost_Tapped);
        _scrollbarHost.AddHandler(DoubleTappedEvent, ScrollbarHost_Tapped);
        _scrollbarHost.AddHandler(RightTappedEvent, ScrollbarHost_Tapped);
    }

    private void ApplyScrollbarMode()
    {
        var mode = ScrollbarMode;
        var isVisible = AreScrollbarsShown();
        var allowAutoHide = mode == ScrollbarMode.AutoHide;

        _scrollbarHost.IsVisible = isVisible;
        _hScrollbar.AllowAutoHide = allowAutoHide;
        _vScrollbar.AllowAutoHide = allowAutoHide;

        UpdateScrollbars();
    }

    private bool AreScrollbarsShown()
    {
        if (ScrollbarMode == ScrollbarMode.Never) return false;

        return IsInteractive;
    }

    private void UpdateScrollbars()
    {
        if (!AreScrollbarsShown()) return;

        var zoomFactor = _zooming.Factor / Dpi;
        var viewport = DrawingArea.Size / zoomFactor;

        UpdateScrollbar(_hScrollbar, BitmapSize.Width, viewport.Width, _logicalSrcPoint.X);
        UpdateScrollbar(_vScrollbar, BitmapSize.Height, viewport.Height, _logicalSrcPoint.Y);
    }

    private void UpdateScrollbar(ScrollBar bar, double extent, double viewport, double offset)
    {
        var logicalPixel = Dpi / _zooming.Factor;
        var hiddenExtent = extent - viewport;
        var hasViewport = viewport > 0;

        var hasHiddenImage = hiddenExtent >= logicalPixel;
        var isScrollable = hasViewport && hasHiddenImage;

        bar.Visibility = isScrollable ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
        if (!isScrollable) return;

        var minimum = Math.Min(0, offset);
        var maximum = Math.Max(hiddenExtent, offset);

        bar.Minimum = minimum;
        bar.Maximum = maximum;
        bar.ViewportSize = viewport;
        bar.LargeChange = viewport;
        bar.SmallChange = Const.MOUSE_WHEEL_SCROLL_DELTA * logicalPixel;
        bar.Value = offset;
    }

    private void PanAlong(ScrollBar bar, double distance)
    {
        if (bar.Orientation == Orientation.Horizontal)
        {
            _ = PanTo(distance, 0, null);
        }
        else
        {
            _ = PanTo(0, distance, null);
        }
    }

    private void Scrollbar_Scroll(object? sender, ScrollEventArgs e)
    {
        if (sender is not ScrollBar bar) return;

        if (FeatureManager.IsPanLocked())
        {
            UpdateScrollbars();
            return;
        }

        var isHorizontal = bar.Orientation == Orientation.Horizontal;
        var offset = isHorizontal ? _logicalSrcPoint.X : _logicalSrcPoint.Y;

        var distance = (e.NewValue - offset) * _zooming.Factor / Dpi;

        PanAlong(bar, distance);
    }

    private void Scrollbar_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        if (sender is not ScrollBar bar) return;
        if (FeatureManager.IsPanLocked()) return;

        var isHorizontalDelta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y);
        var delta = isHorizontalDelta ? e.Delta.X : e.Delta.Y;
        var distance = -delta * Const.MOUSE_WHEEL_SCROLL_DELTA;

        PanAlong(bar, distance);
    }

    private void ScrollbarHost_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.PreventGestureRecognition();

        var p = e.GetCurrentPoint(this);
        var isThumbButton = IsThumbButtonPressed(p);
        if (isThumbButton) return;

        e.Handled = true;
    }

    private void ScrollbarHost_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var isThumbButton = IsThumbButton(e.InitialPressMouseButton);
        if (isThumbButton) return;

        e.Handled = true;
    }

    private static void ScrollbarHost_PointerMoved(object? sender, PointerEventArgs e) => e.Handled = true;

    private static void ScrollbarHost_Tapped(object? sender, TappedEventArgs e) => e.Handled = true;

}
