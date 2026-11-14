using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

public sealed class ProjectorManager : PhDisposable
{

    public const string TOOL_ID = "Tool_Projector";

    private const string SLEEP_GUARD_OWNER = "projector";

    private readonly List<ProjectorWindow> _windows = [];
    private readonly Screens _screens;
    private bool _isTrackingPointer;

    private Point? _mainPointerPoint;

    private InterlockedBool _isPointerRefreshPosted;

    public event EventHandler? Changed;

    public ProjectorConfig Config { get; private set; } = new();

    public IReadOnlyList<ProjectorWindow> Windows => _windows;

    public IReadOnlyList<Screen> AllScreens => _screens.All;

    public bool IsViewSynced => Config.EnableViewSync;

    public bool IsPointerShown => Config.ShowPointer;

    public ProjectorManager()
    {
        LoadConfig();

        _screens = App.MainWindow.Screens;
        _screens.Changed += Screens_Changed;

        if (IsPointerShown) AttachPointerTracking();
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        _screens.Changed -= Screens_Changed;
        DetachPointerTracking();
        CloseAll();
    }

    public async Task<ProjectorWindow?> AddProjectorAsync(Screen? screen = null)
    {
        if (IsDisposed) return null;

        var number = GetFreeNumber();
        var target = screen ?? PickFreeScreen();
        if (target is null) return null;

        var mode = GetModeForScreen(target, ProjectorWindowMode.FullScreen);

        var window = new ProjectorWindow(number, GetMainViewer())
        {
            BackgroundColor = GetSavedBackgroundColor(number),
        };
        window.Viewer.EnableMirrorSync = IsViewSynced;
        window.Closed += Window_Closed;
        window.LayoutChanged += Window_LayoutChanged;

        _windows.Add(window);
        _windows.Sort((a, b) => a.Number.CompareTo(b.Number));

        SleepGuard.Acquire(SLEEP_GUARD_OWNER, $"{BHelper.AppDisplayName} projector");

        await window.ShowOnScreenAsync(target, mode);
        OnChanged();

        return window;
    }

    public async Task MoveProjectorAsync(ProjectorWindow window, Screen screen)
    {
        var mode = GetModeForScreen(screen, window.PreferredMode);

        await PlaceProjectorAsync(window, screen, mode);
    }

    public async Task SetWindowModeAsync(ProjectorWindow window, ProjectorWindowMode mode)
    {
        if (mode == ProjectorWindowMode.Tiled)
        {
            await TileProjectorAsync(window);
            return;
        }

        var screen = window.GetScreen() ?? PickFreeScreen();
        if (screen is null) return;

        window.PreferredMode = mode;
        await PlaceProjectorAsync(window, screen, mode);
    }

    public async Task ArrangeAsync(Screen screen, ProjectorLayout layout, ProjectorWindow? window = null)
    {
        if (IsDisposed) return;

        var tileSet = GetTileSet(screen, window);
        if (tileSet.Count == 0) return;
        if (tileSet.Count > layout.UsedCells.Count) return;

        var tileTasks = tileSet.Select((w, i) => w.ShowInTileAsync(screen, new ProjectorTile(layout, layout.UsedCells[i])));

        var untileTasks = GetWindowsOnScreen(screen)
            .Where(w => w.Tile is not null)
            .Where(w => !tileSet.Contains(w))
            .Select(w => PlaceProjectorAsync(w, screen, GetModeForScreen(screen, w.PreferredMode)));

        await Task.WhenAll(tileTasks.Concat(untileTasks));
        OnChanged();
    }

    public async Task RestoreDefaultAsync(Screen screen)
    {
        if (IsDisposed) return;

        var restoreTasks = GetWindowsOnScreen(screen)
            .Where(w => w.Tile is not null)
            .Select(w => PlaceProjectorAsync(w, screen, GetModeForScreen(screen, w.PreferredMode)));

        await Task.WhenAll(restoreTasks);
    }

    public IReadOnlyList<ProjectorWindow> GetWindowsOnScreen(Screen screen)
    {
        return _windows
            .Where(w => w.GetScreen() == screen)
            .ToArray();
    }

    public IReadOnlyList<ProjectorWindow> GetTileSet(Screen screen, ProjectorWindow? window = null)
    {
        var picked = GetWindowsOnScreen(screen)
            .OrderBy(w => w != window)
            .ThenBy(w => w.Tile is null)
            .ThenBy(w => w.Number)
            .Take(ProjectorLayout.MAX_TILED_PROJECTORS);

        return picked
            .OrderBy(w => w.Number)
            .ToArray();
    }

    public ProjectorLayout? GetActiveLayout(Screen screen)
    {
        var tiledWindows = GetWindowsOnScreen(screen)
            .Where(w => w.Tile is not null)
            .ToArray();

        var layout = tiledWindows.FirstOrDefault()?.Tile?.Layout;
        if (layout is null) return null;

        var isSameGrid = tiledWindows.All(w => w.Tile?.Layout == layout);
        if (!isSameGrid) return null;

        return layout;
    }

    public void SetZoomMode(ProjectorWindow window, ZoomMode mode)
    {
        if (IsDisposed) return;

        window.Viewer.ZoomMode = mode;
        OnChanged();
    }

    public void SetBackgroundColor(ProjectorWindow window, Color? color)
    {
        if (IsDisposed) return;

        window.BackgroundColor = color;

        var colors = Config.BackgroundColors;
        var index = window.Number - 1;
        while (colors.Count <= index)
        {
            colors.Add(string.Empty);
        }
        colors[index] = color?.ToHex() ?? string.Empty;

        SaveConfig();
        OnChanged();
    }

    public static void CloseProjector(ProjectorWindow window)
    {
        window.Close();
    }

    public void CloseAll()
    {
        foreach (var window in _windows.ToArray())
        {
            window.Close();
        }
    }

    public void SetViewSync(bool enabled)
    {

        Config.EnableViewSync = enabled;
        foreach (var window in _windows)
        {
            window.Viewer.EnableMirrorSync = enabled;
        }

        SaveConfig();
        OnChanged();
    }

    public void SetShowPointer(bool enabled)
    {

        Config.ShowPointer = enabled;
        if (enabled) AttachPointerTracking();
        else DetachPointerTracking();

        SaveConfig();
        OnChanged();
    }

    public IReadOnlyList<Screen> GetOrderedScreens()
    {
        return _screens.All
            .OrderBy(s => s.Bounds.X)
            .ThenBy(s => s.Bounds.Y)
            .ToArray();
    }

    public Screen? GetMainWindowScreen() => _screens.ScreenFromWindow(App.MainWindow);

    public JsonElement SaveSettings()
    {
        return JsonSerializer.SerializeToElement(Config, ProjectorConfigJsonContext.Default.ProjectorConfig);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (sender is not ProjectorWindow window) return;

        window.Closed -= Window_Closed;
        window.LayoutChanged -= Window_LayoutChanged;
        _windows.Remove(window);

        if (_windows.Count == 0)
        {
            SleepGuard.Release(SLEEP_GUARD_OWNER);
        }

        OnChanged();
    }

    private void Window_LayoutChanged(object? sender, EventArgs e)
    {
        OnChanged();
    }

    private void Screens_Changed(object? sender, EventArgs e)
    {
        _ = RetileAsync();
        OnChanged();
    }

    private void MainViewer_PointerMoved(object? sender, PointerEventArgs e)
    {
        var viewer = GetMainViewer();
        var clientPoint = e.GetPosition(viewer);
        _mainPointerPoint = clientPoint;

        UpdatePointers(viewer, clientPoint);
    }

    private void MainViewer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var viewer = GetMainViewer();
        var isLeftButton = e.GetCurrentPoint(viewer).Properties.IsLeftButtonPressed;
        if (!isLeftButton) return;

        foreach (var window in _windows)
        {
            window.PlayPointerClick();
        }
    }

    private void MainViewer_PointerExited(object? sender, PointerEventArgs e)
    {
        _mainPointerPoint = null;
        HidePointers();
    }

    private void MainViewer_RenderStateChanged(ViewerControl sender, EventArgs e)
    {
        var isFirstRequest = _isPointerRefreshPosted.SetTrue();
        if (!isFirstRequest) return;

        Dispatcher.UIThread.Post(RefreshPointers, DispatcherPriority.Background);
    }

    private void AttachPointerTracking()
    {
        if (_isTrackingPointer) return;
        _isTrackingPointer = true;

        var viewer = GetMainViewer();
        viewer.AddHandler(InputElement.PointerMovedEvent, MainViewer_PointerMoved, RoutingStrategies.Bubble, true);
        viewer.AddHandler(InputElement.PointerPressedEvent, MainViewer_PointerPressed, RoutingStrategies.Bubble, true);
        viewer.PointerExited += MainViewer_PointerExited;
        viewer.RenderStateChanged += MainViewer_RenderStateChanged;
    }

    private void DetachPointerTracking()
    {
        if (!_isTrackingPointer) return;
        _isTrackingPointer = false;

        var viewer = GetMainViewer();
        viewer.RemoveHandler(InputElement.PointerMovedEvent, MainViewer_PointerMoved);
        viewer.RemoveHandler(InputElement.PointerPressedEvent, MainViewer_PointerPressed);
        viewer.PointerExited -= MainViewer_PointerExited;
        viewer.RenderStateChanged -= MainViewer_RenderStateChanged;

        _mainPointerPoint = null;
        HidePointers();
    }

    private void UpdatePointers(ViewerControl viewer, Point clientPoint)
    {
        var bitmapSize = viewer.BitmapSize;
        if (bitmapSize.IsEmpty)
        {
            HidePointers();
            return;
        }

        var sourcePoint = viewer.PointClientToSource(clientPoint);
        var relativePoint = new Point(sourcePoint.X / bitmapSize.Width, sourcePoint.Y / bitmapSize.Height);

        var transform = Core.ImageTransform;
        var hasFlips = transform.Flips != FlipOptions.None;
        var hasRotation = transform.Rotation % 360 != 0;
        var isTurned = hasFlips || hasRotation;

        foreach (var window in _windows)
        {
            var canPoint = CanPointOn(window, isTurned);
            if (canPoint) window.ShowPointer(relativePoint);
            else window.HidePointer();
        }
    }

    private void RefreshPointers()
    {
        _ = _isPointerRefreshPosted.SetFalse();
        if (_mainPointerPoint is not { } clientPoint) return;

        UpdatePointers(GetMainViewer(), clientPoint);
    }

    private static bool CanPointOn(ProjectorWindow window, bool isTurned)
    {
        if (window.Viewer.EnableMirrorSync) return true;

        return !isTurned;
    }

    private void HidePointers()
    {
        foreach (var window in _windows)
        {
            window.HidePointer();
        }
    }

    private async Task RetileAsync()
    {
        foreach (var window in _windows.ToArray())
        {
            if (IsDisposed) return;
            if (window.Tile is not { } tile) continue;

            var isOpen = _windows.Contains(window);
            var screen = window.GetScreen();
            var isOnTileScreen = screen is not null && screen == window.TileScreen;
            if (!isOpen || !isOnTileScreen) continue;

            await window.ShowInTileAsync(screen!, tile);
        }
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static ViewerControl GetMainViewer() => App.MainWindow.PART_MainView.PART_Viewer;

    private int GetFreeNumber()
    {
        var number = 1;
        while (_windows.Any(w => w.Number == number))
        {
            number++;
        }

        return number;
    }

    private Screen? PickFreeScreen()
    {
        var mainScreen = GetMainWindowScreen();
        var usedScreens = _windows
            .Select(w => w.GetScreen())
            .OfType<Screen>()
            .ToArray();

        var orderedScreens = GetOrderedScreens();
        var freeScreens = orderedScreens.Where(s => !usedScreens.Contains(s)).ToArray();

        return freeScreens.FirstOrDefault(s => s != mainScreen)
            ?? freeScreens.FirstOrDefault()
            ?? mainScreen
            ?? orderedScreens.FirstOrDefault();
    }

    private async Task PlaceProjectorAsync(ProjectorWindow window, Screen screen, ProjectorWindowMode mode)
    {
        if (IsDisposed) return;

        var isOpen = _windows.Contains(window);
        if (!isOpen) return;

        await window.MoveToScreenAsync(screen, mode);
        OnChanged();
    }

    private ProjectorWindowMode GetModeForScreen(Screen screen, ProjectorWindowMode preferredMode)
    {
        var isMainScreen = screen == GetMainWindowScreen();

        return isMainScreen ? ProjectorWindowMode.Normal : preferredMode;
    }

    private async Task TileProjectorAsync(ProjectorWindow window)
    {
        var screen = window.GetScreen();
        if (screen is null) return;

        var tileSet = GetTileSet(screen, window);
        var tiledLayouts = tileSet
            .Select(w => w.Tile?.Layout)
            .OfType<ProjectorLayout>()
            .Distinct()
            .ToArray();

        var sharedLayout = tiledLayouts.Length == 1 ? tiledLayouts[0] : null;
        var hasRoom = sharedLayout?.UsedCells.Count >= tileSet.Count;
        var aspectRatio = ProjectorLayout.GetAspectRatio(screen.GetActualWorkingArea());
        var layout = hasRoom ? sharedLayout! : ProjectorLayout.GetDefault(tileSet.Count, aspectRatio);

        await ArrangeAsync(screen, layout, window);
    }

    private Color? GetSavedBackgroundColor(int number)
    {
        var index = number - 1;
        var colors = Config.BackgroundColors;
        if (index < 0 || index >= colors.Count) return null;

        var hex = colors[index];
        if (string.IsNullOrWhiteSpace(hex)) return null;

        var color = BHelper.ColorFromHex(hex, Core.AccentColor);
        if (color.A == 0) return null;

        return color;
    }

    private void LoadConfig()
    {
        var hasSettings = Core.Config.ToolSettings.TryGetValue(TOOL_ID, out var jsonEl);
        if (!hasSettings) return;

        try
        {
            var config = jsonEl.Deserialize(ProjectorConfigJsonContext.Default.ProjectorConfig);
            if (config is not null) Config = config;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(ProjectorManager)}.{nameof(LoadConfig)}: {ex.Message}");
        }
    }

    private void SaveConfig()
    {
        ToolRegistry.SetToolSettings(TOOL_ID, SaveSettings());
    }

}
