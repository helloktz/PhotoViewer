using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.Common.Windows;
using PhotoViewer.UI;
using PhotoViewer.UI.Viewer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

public partial class ProjectorToolControl : PhControl, IToolControl
{
    private static readonly ProjectorWindowMode[] _windowModes =
    [
        ProjectorWindowMode.FullScreen,
        ProjectorWindowMode.Maximized,
        ProjectorWindowMode.Normal,
        ProjectorWindowMode.Tiled,
    ];

    private static readonly ZoomMode[] _zoomModes =
    [
        ZoomMode.AutoZoom,
        ZoomMode.ScaleToFit,
        ZoomMode.ScaleToFill,
        ZoomMode.ScaleToWidth,
        ZoomMode.ScaleToHeight,
    ];

    private const double MAP_HEIGHT = 96;
    private const double MAP_HEIGHT_STEP = 16;
    private const int MAX_MAP_HEIGHT_STEPS = 3;

    private const double DIVIDER_MARGIN_ACROSS = 20;
    private const double DIVIDER_MARGIN_DOWN = 14;

    private const double SCROLL_BAR_ROOM = 14;

    private const double DRAG_THRESHOLD = 4;

    private const double DRAG_GHOST_OFFSET = 14;

    private const int MAX_LAYOUT_COLUMNS = 3;

    private readonly Dictionary<ProjectorWindow, ProjectorRow> _rows = [];

    private readonly List<ProjectorWindow> _rowOrder = [];
    private bool? _isRowLayoutSynced;
    private readonly List<LayoutOption> _layoutOptions = [];

    private static ProjectorManager? _manager;

    private ProjectorWindow? _dragWindow;
    private Screen? _dragSourceScreen;
    private Point _dragStart;
    private bool _isDragging;

    private Screen? _layoutScreen;
    private int[] _layoutNumbers = [];

    private bool _isUpdatingUI;

    public static string TOOL_ID => ProjectorManager.TOOL_ID;
    public string ToolId => TOOL_ID;
    public string ToolName => Core.Lang[LangId.Menu_MnuProjector];
    public ResxIconId? ToolIcon => ResxIconId.IconToolProjector;
    public bool HasSettingsUI => false;
    public object? Settings => _manager?.Config;
    public ViewerControl Viewer { get; set; } = null!;

    internal static ProjectorManager? Manager => _manager;

    public ProjectorToolControl()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var manager = GetManager();
        manager.Changed += Manager_Changed;
        PART_ScreenMap.Manager = manager;

        RefreshUI();

        PART_ScreenMap.ScreenClicked += PART_ScreenMap_ScreenClicked;
        PART_LayoutPopup.Closed += PART_LayoutPopup_Closed;
        PART_BtnAddFirst.Click += PART_BtnAdd_Click;
        PART_BtnAdd.Click += PART_BtnAdd_Click;
        PART_ChkSync.IsCheckedChanged += PART_ChkSync_IsCheckedChanged;
        PART_ChkPointer.IsCheckedChanged += PART_ChkPointer_IsCheckedChanged;
        PART_ProjectorScroller.ScrollChanged += PART_ProjectorScroller_ScrollChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _manager?.Changed -= Manager_Changed;
        PART_ScreenMap.Manager = null;
        PART_LayoutPopup.IsOpen = false;
        EndDrag();

        PART_ScreenMap.ScreenClicked -= PART_ScreenMap_ScreenClicked;
        PART_LayoutPopup.Closed -= PART_LayoutPopup_Closed;
        PART_BtnAddFirst.Click -= PART_BtnAdd_Click;
        PART_BtnAdd.Click -= PART_BtnAdd_Click;
        PART_ChkSync.IsCheckedChanged -= PART_ChkSync_IsCheckedChanged;
        PART_ChkPointer.IsCheckedChanged -= PART_ChkPointer_IsCheckedChanged;
        PART_ProjectorScroller.ScrollChanged -= PART_ProjectorScroller_ScrollChanged;

        base.OnUnloaded(e);
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        var addText = Core.Lang[LangId.Tool_Projector_BtnAdd];
        PART_BtnAddFirst.Text = addText;
        PART_BtnAdd.Text = addText;

        foreach (var row in _rows.Values)
        {
            RelocalizeRow(row);
        }
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);

        RefreshUI();
    }

    private void Manager_Changed(object? sender, EventArgs e)
    {
        RefreshUI();
    }

    private void PART_ScreenMap_ScreenClicked(ScreenMapControl sender, Screen screen)
    {
        OpenLayoutMenu(screen);
    }

    private void PART_LayoutPopup_Closed(object? sender, EventArgs e)
    {
        _layoutScreen = null;
        _layoutNumbers = [];
    }

    private async void PART_BtnAdd_Click(object? sender, RoutedEventArgs e)
    {
        if (_manager is null) return;

        _ = await _manager.AddProjectorAsync();
    }

    private void PART_ChkSync_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI) return;

        var isChecked = PART_ChkSync.IsChecked == true;
        _manager?.SetViewSync(isChecked);
    }

    private void PART_ChkPointer_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingUI) return;

        var isChecked = PART_ChkPointer.IsChecked == true;
        _manager?.SetShowPointer(isChecked);
    }

    private void PART_ProjectorScroller_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var canScroll = PART_ProjectorScroller.Extent.Height > PART_ProjectorScroller.Viewport.Height;
        PART_ProjectorList.Margin = canScroll
            ? new Thickness(0, 0, SCROLL_BAR_ROOM, 0)
            : default;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateOrientation(availableSize.Width);

        return base.MeasureOverride(availableSize);
    }

    private static ProjectorManager GetManager() => _manager ??= new ProjectorManager();

    internal static void DisposeManager()
    {
        _manager?.Dispose();
        _manager = null;
    }

    public void LoadSettings(JsonElement? jsonEl)
    {
        _ = GetManager();
    }

    public JsonElement? SaveSettings()
    {
        return GetManager().SaveSettings();
    }

    private void RefreshUI()
    {
        if (_manager is null) return;

        _isUpdatingUI = true;
        try
        {
            var windows = _manager.Windows;
            var hasProjectors = windows.Count > 0;
            PART_BtnAddFirst.IsVisible = !hasProjectors;
            PART_Projectors.IsVisible = hasProjectors;

            var isSynced = _manager.IsViewSynced;
            SyncRows(isSynced);

            var screens = _manager.GetOrderedScreens();
            var defaultColor = ProjectorWindow.DefaultBackgroundColor;
            foreach (var row in _rows.Values)
            {
                SyncMonitorItems(row.CmbMonitor, screens.Count);
                row.CmbMonitor.SelectedIndex = IndexOfScreen(screens, row.Window.GetScreen());
                row.CmbMode.SelectedIndex = Array.IndexOf(_windowModes, row.Window.Mode);
                row.CmbZoom.SelectedIndex = Array.IndexOf(_zoomModes, row.Window.Viewer.ZoomMode);
                row.ColorPicker.DefaultColor = defaultColor;
                row.ColorPicker.SelectedColor = row.Window.BackgroundColor ?? defaultColor;
            }

            PART_ChkSync.IsChecked = isSynced;
            PART_ChkPointer.IsChecked = _manager.IsPointerShown;

            SyncLayoutMenu();
        }
        finally
        {
            _isUpdatingUI = false;
        }
    }

    private void SyncRows(bool isSynced)
    {
        var windows = _manager!.Windows;

        var closedWindows = _rows.Keys.Where(w => !windows.Contains(w)).ToArray();
        foreach (var window in closedWindows)
        {
            _ = _rows.Remove(window);
        }

        foreach (var window in windows)
        {
            var hasRow = _rows.ContainsKey(window);
            if (!hasRow) _rows[window] = CreateRow(window);
        }

        var isLaidOut = _rowOrder.SequenceEqual(windows) && _isRowLayoutSynced == isSynced;
        if (isLaidOut) return;

        _rowOrder.Clear();
        _rowOrder.AddRange(windows);
        _isRowLayoutSynced = isSynced;
        PART_ProjectorList.Children.Clear();
        PART_ProjectorList.RowDefinitions.Clear();

        var columnCount = isSynced ? 5 : 6;
        PART_ProjectorList.ColumnDefinitions.Clear();
        for (var column = 0; column < columnCount; column++)
        {
            PART_ProjectorList.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (var rowIndex = 0; rowIndex < windows.Count; rowIndex++)
        {
            var row = _rows[windows[rowIndex]];
            Control[] cells = isSynced
                ? [row.BtnName, row.CmbMonitor, row.CmbMode, row.ColorPicker, row.BtnClose]
                : [row.BtnName, row.CmbMonitor, row.CmbMode, row.CmbZoom, row.ColorPicker, row.BtnClose];

            PART_ProjectorList.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var column = 0; column < cells.Length; column++)
            {
                Grid.SetRow(cells[column], rowIndex);
                Grid.SetColumn(cells[column], column);
                PART_ProjectorList.Children.Add(cells[column]);
            }
        }
    }

    private ProjectorRow CreateRow(ProjectorWindow window)
    {
        var badge = new PhChip
        {
            Text = ScreenMapControl.GetBadgeText(window.Number),
            Variant = PhChipVariant.Accent,
        };
        var lblName = new PhTextBlock
        {
            LangKey = LangId.Tool_Projector_WindowTitle,
            LangParams = window.Number,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var btnName = new PhButton
        {
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { badge, lblName },
            },
        };
        btnName.Click += (_, _) =>
        {
            if (_isDragging) return;

            window.RestoreAndActivate();
        };
        btnName.PointerEntered += (_, _) => PART_ScreenMap.SelectedProjectorNumber = window.Number;
        btnName.PointerExited += (_, _) =>
        {
            if (!_isDragging) PART_ScreenMap.SelectedProjectorNumber = 0;
        };
        btnName.AddHandler(PointerPressedEvent, (_, e) => StartDrag(window, btnName, e), RoutingStrategies.Bubble, true);
        btnName.AddHandler(PointerMovedEvent, (_, e) => ContinueDrag(e), RoutingStrategies.Bubble, true);
        btnName.AddHandler(PointerReleasedEvent, (_, _) => DropDraggedProjector(), RoutingStrategies.Bubble, true);
        btnName.PointerCaptureLost += (_, _) => EndDrag();

        var cmbMonitor = new ComboBox
        {
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        cmbMonitor.SelectionChanged += async (_, _) => await MoveToMonitorAsync(window, cmbMonitor.SelectedIndex);

        var cmbMode = new ComboBox
        {
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var _ in _windowModes)
        {
            cmbMode.Items.Add(new ComboBoxItem());
        }
        cmbMode.SelectionChanged += async (_, _) =>
        {
            if (_isUpdatingUI) return;
            if (_manager is null) return;

            var index = cmbMode.SelectedIndex;
            if (index < 0) return;

            await _manager.SetWindowModeAsync(window, _windowModes[index]);
        };

        var colorPicker = new PhColorPickerControl
        {
            ShowHexLabel = false,
            ShowResetButton = false,
            SwatchWidth = 24,
            VerticalAlignment = VerticalAlignment.Center,
        };
        colorPicker.ColorChanged += (_, _) =>
        {
            if (_isUpdatingUI) return;
            if (_manager is null) return;

            var color = colorPicker.SelectedColor;
            var isDefaultColor = color == colorPicker.DefaultColor;

            _manager.SetBackgroundColor(window, isDefaultColor ? null : color);
        };

        var closeIcon = new Path
        {
            Width = 12,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = Resx.GetIcon(ResxIconId.IconClose),
            Stretch = Stretch.Uniform,
        };
        closeIcon[!Shape.FillProperty] = Resx.CreateBinding(ResxId.IG_ThemeForegroundBrush);

        var btnClose = new PhToolButton
        {
            Padding = new Thickness(6),
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            Content = closeIcon,
        };

        btnClose.Classes.Add("plugin_button");
        btnClose.Click += (_, _) => ProjectorManager.CloseProjector(window);

        var cmbZoom = new ComboBox
        {
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var _ in _zoomModes)
        {
            cmbZoom.Items.Add(new ComboBoxItem());
        }
        cmbZoom.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingUI) return;
            if (_manager is null) return;

            var index = cmbZoom.SelectedIndex;
            if (index < 0) return;

            _manager.SetZoomMode(window, _zoomModes[index]);
        };

        var row = new ProjectorRow(window, btnName, cmbMonitor, cmbMode, cmbZoom, colorPicker, btnClose);
        RelocalizeRow(row);

        return row;
    }

    private async Task MoveToMonitorAsync(ProjectorWindow window, int monitorIndex)
    {
        if (_isUpdatingUI) return;
        if (_manager is null) return;

        var screens = _manager.GetOrderedScreens();
        if (monitorIndex < 0 || monitorIndex >= screens.Count) return;

        var screen = screens[monitorIndex];
        if (screen == window.GetScreen()) return;

        await _manager.MoveProjectorAsync(window, screen);
    }

    private static void SyncMonitorItems(ComboBox combo, int monitorCount)
    {
        while (combo.ItemCount > monitorCount)
        {
            combo.Items.RemoveAt(combo.ItemCount - 1);
        }

        while (combo.ItemCount < monitorCount)
        {
            var monitorNumber = combo.ItemCount + 1;
            combo.Items.Add(new ComboBoxItem { Content = Core.Lang[LangId.Tool_Projector_Monitor, monitorNumber] });
        }
    }

    private static int IndexOfScreen(IReadOnlyList<Screen> screens, Screen? screen)
    {
        if (screen is null) return -1;

        for (var i = 0; i < screens.Count; i++)
        {
            if (screens[i] == screen) return i;
        }

        return -1;
    }

    private static void RelocalizeRow(ProjectorRow row)
    {
        var projectorName = Core.Lang[LangId.Tool_Projector_WindowTitle, row.Window.Number];
        var colorText = Core.Lang[LangId._BackgroundColor];

        row.ColorPicker.Title = $"{projectorName} - {colorText}";
        ToolTip.SetTip(row.BtnName, Core.Lang[LangId.Tool_Projector_DragHint]);
        ToolTip.SetTip(row.ColorPicker, colorText);
        ToolTip.SetTip(row.BtnClose, Core.Lang[LangId._Close]);

        for (var i = 0; i < row.CmbMonitor.ItemCount; i++)
        {
            if (row.CmbMonitor.Items[i] is not ComboBoxItem item) continue;
            item.Content = Core.Lang[LangId.Tool_Projector_Monitor, i + 1];
        }

        for (var i = 0; i < row.CmbMode.ItemCount; i++)
        {
            if (row.CmbMode.Items[i] is not ComboBoxItem item) continue;
            item.Content = GetWindowModeText(_windowModes[i]);
        }

        for (var i = 0; i < row.CmbZoom.ItemCount; i++)
        {
            if (row.CmbZoom.Items[i] is not ComboBoxItem item) continue;
            item.Content = GetZoomModeText(_zoomModes[i]);
        }
    }

    private static string GetWindowModeText(ProjectorWindowMode mode) => mode switch
    {
        ProjectorWindowMode.FullScreen => Core.Lang[LangId.Tool_Projector_ModeFullScreen],
        ProjectorWindowMode.Maximized => Core.Lang[LangId.Tool_Projector_ModeMaximized],
        ProjectorWindowMode.Tiled => Core.Lang[LangId.Tool_Projector_ModeTiled],
        _ => Core.Lang[LangId.Tool_Projector_ModeNormal],
    };

    private static string GetZoomModeText(ZoomMode mode) => mode switch
    {
        ZoomMode.ScaleToFit => Core.Lang[LangId.Menu_MnuScaleToFit],
        ZoomMode.ScaleToFill => Core.Lang[LangId.Menu_MnuScaleToFill],
        ZoomMode.ScaleToWidth => Core.Lang[LangId.Menu_MnuScaleToWidth],
        ZoomMode.ScaleToHeight => Core.Lang[LangId.Menu_MnuScaleToHeight],
        _ => Core.Lang[LangId.Menu_MnuAutoZoom],
    };

    private void UpdateOrientation(double availableWidth)
    {
        if (!PART_Projectors.IsVisible) return;

        var projectorCount = _manager?.Windows.Count ?? 0;
        var heightSteps = Math.Clamp(projectorCount - 1, 0, MAX_MAP_HEIGHT_STEPS);
        var mapHeightAcross = MAP_HEIGHT + heightSteps * MAP_HEIGHT_STEP;
        PART_ListPanel.Measure(Size.Infinity);

        var widthAcross = PART_ScreenMap.GetWidthForHeight(mapHeightAcross)
            + DIVIDER_MARGIN_ACROSS * 2 + 1
            + PART_ListPanel.DesiredSize.Width
            + Padding.Left + Padding.Right;
        var isDown = double.IsFinite(availableWidth) && availableWidth < widthAcross;

        Grid.SetRow(PART_Divider, isDown ? 1 : 0);
        Grid.SetColumn(PART_Divider, isDown ? 0 : 1);
        Grid.SetRow(PART_ListPanel, isDown ? 2 : 0);
        Grid.SetColumn(PART_ListPanel, isDown ? 0 : 2);

        PART_Divider.Width = isDown ? double.NaN : 1;
        PART_Divider.Height = isDown ? 1 : double.NaN;
        PART_Divider.Margin = isDown
            ? new Thickness(0, DIVIDER_MARGIN_DOWN)
            : new Thickness(DIVIDER_MARGIN_ACROSS, 0);
        PART_ScreenMap.Height = isDown ? MAP_HEIGHT : mapHeightAcross;
    }

    private void StartDrag(ProjectorWindow window, Control btnName, PointerPressedEventArgs e)
    {
        var isLeftButton = e.GetCurrentPoint(btnName).Properties.IsLeftButtonPressed;
        if (!isLeftButton) return;

        _dragWindow = window;
        _dragSourceScreen = window.GetScreen();
        _dragStart = e.GetPosition(this);
        _isDragging = false;

        e.Pointer.Capture(btnName);
    }

    private void ContinueDrag(PointerEventArgs e)
    {
        if (_dragWindow is null) return;

        if (!_isDragging)
        {
            var moved = e.GetPosition(this) - _dragStart;
            var isFarEnough = Math.Abs(moved.X) > DRAG_THRESHOLD || Math.Abs(moved.Y) > DRAG_THRESHOLD;
            if (!isFarEnough) return;

            _isDragging = true;
            ShowDragGhost(_dragWindow);
        }

        var ghostPosition = e.GetPosition(PART_DragLayer);
        Canvas.SetLeft(PART_DragGhost, ghostPosition.X + DRAG_GHOST_OFFSET);
        Canvas.SetTop(PART_DragGhost, ghostPosition.Y + DRAG_GHOST_OFFSET);

        var mapPosition = e.GetPosition(PART_ScreenMap);
        var screen = PART_ScreenMap.GetScreenAt(mapPosition);
        var isSourceScreen = screen is not null && screen == _dragSourceScreen;

        PART_ScreenMap.DropTargetScreen = isSourceScreen ? null : screen;
    }

    private async void DropDraggedProjector()
    {
        var window = _dragWindow;
        var target = _isDragging ? PART_ScreenMap.DropTargetScreen : null;
        EndDrag();

        if (_manager is null) return;
        if (window is null || target is null) return;
        if (target == window.GetScreen()) return;

        await _manager.MoveProjectorAsync(window, target);
    }

    private void ShowDragGhost(ProjectorWindow window)
    {
        var accent = Core.AccentColor;

        PART_DragGhost.Background = accent.ToBrush();
        PART_DragGhostText.Foreground = accent.InvertBlackOrWhite().ToBrush();
        PART_DragGhostText.Text = Core.Lang[LangId.Tool_Projector_WindowTitle, window.Number];
        PART_DragGhost.IsVisible = true;

        PART_ScreenMap.SelectedProjectorNumber = 0;
    }

    private void EndDrag()
    {
        _dragWindow = null;
        _dragSourceScreen = null;
        _isDragging = false;
        PART_DragGhost.IsVisible = false;
        PART_ScreenMap.DropTargetScreen = null;
    }

    private void OpenLayoutMenu(Screen screen)
    {
        if (_manager is null) return;

        var windows = _manager.GetWindowsOnScreen(screen);
        if (windows.Count == 0) return;

        _layoutScreen = screen;
        BuildLayoutOptions(screen);

        PART_LayoutPopup.PlacementRect = PART_ScreenMap.GetScreenBounds(screen);
        PART_LayoutPopup.IsOpen = true;
    }

    private void BuildLayoutOptions(Screen screen)
    {
        PART_LayoutOptions.Children.Clear();
        _layoutOptions.Clear();

        var screens = _manager!.GetOrderedScreens();
        PART_LblLayout.LangParams = IndexOfScreen(screens, screen) + 1;

        _layoutNumbers = GetTileNumbers(screen);
        var aspectRatio = ProjectorLayout.GetAspectRatio(screen.GetActualWorkingArea());

        AddLayoutOption(null, aspectRatio);
        foreach (var layout in ProjectorLayout.GetLayouts(_layoutNumbers.Length, aspectRatio))
        {
            AddLayoutOption(layout, aspectRatio);
        }

        PART_LayoutOptions.Columns = Math.Min(MAX_LAYOUT_COLUMNS, _layoutOptions.Count);
        UpdateLayoutOptionStates();
    }

    private void AddLayoutOption(ProjectorLayout? layout, double aspectRatio)
    {
        var thumbnail = new LayoutThumbnailControl(layout, _layoutNumbers, aspectRatio);
        var button = new PhToolButton
        {
            Padding = new Thickness(6),
            Focusable = false,
            Content = thumbnail,
        };
        button.Click += async (_, _) => await ApplyLayoutAsync(layout);

        PART_LayoutOptions.Children.Add(button);
        _layoutOptions.Add(new LayoutOption(layout, thumbnail));
    }

    private async Task ApplyLayoutAsync(ProjectorLayout? layout)
    {
        var screen = _layoutScreen;
        PART_LayoutPopup.IsOpen = false;

        if (_manager is null) return;
        if (screen is null) return;

        if (layout is null) await _manager.RestoreDefaultAsync(screen);
        else await _manager.ArrangeAsync(screen, layout);
    }

    private void SyncLayoutMenu()
    {
        if (!PART_LayoutPopup.IsOpen) return;
        if (_layoutScreen is not { } screen) return;

        var windows = _manager!.GetWindowsOnScreen(screen);
        if (windows.Count == 0)
        {
            PART_LayoutPopup.IsOpen = false;
            return;
        }

        var projectorNumbers = GetTileNumbers(screen);
        var isSameProjectors = projectorNumbers.SequenceEqual(_layoutNumbers);
        if (isSameProjectors) UpdateLayoutOptionStates();
        else BuildLayoutOptions(screen);
    }

    private int[] GetTileNumbers(Screen screen)
    {
        return _manager!.GetTileSet(screen)
            .Select(w => w.Number)
            .ToArray();
    }

    private void UpdateLayoutOptionStates()
    {
        if (_manager is null) return;
        if (_layoutScreen is not { } screen) return;

        var windows = _manager.GetWindowsOnScreen(screen);
        var activeLayout = _manager.GetActiveLayout(screen);
        var isDefault = windows.All(w => w.Tile is null);

        foreach (var option in _layoutOptions)
        {
            option.Thumbnail.IsActive = option.Layout is null ? isDefault : option.Layout == activeLayout;
        }
    }

    private sealed record ProjectorRow(ProjectorWindow Window, PhButton BtnName, ComboBox CmbMonitor, ComboBox CmbMode,
        ComboBox CmbZoom, PhColorPickerControl ColorPicker, PhToolButton BtnClose);

    private sealed record LayoutOption(ProjectorLayout? Layout, LayoutThumbnailControl Thumbnail);

}
