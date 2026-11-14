using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.Tools;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using PhotoViewer.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class MainWindow : PhWindow
{
    private readonly AppStatusInfo _status;
    private bool _isClosingHandled;

    private const double DEFAULT_WIDTH = 800;
    private const double DEFAULT_HEIGHT = 500;
    private const double MIN_RESTORE_WIDTH = 200;
    private const double MIN_RESTORE_HEIGHT = 100;

    public MainWindowModel VM => (MainWindowModel)DataContext!;

    public MainWindow()
    {
        StartupTrace.Mark("MainWindow:InitComponent:begin");
        InitializeComponent();
        StartupTrace.Mark("MainWindow:InitComponent:end");
        _status = new AppStatusInfo(PART_MainView.PART_Viewer);

        RestoreWindowBounds(Core.Config.MainWindowBounds,
            new(DEFAULT_WIDTH, DEFAULT_HEIGHT),
            new(MIN_RESTORE_WIDTH, MIN_RESTORE_HEIGHT));

        if (!Core.Config.EnableWindowFit)
        {
            if (Core.Config.EnableMainWindowMaximized) WindowState = WindowState.Maximized;
        }

        if (Core.Config.ZoomMode == UI.Viewer.ZoomMode.LockZoom)
        {
            PART_MainView.PART_Viewer.ZoomFactor = Core.Config.ZoomLockValue / 100f;
        }

        Core.AppInstance.InstanceInvoked += AppInstance_InstanceInvoked;
    }

    protected override async void OnOpened(EventArgs e)
    {
        StartupTrace.Mark("MainWindow:opened");
        base.OnOpened(e);

        if (Core.Config.EnableFullScreen)
        {
            _ = await Core.API.RunApiAsync(API.IG_ToggleFullScreen, "true");
        }
        else if (Core.Config.EnableWindowFit)
        {
            _ = await Core.API.RunApiAsync(API.IG_ToggleWindowFit, "true");
        }

        Core.UpdateDestColorProfile();

        _ = await Core.API.RunApiAsync(API.IG_OpenTool, Core.Config.LastOpenedTool);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        StartupTrace.Mark("Hotkeys:register:begin");
        Core.API.RegisterHotkeys();
        StartupTrace.Mark("Hotkeys:register:end");

        if (OperatingSystem.IsMacOS())
        {
            NativeMenu.SetMenu(this, PART_MainView.PART_Toolbar.BuildNativeWindowMenu());
        }

        _status.Changed += Status_Changed;
        PART_MainView.PART_Toolbar.ItemClicked += PART_Toolbar_ItemClicked;
        PART_MainView.PART_Gallery.ItemClicked += PART_Gallery_ItemClicked;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isClosingHandled)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        _isClosingHandled = true;

        if (Photo.PendingSaveCount > 0)
        {
            _ = PART_MainView.PART_Message.ShowAsync(Core.Photos.CurrentFilePath,
                Core.Lang[LangId.Menu_MnuSave_Saving], durationMs: 0);

            await Photo.WaitForPendingSavesAsync(TimeSpan.FromMinutes(2));
        }

        _status.Changed -= Status_Changed;
        _status.Dispose();

        PART_MainView.PART_Toolbar.ItemClicked -= PART_Toolbar_ItemClicked;
        PART_MainView.PART_Gallery.ItemClicked -= PART_Gallery_ItemClicked;

        try
        {
            Core.API?.IG_ToggleSlideshow(false);

            ProjectorToolControl.Manager?.CloseAll();

            await Core.ToolRegistry.ExternalTools.StopAllAsync();

            await SaveConfigOnClosingAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] Closing failed: {ex}");
        }

        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        Core.Dispose();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        if (e.Source is TextBox
            or NumericUpDown
            or MaskedTextBox
            or AutoCompleteBox) return;

        var hk = new Hotkey(e);
        if (hk.IsSame(Key.Escape) && Core.Slideshow?.IsRunning == true)
        {
            _ = await Core.API.RunApiAsync(API.IG_ToggleSlideshow, "false");
            e.Handled = true;
            return;
        }

        await Core.API.HandleKeyDownAsync(e);
        if (e.Handled) return;
    }

    protected override async void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Handled) return;

        await Core.API.HandleKeyUpAsync(e);
        if (e.Handled) return;
    }

    private async void AppInstance_InstanceInvoked(AppInstance sender, InstanceInvokedEventArgs e)
    {
        if (e.Command.Equals(AppCmds.SINGLE_INSTANCE))
        {
            RestoreAndActivate();

            var modulePath = Core.Args.ElementAtOrDefault(0) ?? string.Empty;
            Core.Args = [modulePath, .. e.Arguments];
            Core.UpdateInitImagePath();

            Config.ApplyCliOverrides(Core.Config, e.Arguments);

            ServiceProviders.FeatureManager.Refresh();

            _ = await Core.API.RunApiAsync(API.IG_OpenPath, Core.InputImagePathFromArgs);

            Activate();
            Topmost = true;
            Topmost = Core.Config.EnableWindowTopMost;
        }
    }

    private void Status_Changed(object? sender, EventArgs e)
    {
        var items = _status.GetItems();

        VM.Title = AppStatusInfo.ToText(items);
        PART_MainView.PART_ImageInfoOverlay.SetItems(items);
    }

    private void PART_Toolbar_ItemClicked(object sender, ToolbarItemClickEventArgs e)
    {
        _ = Core.API.RunActionAsync(e.VM.OnClick);
    }

    private async void PART_Gallery_ItemClicked(GalleryItem sender, GalleryItemClickEventArgs e)
    {
        var photoIndex = Core.Photos.IndexOf(sender.VM.FilePath);
        _ = await Core.API.RunApiAsync(API.IG_ViewByIndex, photoIndex.ToString());
    }

    public WindowLayoutSnapshot CaptureWindowLayout()
    {
        Rect? bounds = WindowedBounds is { } b
            && b.Width >= MIN_RESTORE_WIDTH
            && b.Height >= MIN_RESTORE_HEIGHT ? b : null;

        return new WindowLayoutSnapshot
        {
            IsMaximized = RestorableWindowState == WindowState.Maximized,
            Bounds = bounds,
            ShowToolbar = Core.Config.ShowToolbar,
            ShowGallery = Core.Config.ShowGallery,
            IsFrameless = Core.Config.EnableFrameless,
            IsWindowFit = Core.Config.EnableWindowFit,
        };
    }

    internal async Task<bool> CaptureAndSaveConfigAsync()
    {
        Core.Config.EnableFullScreen = RestorableWindowState == WindowState.FullScreen;

        var layout = Core.API.PreFullScreenLayout ?? CaptureWindowLayout();

        Core.Config.EnableMainWindowMaximized = layout.IsMaximized;
        Core.Config.ShowToolbar = layout.ShowToolbar;
        Core.Config.ShowGallery = layout.ShowGallery;
        Core.Config.EnableFrameless = layout.IsFrameless;
        Core.Config.EnableWindowFit = layout.IsWindowFit;
        if (layout.Bounds is { } bounds) Core.Config.MainWindowBounds = bounds;

        Core.Config.ZoomLockValue = PART_MainView.PART_Viewer.ZoomFactor * 100f;
        Core.Config.LastSeenImagePath = Core.Config.EnableLastSeenImage
            ? Core.Photos.CurrentFilePath
            : string.Empty;

        if (PART_MainView.PART_ToolHost.Tool is ITool toolToSave)
        {
            ToolRegistry.SaveToolSettings(toolToSave);
        }

        return await Core.Config.SaveAsync();
    }

    private async Task SaveConfigOnClosingAsync()
    {
        Hide();
        App.SettingsWindow?.Hide();

        var taskConfig = CaptureAndSaveConfigAsync();

        if (Clipboard is not null)
        {
            try
            {
                var ownData = await Clipboard.TryGetInProcessDataAsync();
                if (ownData is not null)
                {
                    await Clipboard.FlushAsync();
                }
            }
            catch { }
        }

        await taskConfig;

        try
        {
            var tempDir = BHelper.ConfigDir(Dir.Temporary);
            Directory.Delete(tempDir, true);
        }
        catch { }
    }

}
