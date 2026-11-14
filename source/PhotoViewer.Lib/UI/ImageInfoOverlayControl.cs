using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoViewer.Common;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace PhotoViewer.UI;

public class ImageInfoOverlayControl : PhOverlay
{
    private const double REVEAL_ZONE_EXTRA = 10;
    private const double KEEP_ZONE_EXTRA = 48;
    private static readonly TimeSpan HIDE_DELAY = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan PEEK_DURATION = TimeSpan.FromMilliseconds(2200);

    private static readonly FontFeatureCollection TABULAR_DIGITS = new() { new FontFeature { Tag = "tnum" } };

    private readonly TextBlock _textBlock;
    private readonly DispatcherTimer _hideTimer = new() { Interval = HIDE_DELAY };
    private readonly DispatcherTimer _peekTimer = new() { Interval = PEEK_DURATION };
    private TopLevel? _topLevel;

    private List<ImageInfoItem> _items = [];
    private bool _isTextDirty;
    private bool _isHovered;
    private bool _isPeeking;
    private double _coveredHeight;

    static ImageInfoOverlayControl()
    {
        HorizontalAlignmentProperty.OverrideDefaultValue<ImageInfoOverlayControl>(HorizontalAlignment.Center);
        VerticalAlignmentProperty.OverrideDefaultValue<ImageInfoOverlayControl>(VerticalAlignment.Top);
        MarginProperty.OverrideDefaultValue<ImageInfoOverlayControl>(new Thickness(20, 8, 20, 0));
        PaddingProperty.OverrideDefaultValue<ImageInfoOverlayControl>(new Thickness(12, 4));
        TransitionDirectionProperty.OverrideDefaultValue<ImageInfoOverlayControl>(PhOverlayDirection.Top);

        IsHitTestVisibleProperty.OverrideDefaultValue<ImageInfoOverlayControl>(false);
    }

    public ImageInfoOverlayControl()
    {
        _textBlock = new TextBlock
        {
            FontSize = Const.FONT_SIZE_BODY,
            FontFeatures = TABULAR_DIGITS,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Content = _textBlock;

        _hideTimer.Tick += HideTimer_Tick;
        _peekTimer.Tick += PeekTimer_Tick;
    }

    public static bool IsTitleBarHidden
    {
        get
        {
            var isFullScreen = Core.Config.EnableFullScreen;
            if (isFullScreen) return true;

            var isFrameless = Core.Config.EnableFrameless;
            return isFrameless;
        }
    }

    public double CoveredHeight
    {
        get => _coveredHeight;
        private set => SetAndRaise(CoveredHeightProperty, ref _coveredHeight, value);
    }
    public static readonly DirectProperty<ImageInfoOverlayControl, double> CoveredHeightProperty =
        AvaloniaProperty.RegisterDirect<ImageInfoOverlayControl, double>(nameof(CoveredHeight), o => o.CoveredHeight);

    public void SetItems(IReadOnlyList<ImageInfoItem> items)
    {
        var imageItems = items.Where(item => item.Tag != nameof(AppStatusInfo.AppName)).ToList();

        var isSame = imageItems.SequenceEqual(_items);
        if (isSame) return;

        _items = imageItems;
        _isTextDirty = true;

        RefreshText();
        UpdateVisibility();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerMovedEvent, TopLevel_PointerMoved, RoutingStrategies.Tunnel, true);
        _topLevel?.AddHandler(PointerPressedEvent, TopLevel_PointerPressed, RoutingStrategies.Tunnel, true);
        _topLevel?.PointerExited += TopLevel_PointerExited;

        Core.Config.PropertyChanged += Config_PropertyChanged;

        UpdateVisibility();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        _topLevel?.RemoveHandler(PointerMovedEvent, TopLevel_PointerMoved);
        _topLevel?.RemoveHandler(PointerPressedEvent, TopLevel_PointerPressed);
        _topLevel?.PointerExited -= TopLevel_PointerExited;
        _topLevel = null;

        Core.Config.PropertyChanged -= Config_PropertyChanged;

        _hideTimer.Stop();
        _peekTimer.Stop();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);

        _isTextDirty = true;
        RefreshText();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (e.HeightChanged) UpdateCoveredHeight();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        var isPlacementChanged = change.Property == MarginProperty
            || change.Property == PaddingProperty
            || change.Property == VerticalAlignmentProperty;
        if (isPlacementChanged) UpdateCoveredHeight();
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var isRelevant = e.PropertyName is nameof(Config.ImageInfoOverlayMode)
            or nameof(Config.EnableFullScreen)
            or nameof(Config.EnableFrameless)
            or nameof(Config.EnableSlideshow);
        if (!isRelevant) return;

        var isModeChanged = e.PropertyName is nameof(Config.ImageInfoOverlayMode)
            or nameof(Config.EnableSlideshow);

        Dispatcher.UIThread.Post(() =>
        {
            if (isModeChanged)
            {
                _isHovered = false;
                _hideTimer.Stop();
            }

            UpdateVisibility();
        });
    }

    private void TopLevel_PointerMoved(object? sender, PointerEventArgs e)
    {
        var isTouch = e.Pointer.Type == PointerType.Touch;
        if (isTouch) return;

        var isWaitingForPointer = IsAutoHideActive();
        if (!isWaitingForPointer) return;

        var isInZone = IsInRevealZone(e);
        if (isInZone)
        {
            _hideTimer.Stop();
            if (_isHovered) return;

            _isHovered = true;
            UpdateVisibility();
            return;
        }

        if (!_isHovered) return;
        if (_hideTimer.IsEnabled) return;

        _hideTimer.Start();
    }

    private void TopLevel_PointerExited(object? sender, PointerEventArgs e)
    {
        if (!_isHovered) return;

        _hideTimer.Start();
    }

    private void TopLevel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var isTouch = e.Pointer.Type == PointerType.Touch;
        if (!isTouch) return;

        var isWaitingForPointer = IsAutoHideActive();
        if (!isWaitingForPointer) return;

        var isInZone = IsInRevealZone(e);
        if (!isInZone) return;

        Peek();
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        _isHovered = false;

        UpdateVisibility();
    }

    private void PeekTimer_Tick(object? sender, EventArgs e)
    {
        _peekTimer.Stop();
        _isPeeking = false;

        UpdateVisibility();
    }

    private static bool IsModeActive
    {
        get
        {
            var mode = Core.Config.ImageInfoOverlayMode;
            if (mode == ImageInfoOverlayMode.Hidden) return false;

            var isInEveryWindow = mode is ImageInfoOverlayMode.Always or ImageInfoOverlayMode.AlwaysAutoHide;
            if (isInEveryWindow) return true;

            var isTitleBarHidden = IsTitleBarHidden;
            return isTitleBarHidden;
        }
    }

    private static bool IsAutoHideMode
    {
        get
        {
            var isSlideshow = Core.Config.EnableSlideshow;
            if (isSlideshow) return true;

            var mode = Core.Config.ImageInfoOverlayMode;
            var isAutoHide = mode is ImageInfoOverlayMode.AlwaysAutoHide or ImageInfoOverlayMode.BorderlessAutoHide;

            return isAutoHide;
        }
    }

    private bool IsAvailable()
    {
        var hasItems = _items.Count > 0;
        if (!hasItems) return false;

        var isModeActive = IsModeActive;
        return isModeActive;
    }

    private bool IsAutoHideActive()
    {
        var isAutoHide = IsAutoHideMode;
        if (!isAutoHide) return false;

        var isAvailable = IsAvailable();
        return isAvailable;
    }

    private bool ShouldShow()
    {
        var isAvailable = IsAvailable();
        if (!isAvailable) return false;

        var isAutoHide = IsAutoHideMode;
        if (!isAutoHide) return true;

        if (_isHovered) return true;
        return _isPeeking;
    }

    private void UpdateVisibility()
    {
        var isAvailable = IsAvailable();
        if (!isAvailable) ResetReveal();

        UpdateCoveredHeight();

        var isShown = ShouldShow();
        if (isShown == IsShown) return;

        IsShown = isShown;
        RefreshText();
    }

    private void Peek()
    {
        var isAutoHide = IsAutoHideMode;
        if (!isAutoHide) return;

        _isPeeking = true;
        _peekTimer.Stop();
        _peekTimer.Start();

        UpdateVisibility();
    }

    private void ResetReveal()
    {
        _isHovered = false;
        _isPeeking = false;
        _hideTimer.Stop();
        _peekTimer.Stop();
    }

    private bool IsInRevealZone(PointerEventArgs e)
    {
        if (this.GetVisualParent() is not Visual host) return false;
        var point = e.GetPosition(host);

        var isLeftOfViewer = point.X < 0;
        if (isLeftOfViewer) return false;

        var isRightOfViewer = point.X > host.Bounds.Width;
        if (isRightOfViewer) return false;

        var zoneExtra = _isHovered ? KEEP_ZONE_EXTRA : REVEAL_ZONE_EXTRA;
        var zoneDepth = GetBarHeight() + zoneExtra;

        var isAtBottom = VerticalAlignment == VerticalAlignment.Bottom;
        if (isAtBottom)
        {
            var zoneTop = host.Bounds.Height - Margin.Bottom - zoneDepth;
            var isBelowZoneTop = point.Y >= zoneTop;
            return isBelowZoneTop;
        }

        var zoneBottom = Margin.Top + zoneDepth;
        var isAboveZoneBottom = point.Y <= zoneBottom;
        return isAboveZoneBottom;
    }

    private void UpdateCoveredHeight()
    {
        var isAvailable = IsAvailable();
        if (!isAvailable)
        {
            CoveredHeight = 0;
            return;
        }

        var isAtBottom = VerticalAlignment == VerticalAlignment.Bottom;
        var edgeMargin = isAtBottom ? Margin.Bottom : Margin.Top;
        CoveredHeight = edgeMargin + GetBarHeight();
    }

    private double GetBarHeight()
    {
        var isLaidOut = Bounds.Height > 0;
        if (isLaidOut) return Bounds.Height;

        return Const.FONT_SIZE_BODY * 1.4 + Padding.Top + Padding.Bottom;
    }

    private void RefreshText()
    {
        if (!_isTextDirty) return;

        if (!IsOnScreen) return;

        if (_items.Count == 0) return;

        _isTextDirty = false;
        _textBlock.Inlines = BuildInlines();
    }

    private InlineCollection BuildInlines()
    {
        var textBrush = new ImmutableSolidColorBrush(AppThemeColors.TextColorBrush.Color);
        var separatorBrush = new ImmutableSolidColorBrush(GetSeparatorColor());

        var inlines = new InlineCollection();
        foreach (var item in _items)
        {
            if (inlines.Count > 0)
            {
                inlines.Add(new Run(AppStatusInfo.TEXT_SEPARATOR) { Foreground = separatorBrush });
            }

            var isTitle = IsTitleItem(item);
            inlines.Add(new Run(item.Value)
            {
                Foreground = textBrush,
                FontWeight = isTitle ? FontWeight.SemiBold : FontWeight.Normal,
            });
        }

        return inlines;
    }

    private static bool IsTitleItem(ImageInfoItem item)
    {
        var isTitle = item.Tag is nameof(AppStatusInfo.Name)
            or nameof(AppStatusInfo.Path)
            or AppStatusInfo.CLIPBOARD_TAG;

        return isTitle;
    }

    private static Color GetSeparatorColor()
    {
        var textAccent = Resx.Get<object?>(ResxId.IG_TextAccentColor);
        if (textAccent is Color color) return color;

        return Core.AccentColor;
    }

}
