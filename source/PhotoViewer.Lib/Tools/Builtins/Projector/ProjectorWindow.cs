using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer;
using PhotoViewer.UI.Windowing;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

public enum ProjectorWindowMode
{
    FullScreen,
    Maximized,
    Normal,

    Tiled,
}

public sealed class ProjectorWindow : PhWindow
{
    private const double NORMAL_SIZE_RATIO = 0.6;

    private const int MOVE_SETTLE_MS = 50;

    private const int MAC_FULL_SCREEN_EXIT_MS = 800;

    private readonly IdleCursorHider _cursorHider;
    private readonly Border _host;
    private readonly ProjectorPointerControl _pointer = new();

    private Point? _pointerPoint;

    public event EventHandler? LayoutChanged;

    public int Number { get; }

    public ViewerControl Viewer { get; }

    public ProjectorWindowMode Mode { get; private set; } = ProjectorWindowMode.Normal;

    public ProjectorWindowMode PreferredMode { get; set; } = ProjectorWindowMode.FullScreen;

    public ProjectorTile? Tile { get; private set; }

    public Screen? TileScreen { get; private set; }

    public static Color DefaultBackgroundColor
    {
        get
        {
            var brush = Resx.Get<IBrush?>(ResxId.IG_ProjectorBackgroundBrush);
            if (brush is ISolidColorBrush solidBrush) return solidBrush.Color;

            return Colors.Black;
        }
    }

    public Color? BackgroundColor
    {
        get;
        set
        {
            field = value;
            ApplyBackground();
        }
    }

    public ProjectorWindow(int number, ViewerControl source)
    {
        Number = number;
        WindowStartupLocation = WindowStartupLocation.Manual;

        ShowActivated = false;
        MinWidth = 160;
        MinHeight = 90;

        Viewer = new ViewerControl
        {
            MirrorSource = source,
            IsInteractive = false,
            EnableNavButtons = false,
            PanMargin = 0,
            ZoomMode = GetStartZoomMode(),
            CheckerboardMode = Core.Config.CheckerboardMode,
            InterpolationScaleDown = Core.Config.ImageInterpolationScaleDown,
            InterpolationScaleUp = Core.Config.ImageInterpolationScaleUp,
        };

        var pointerLayer = new Canvas
        {
            IsHitTestVisible = false,
            ClipToBounds = true,
            Children = { _pointer },
        };
        _host = new Border
        {
            Child = new Panel { Children = { Viewer, pointerLayer } },
        };
        ApplyBackground();
        Content = _host;

        _cursorHider = new IdleCursorHider(this, Viewer);

        PositionChanged += ProjectorWindow_PositionChanged;
        Viewer.RenderStateChanged += Viewer_RenderStateChanged;
        Core.Config.PropertyChanged += Config_PropertyChanged;
        _ = UpdateWindowIconAsync();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _cursorHider.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        PositionChanged -= ProjectorWindow_PositionChanged;
        Viewer.RenderStateChanged -= Viewer_RenderStateChanged;
        Core.Config.PropertyChanged -= Config_PropertyChanged;
        _cursorHider.Dispose();
        Viewer.MirrorSource = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != WindowStateProperty) return;

        var state = (WindowState)e.NewValue!;
        if (state is WindowState.FullScreen or WindowState.Maximized) LeaveTile();

        if (state == WindowState.FullScreen) Mode = ProjectorWindowMode.FullScreen;
        else if (state == WindowState.Maximized) Mode = ProjectorWindowMode.Maximized;
        else if (state == WindowState.Normal) Mode = Tile is null ? ProjectorWindowMode.Normal : ProjectorWindowMode.Tiled;

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ProjectorWindow_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Viewer_RenderStateChanged(ViewerControl sender, EventArgs e)
    {
        if (_pointerPoint is null) return;

        Dispatcher.UIThread.Post(PlacePointer, DispatcherPriority.Background);
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = $"{Core.Lang[LangId.Tool_Projector_WindowTitle, Number]} - {BHelper.AppDisplayName}";
    }

    protected override void OnIgActivated(EventArgs e)
    {
        base.OnIgActivated(e);

        if (Mode == ProjectorWindowMode.Normal) return;

        Dispatcher.UIThread.Post(() =>
        {
            var mainWindow = App.MainWindow;
            var isMainMinimized = mainWindow.WindowState == WindowState.Minimized;
            if (isMainMinimized || !mainWindow.IsVisible) return;

            mainWindow.Activate();
        }, DispatcherPriority.Background);
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Config.ImageInterpolationScaleDown))
        {
            Viewer.InterpolationScaleDown = Core.Config.ImageInterpolationScaleDown;
        }
        else if (e.PropertyName == nameof(Config.ImageInterpolationScaleUp))
        {
            Viewer.InterpolationScaleUp = Core.Config.ImageInterpolationScaleUp;
        }
        else if (e.PropertyName == nameof(Config.CheckerboardMode))
        {
            Viewer.CheckerboardMode = Core.Config.CheckerboardMode;
        }
    }

    public async Task ShowOnScreenAsync(Screen screen, ProjectorWindowMode mode)
    {
        PlaceOnScreen(screen);
        Show();

        await Task.Delay(MOVE_SETTLE_MS);
        ApplyMode(mode);

        Activate();
    }

    public async Task MoveToScreenAsync(Screen screen, ProjectorWindowMode mode)
    {
        LeaveTile();
        await RestoreNormalStateAsync();

        PlaceOnScreen(screen);
        await Task.Delay(MOVE_SETTLE_MS);

        var currentScreen = Screens.ScreenFromWindow(this);
        if (currentScreen != screen)
        {
            PlaceOnScreen(screen);
            await Task.Delay(MOVE_SETTLE_MS);
        }

        ApplyMode(mode);
    }

    public async Task ShowInTileAsync(Screen screen, ProjectorTile tile)
    {
        await RestoreNormalStateAsync();

        Tile = tile;
        TileScreen = screen;
        Mode = ProjectorWindowMode.Tiled;
        WindowDecorations = WindowDecorations.None;
        PlaceInTile(screen, tile);
        if (!IsVisible) Show();

        await Task.Delay(MOVE_SETTLE_MS);
        PlaceInTile(screen, tile);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    public Screen? GetScreen() => Screens.ScreenFromWindow(this);

    public void ShowPointer(Point relativePoint)
    {
        _pointerPoint = relativePoint;
        Viewer.MirrorRevealPoint = relativePoint;
        PlacePointer();
    }

    public void HidePointer()
    {
        _pointerPoint = null;
        Viewer.MirrorRevealPoint = null;
        _pointer.Hide();
    }

    public void PlayPointerClick() => _pointer.PlayClick();

    private void PlacePointer()
    {
        if (_pointerPoint is not { } relativePoint) return;

        var bitmapSize = Viewer.BitmapSize;
        if (bitmapSize.IsEmpty)
        {
            _pointer.Hide();
            return;
        }

        var sourcePoint = new Point(relativePoint.X * bitmapSize.Width, relativePoint.Y * bitmapSize.Height);
        var clientPoint = Viewer.PointSourceToClient(sourcePoint);
        _pointer.MoveTo(clientPoint);
    }

    private void PlaceOnScreen(Screen screen)
    {
        var workArea = screen.GetActualWorkingArea();
        var pixelWidth = (int)(workArea.Width * NORMAL_SIZE_RATIO);
        var pixelHeight = (int)(workArea.Height * NORMAL_SIZE_RATIO);

        Position = new PixelPoint(
            workArea.X + (workArea.Width - pixelWidth) / 2,
            workArea.Y + (workArea.Height - pixelHeight) / 2);
        Width = pixelWidth / screen.Scaling;
        Height = pixelHeight / screen.Scaling;
    }

    private void PlaceInTile(Screen screen, ProjectorTile tile)
    {
        var bounds = tile.Layout.GetCellBounds(screen.GetActualWorkingArea(), tile.Cell);
        Position = bounds.Position;

        Width = bounds.Width / screen.Scaling;
        Height = bounds.Height / screen.Scaling;
    }

    private void LeaveTile()
    {
        if (Tile is null) return;

        Tile = null;
        TileScreen = null;
        WindowDecorations = WindowDecorations.Full;
    }

    private async Task RestoreNormalStateAsync()
    {
        if (WindowState == WindowState.Normal) return;

        var wasFullScreen = WindowState == WindowState.FullScreen;
        WindowState = WindowState.Normal;

        var isMacFullScreenExit = wasFullScreen && BHelper.OS == OSType.Mac;
        await Task.Delay(isMacFullScreenExit ? MAC_FULL_SCREEN_EXIT_MS : MOVE_SETTLE_MS);
    }

    private static ZoomMode GetStartZoomMode()
    {
        var mode = Core.Config.ZoomMode;
        if (mode == ZoomMode.LockZoom) return ZoomMode.AutoZoom;

        return mode;
    }

    private void ApplyBackground()
    {
        _host.ClearValue(Border.BackgroundProperty);

        if (BackgroundColor is { } color)
        {
            _host.Background = color.ToBrush();
            return;
        }

        _host[!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_ProjectorBackgroundBrush);
    }

    private void ApplyMode(ProjectorWindowMode mode)
    {
        Mode = mode;
        WindowState = mode switch
        {
            ProjectorWindowMode.FullScreen => WindowState.FullScreen,
            ProjectorWindowMode.Maximized => WindowState.Maximized,
            _ => WindowState.Normal,
        };
    }

}
