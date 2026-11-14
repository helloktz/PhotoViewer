using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using D2Phap.FileWatcherEx;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.ServiceProviders.FileSearchService;
using PhotoViewer.Common.Types;
using PhotoViewer.Tools;
using PhotoViewer.UI;
using PhotoViewer.UI.Viewer;
using PhotoViewer.UI.Viewer.Transitions;
using PhotoViewer.UI.Viewer.ZoomAndPan;
using PhotoViewer.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class MainWindowView : PhControl
{
    private static readonly TimeSpan _sequentialRenderTimeout = TimeSpan.FromSeconds(2);

    private int _sequentialLoadDepth;

    private bool _selectLastOnSearch;

    private Thickness _messageMargin;

    public MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    public bool IsPhotoLoadInProgress => Volatile.Read(ref _sequentialLoadDepth) > 0;

    public MainWindowView()
    {
        InitializeComponent();

        PART_Viewer.ContextMenu = new();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        ApplyAppLayout();

        base.OnLoaded(e);

        DragDrop.SetAllowDrop(PART_Viewer, true);
        DragDrop.AddDragOverHandler(PART_Viewer, PART_Viewer_DragOver);
        DragDrop.AddDropHandler(PART_Viewer, PART_Viewer_Drop);

        Core.Config.PropertyChanged += Config_PropertyChanged;
        Core.Photos.FileWatcherChanged += Photos_FileWatcherChanged;
        PART_Viewer.PhotoLoading += PART_Viewer_PhotoLoading;
        PART_Viewer.ZoomChanged += PART_Viewer_ZoomChanged;
        PART_Viewer.NavButtonClicked += PART_Viewer_NavButtonClicked;
        PART_Viewer.ViewerPointerClicked += PART_Viewer_ViewerPointerClicked;
        PART_Viewer.ViewerMouseWheel += PART_Viewer_ViewerMouseWheel;

        PART_ToolHost.PropertyChanged += PART_ToolHost_PropertyChanged;
        UpdateMotionButtonState();

        _messageMargin = PART_Message.Margin;
        PART_ImageInfoOverlay.PropertyChanged += PART_ImageInfoOverlay_PropertyChanged;
        UpdateMessageMargin();

        PART_Viewer.PhotoLoading += Core.Viewer_PhotoLoadingForPlugins;
        PART_Viewer.ViewerPointerMoved += Core.Viewer_PointerMovedForPlugins;
        PART_Viewer.ViewerPointerPressed += Core.Viewer_PointerPressedForPlugins;
        PART_Viewer.SelectionChanged += Core.Viewer_SelectionChangedForPlugins;
        PART_Viewer.PhotoFrameChanged += Core.Viewer_FrameChangedForPlugins;

        LoadImagesFromCmdArgs();

        StartupTrace.Mark("MainView:loaded");
        StartupTrace.Flush();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        DragDrop.RemoveDragOverHandler(PART_Viewer, PART_Viewer_DragOver);
        DragDrop.RemoveDropHandler(PART_Viewer, PART_Viewer_Drop);

        Core.Config.PropertyChanged -= Config_PropertyChanged;
        Core.Photos.FileWatcherChanged -= Photos_FileWatcherChanged;
        PART_Viewer.PhotoLoading -= PART_Viewer_PhotoLoading;
        PART_Viewer.ZoomChanged -= PART_Viewer_ZoomChanged;
        PART_Viewer.NavButtonClicked -= PART_Viewer_NavButtonClicked;
        PART_Viewer.ViewerPointerClicked -= PART_Viewer_ViewerPointerClicked;
        PART_Viewer.ViewerMouseWheel -= PART_Viewer_ViewerMouseWheel;

        PART_ToolHost.PropertyChanged -= PART_ToolHost_PropertyChanged;

        PART_ImageInfoOverlay.PropertyChanged -= PART_ImageInfoOverlay_PropertyChanged;

        PART_Viewer.PhotoLoading -= Core.Viewer_PhotoLoadingForPlugins;
        PART_Viewer.ViewerPointerMoved -= Core.Viewer_PointerMovedForPlugins;
        PART_Viewer.ViewerPointerPressed -= Core.Viewer_PointerPressedForPlugins;
        PART_Viewer.SelectionChanged -= Core.Viewer_SelectionChangedForPlugins;
        PART_Viewer.PhotoFrameChanged -= Core.Viewer_FrameChangedForPlugins;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        UpdateGalleryWidth();
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Config.Layout))
        {
            ApplyAppLayout();
        }
        else if (e.PropertyName == nameof(Config.EnableFileWatcher))
        {
            AppAPIProvider.SetFileWatcher(Core.Config.EnableFileWatcher);
        }
        else if (e.PropertyName is nameof(Config.BackgroundColor)
            or nameof(Config.SlideshowBackgroundColor)
            or nameof(Config.EnableSlideshow))
        {
            Core.UpdateViewerBackgroundBrushResource();
        }
    }

    private void PART_Viewer_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void PART_Viewer_Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not IStorageItem[] sItems) return;

        var paths = sItems.Select(i => i.Path.LocalPath).ToArray();

        if (paths.Length > 1)
        {
            PrepareLoadPhotoList(paths,
                currentFilePath: null, disposeForegroundShell: true, reloadInitPhoto: true);
            return;
        }

        Core.ShellProvider?.ForegroundShell = Core.ShellProvider?.GetForegroundWindowView();
        Core.UpdateInitImagePath(paths[0]);

        _ = await Core.API.RunApiAsync(API.IG_OpenPath, paths[0]);
    }

    private void Photos_FileWatcherChanged(PhotoManager sender, FileWatcherChangedEventArgs e)
    {
        if (e.ChangeType == ChangeType.LOG) return;

        Dispatcher.UIThread.Post(() =>
        {
            switch (e.ChangeType)
            {
                case ChangeType.CREATED:
                    HandleFileWatcher_FilesAdded(e);
                    break;

                case ChangeType.DELETED:
                    HandleFileWatcher_FilesDeleted(e);
                    break;

                case ChangeType.CHANGED:
                    HandleFileWatcher_FilesChanged(e);
                    break;

                case ChangeType.RENAMED:
                    HandleFileWatcher_FilesRenamed(e);
                    break;
            }
        });
    }

    private void HandleFileWatcher_FilesAdded(FileWatcherChangedEventArgs e)
    {
        if (e.FilePaths.Count == 0) return;

        if (Core.Config.EnableAutoOpenNewAddedImage)
        {
            var lastAdded = e.FilePaths[^1];
            var newIndex = Core.Photos.IndexOf(lastAdded);

            if (newIndex >= 0)
            {
                var photo = Core.Photos.Select(newIndex);
                _ = ViewPhotoAsync(photo);
            }
        }
        else
        {
            _ = Core.Photos.Select(Core.Photos.CurrentFilePath);
            PART_Gallery.ScrollToItem(Core.Photos.CurrentIndex);
        }
    }

    private void HandleFileWatcher_FilesDeleted(FileWatcherChangedEventArgs e)
    {
        if (e.FilePaths.Count == 0) return;

        if (!string.IsNullOrEmpty(e.AffectedCurrentFilePath))
        {
            AppAPIProvider.ViewPhotoAfterCurrentRemoved();
        }
        else
        {
            _ = Core.Photos.Select(Core.Photos.CurrentFilePath);
            PART_Gallery.ScrollToItem(Core.Photos.CurrentIndex);
        }

        if (Core.Photos.InitPhoto is not null)
        {
            for (var i = 0; i < e.FilePaths.Count; i++)
            {
                if (string.Equals(e.FilePaths[i], Core.Photos.InitPhoto.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    var dirPath = Path.GetDirectoryName(e.FilePaths[i]) ?? string.Empty;
                    Core.Photos.InitPhoto = !string.IsNullOrEmpty(dirPath)
                        ? new Photo(dirPath)
                        : null;
                    break;
                }
            }
        }
    }

    private void HandleFileWatcher_FilesChanged(FileWatcherChangedEventArgs e)
    {
        if (e.FilePaths.Count == 0) return;

        for (var i = 0; i < e.FilePaths.Count; i++)
        {
            var filePath = e.FilePaths[i];
            var photoIndex = Core.Photos.IndexOf(filePath);
            var photo = Core.Photos.Get(photoIndex);
            if (photo is null) continue;

            PART_Gallery.LoadThumbnail(photoIndex, false);

            if (Core.Photos.IsSelected(filePath))
            {
                _ = PART_Viewer.SetPhotoAsync(photo, new PhotoLoadingOptions
                {
                    ResetZoom = false,
                    UseCache = false,
                    Channels = Core.ColorChannels,
                });
            }
        }
    }

    private static void HandleFileWatcher_FilesRenamed(FileWatcherChangedEventArgs e)
    {
        if (e.FilePaths.Count == 0 || e.OldFilePaths is null) return;

        var currentNeedsRefresh = false;
        for (var i = 0; i < e.FilePaths.Count; i++)
        {
            var newPath = e.FilePaths[i];
            var oldPath = i < e.OldFilePaths.Count ? e.OldFilePaths[i] : null;

            if (Core.Photos.InitPhoto is not null
                && oldPath is not null
                && string.Equals(oldPath, Core.Photos.InitPhoto.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                Core.Photos.InitPhoto = new Photo(newPath);
            }

            if (Core.Photos.IsSelected(newPath))
            {
                currentNeedsRefresh = true;
            }
        }

        if (currentNeedsRefresh)
        {
            _ = Core.Photos.Select(Core.Photos.CurrentIndex);
        }
    }

    private void PART_Viewer_PhotoLoading(ViewerControl sender, PhotoLoadingEventArgs e)
    {

        if (e.Photo.Error is not null)
        {
            var heading = $"{Core.Lang[LangId._PicMain_ErrorText]} 😶";
            var err = BHelper.GetInAppError(e.Photo.Error);

            _ = PART_Message.ShowAsync(err.DebugInfo, heading, err.Details, 0);
        }

        else if (e.State == PhotoState.Preview)
        {
            _ = PART_Message.ShowAsync(Core.Lang[LangId._Loading], durationMs: 0, delayMs: 2000);
        }

        else if (e.State == PhotoState.Loaded)
        {
            _ = PART_Message.ShowAsync(null);
        }

        UpdateMotionButtonState();
    }

    private void PART_ToolHost_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ToolHostControl.PluginContentProperty)
        {
            UpdateMotionButtonState();
        }
    }

    private void PART_ImageInfoOverlay_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        var isPlacementChanged = e.Property == ImageInfoOverlayControl.CoveredHeightProperty
            || e.Property == VerticalAlignmentProperty;
        if (!isPlacementChanged) return;

        UpdateMessageMargin();
    }

    private void PART_Viewer_ZoomChanged(ViewerControl sender, ViewerZoomEventArgs e)
    {
        if (Core.Config.EnableWindowFit
            && e.ChangeSource != ZoomChangeSource.SizeChanged
            && e.ChangeSource != ZoomChangeSource.FrameChanged
            && (e.IsManualZoom || e.IsZoomModeChange))
        {
            AppAPIProvider.ApplyWindowFitMode(e.ChangeSource == ZoomChangeSource.ZoomMode);
        }
    }

    private async void PART_Viewer_NavButtonClicked(ViewerControl sender, NavButtonClickedEventArgs e)
    {
        if (e.Direction == NavButtonDirection.Right)
        {
            _ = await Core.API.RunApiAsync(API.IG_ViewNext);
        }
        else
        {
            _ = await Core.API.RunApiAsync(API.IG_ViewPrevious);
        }
    }

    private async void PART_Viewer_ViewerPointerClicked(ViewerControl sender, ViewerPointerClickEventArgs e)
    {
        var action = Core.Config.MouseClickActions.GetValueOrDefault(e.ClickEvent);

        if (action is null)
        {
            action = Config.DefaultMouseClickActions.GetValueOrDefault(e.ClickEvent);
        }

        await Core.API!.RunActionAsync(action);
    }

    private async void PART_Viewer_ViewerMouseWheel(ViewerControl sender, ViewerMouseWheelEventArgs e)
    {
        if (!Core.Config.MouseWheelActions.TryGetValue(e.WheelEvent, out var wheelAction))
        {
            _ = Config.DefaultMouseWheelActions.TryGetValue(e.WheelEvent, out wheelAction);
        }

        switch (wheelAction)
        {
            case MouseWheelAction.Zoom:
                if (!FeatureManager.IsZoomLocked())
                    _ = PART_Viewer.ZoomByDeltaToPoint(e.Delta, e.Position);
                break;

            case MouseWheelAction.PanVertically:
                if (!FeatureManager.IsPanLocked())
                    PART_Viewer.PanTo(0, -e.Delta, e.Position);
                break;

            case MouseWheelAction.PanHorizontally:
                if (!FeatureManager.IsPanLocked())
                    PART_Viewer.PanTo(-e.Delta, 0, e.Position);
                break;

            case MouseWheelAction.BrowseImages:
                if (e.Delta > 0) _ = await Core.API.RunApiAsync(API.IG_ViewPrevious);
                else _ = await Core.API.RunApiAsync(API.IG_ViewNext);
                break;

            case MouseWheelAction.DoNothing:
            default:
                break;
        }
    }

    private void PART_GalleryResizer_DragCompleted(object? sender, VectorEventArgs e)
    {
        var panelEl = PART_Gallery.FindVirtualPanel();
        if (panelEl is null) return;

        Core.Config.GalleryColumns = (uint)panelEl.ColumnsPerRow;
    }

    private void BuildViewerContextMenu(ContextMenu mnuContext)
    {
        mnuContext.Items.Clear();

        var hasClipboardImage = Core.ClipboardImage != null;
        var imageNotFound = !File.Exists(Core.Photos.CurrentFilePath);

        if (Core.Slideshow?.IsRunning == true)
        {
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId._MnuPauseResumeSlideshow,
                Command = Core.API.GetApiCommand(API.IG_ToggleSlideshowPlayback),
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId._MnuPauseResumeSlideshow),
            });
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId._MnuExitSlideshow,
                Command = Core.API.GetApiCommand(API.IG_ToggleSlideshow),
                CommandParameter = false,
                HotkeyText = string.Join(", ", [
                    new Hotkey(Key.Escape),
                    AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuSlideshow)
                ]),
            });
            mnuContext.Items.Add(new PhMenuItem
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = Core.Config.EnableSlideshowCountdown,
                LangKey = LangId._MnuToggleCountdown,
                Command = Core.API.GetApiCommand(API.IG_ToggleSlideshowCountdown),
                CommandParameter = !Core.Config.EnableSlideshowCountdown,
            });
            mnuContext.Items.Add("-");
        }

        mnuContext.Items.Add(new PhMenuItem
        {
            LangKey = LangId.Menu_MnuToggleToolbar,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = Core.Config.ShowToolbar,
            Command = Core.API.GetApiCommand(API.IG_ToggleToolbar),
            HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuToggleToolbar),
        });
        mnuContext.Items.Add(new PhMenuItem
        {
            LangKey = LangId.Menu_MnuToggleTopMost,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = Core.Config.EnableWindowTopMost,
            Command = Core.API.GetApiCommand(API.IG_ToggleWindowTopMost),
            HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuToggleTopMost),
        });

        if (!imageNotFound) mnuContext.Items.Add("-");
        if (!imageNotFound && !hasClipboardImage)
        {
            var mnuLoadingOrders = new PhMenuItem
            {
                LangKey = LangId.Menu_MnuLoadingOrders,
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuLoadingOrders),
            };
            mnuContext.Items.Add(mnuLoadingOrders);

            if (Core.ShellProvider is not null && BHelper.OS == OSType.Windows)
            {
                mnuLoadingOrders.Items.Add(new PhMenuItem
                {
                    LangKey = LangId.Settings_EnableExplorerSortOrder,
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = Core.Config.EnableExplorerSortOrder,
                    Command = Core.API.GetApiCommand(API.IG_ToggleExplorerSortOrder),
                    HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Settings_EnableExplorerSortOrder),
                });
                mnuLoadingOrders.Items.Add("-");
            }

            foreach (var orderBy in Enum.GetNames<ImageOrderBy>())
            {
                mnuLoadingOrders.Items.Add(new PhMenuItem
                {
                    LangKey = Lang.GetKey($"{nameof(ImageOrderBy)}_{orderBy}"),
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = Core.Config.ImageLoadingOrder == Enum.Parse<ImageOrderBy>(orderBy),
                    Command = Core.API.GetApiCommand(API.IG_SetLoadingOrderBy),
                    CommandParameter = orderBy,
                });
            }

            mnuLoadingOrders.Items.Add("-");
            foreach (var orderType in Enum.GetNames<ImageOrderType>())
            {
                mnuLoadingOrders.Items.Add(new PhMenuItem
                {
                    LangKey = Lang.GetKey($"{nameof(ImageOrderType)}_{orderType}"),
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = Core.Config.ImageLoadingOrderType == Enum.Parse<ImageOrderType>(orderType),
                    Command = Core.API.GetApiCommand(API.IG_SetLoadingOrderType),
                    CommandParameter = orderType,
                });
            }
        }

        if (!PART_Viewer.IsImageAnimating && (!imageNotFound || hasClipboardImage))
        {
            var mnuChannels = new PhMenuItem
            {
                LangKey = LangId.Menu_MnuViewChannels,
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuViewChannels),
            };
            mnuContext.Items.Add(mnuChannels);

            foreach (var item in PART_Toolbar.PART_MnuViewChannels.Items)
            {
                if (item is not PhMenuItem oriItem) continue;

                var mnuItem = new PhMenuItem
                {
                    Header = oriItem.Header,
                    ToggleType = oriItem.ToggleType,
                    HotkeyText = oriItem.HotkeyText,
                    CommandParameter = oriItem.CommandParameter,
                };
                if (Enum.TryParse<ColorChannels>((string)oriItem.CommandParameter!, true, out var val))
                {
                    mnuItem.IsChecked = Core.ColorChannels.HasFlag(val);
                }
                mnuItem.Click += PART_Toolbar.MainMenu_ViewChannelItem_Click;
                mnuChannels.Items.Add(mnuItem);
            }
        }

        if (!imageNotFound) mnuContext.Items.Add("-");
        if (!imageNotFound || hasClipboardImage)
        {
            var mnuEdit = new PhMenuItem
            {
                LangKey = LangId.Menu_MnuEdit,
                Command = Core.API.GetApiCommand(API.IG_OpenEditingApp),
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuEdit),
            };

            EditingApp.UpdateAppNameForMenuEdit(mnuEdit);
            mnuContext.Items.Add(mnuEdit);
        }

        if ((!imageNotFound && !Core.Photos.IsCurrentError) || hasClipboardImage)
        {
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId.Menu_MnuSetDesktopBackground,
                Command = Core.API.GetApiCommand(API.IG_SetDesktopBackground),
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuSetDesktopBackground),
            });

            if (BHelper.OS == OSType.Windows)
            {
                mnuContext.Items.Add(new PhMenuItem
                {
                    LangKey = LangId.Menu_MnuSetLockScreen,
                    Command = Core.API.GetApiCommand(API.IG_SetLockScreenImage),
                    HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuSetLockScreen),
                });
            }
        }

        mnuContext.Items.Add(new PhMenuItem("-"));

        mnuContext.Items.Add(new PhMenuItem
        {
            LangKey = LangId.Menu_MnuPasteImage,
            Command = Core.API.GetApiCommand(API.IG_PasteImage),
            HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuPasteImage),
        });
        mnuContext.Items.Add(new PhMenuItem
        {
            LangKey = LangId.Menu_MnuCopyImagePixels,
            Command = Core.API.GetApiCommand(API.IG_CopyImagePixels),
            HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuCopyImagePixels),
        });

        if (!imageNotFound && !hasClipboardImage)
        {
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId.Menu_MnuCopyPath,
                Command = Core.API.GetApiCommand(API.IG_CopyImagePath),
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuCopyPath),
            });
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId.Menu_MnuCopyFile,
                Command = Core.API.GetApiCommand(API.IG_CopyFiles),
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuCopyFile),
            });
            if (BHelper.OS != OSType.Mac) {
                mnuContext.Items.Add(new PhMenuItem
                {
                    LangKey = LangId.Menu_MnuCutFile,
                    Command = Core.API.GetApiCommand(API.IG_CutFiles),
                    HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuCutFile),
                });
            }
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId.Menu_MnuClearClipboard,
                Command = Core.API.GetApiCommand(API.IG_ClearClipboard),
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuClearClipboard),
            });
        }

        if (!imageNotFound && !hasClipboardImage)
        {
            mnuContext.Items.Add(new PhMenuItem("-"));
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId.Menu_MnuRename,
                Command = Core.API.GetApiCommand(API.IG_Rename),
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuRename),
            });
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId.Menu_MnuMoveToRecycleBin,
                Command = Core.API.GetApiCommand(API.IG_Delete),
                CommandParameter = "true",
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuMoveToRecycleBin),
            });
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId.Menu_MnuOpenLocation,
                Command = Core.API.GetApiCommand(API.IG_OpenLocation),
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuOpenLocation),
            });
            mnuContext.Items.Add(new PhMenuItem
            {
                LangKey = LangId.Menu_MnuImageProperties,
                Command = Core.API.GetApiCommand(API.IG_OpenProperties),
                HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuImageProperties),
            });
        }

        mnuContext.Items.Add("-");
        mnuContext.Items.Add(new PhMenuItem
        {
            LangKey = LangId.Menu_MnuExit,
            Command = Core.API.GetApiCommand(API.IG_Exit),
            HotkeyText = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuExit),
        });

        FeatureManager.HideLockedMenuItems(mnuContext.Items);
    }

    private async void LoadImagesFromCmdArgs()
    {
        var pathToLoad = Core.InputImagePathFromArgs;

        if (string.IsNullOrEmpty(pathToLoad)
            && Core.Config.EnableLastSeenImage
            && BHelper.CheckPath(Core.Config.LastSeenImagePath) == PathType.File)
        {
            pathToLoad = Core.Config.LastSeenImagePath;
        }

        if (string.IsNullOrEmpty(pathToLoad))
        {
            if (Core.Config.EnableWelcomeImage)
            {
                pathToLoad = BHelper.BaseDir("default.webp");
            }
            else
            {
                return;
            }
        }

        var ext = Path.GetExtension(pathToLoad);
        if (!string.IsNullOrEmpty(ext)
            && !Config.DefaultFileFormats.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            StartupTrace.Mark("plugins:await:begin");
            try { await Core.PluginDiscoveryTask; } catch { }
            StartupTrace.Mark("plugins:await:end");
            StartupTrace.Flush();
        }

        PrepareLoadPhotoList([pathToLoad],
            currentFilePath: null, disposeForegroundShell: false, reloadInitPhoto: true);
    }

    public void PrepareLoadPhotoList(ICollection<string> inputPaths, string? currentFilePath, bool disposeForegroundShell, bool reloadInitPhoto, bool selectLastPhoto = false)
    {
        _selectLastOnSearch = selectLastPhoto;

        if (disposeForegroundShell) Core.ShellProvider?.ForegroundShell = null;

        var useForegroundWindow = Core.ShellProvider?.CanUseForegroundShell() ?? false;
        var foregroundShell = useForegroundWindow
            ? Core.ShellProvider?.ForegroundShell
            : null;

        Dispatcher.UIThread.Post(async () =>
        {
            var resolvedPaths = inputPaths.Select(BHelper.ResolvePath).ToList();
            var resolvedCurrentPath = BHelper.ResolvePath(currentFilePath);

            var allowedExts = Core.GetSupportedFileExtensions();
            AddFileExtension(allowedExts, resolvedCurrentPath);
            foreach (var p in resolvedPaths) AddFileExtension(allowedExts, p);

            var searchOptions = new FileSearchOptions()
            {
                AllowedExtensions = allowedExts,
                UseExplorerSortOrder = Core.Config.EnableExplorerSortOrder,
                ForegroundShell = foregroundShell,
                SearchSubDirectories = Core.Config.EnableSubfoldersLoading,
                GroupByDir = Core.Config.EnableImageFolderGrouping,
                IncludeHidden = Core.Config.EnableHiddenImagesLoading,
                OrderBy = Core.Config.ImageLoadingOrder,
                OrderType = Core.Config.ImageLoadingOrderType,
            };
            var initPhoto = Core.Photos.StartLoadingFiles(resolvedPaths, resolvedCurrentPath, searchOptions, Files_Searched, reloadInitPhoto);

            if (reloadInitPhoto && initPhoto is not null)
            {
                _ = ViewPhotoAsync(initPhoto);
            }
        });
    }

    private static void AddFileExtension(HashSet<string> exts, string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext)) exts.Add(ext);
    }

    private void Files_Searched(FileSearchingEventArgs e)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            try
            {
                Files_Searched__(e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌ {nameof(Files_Searched)}: {ex.Message}");
            }
        });
    }

    private void Files_Searched__(FileSearchingEventArgs e)
    {
        var isEmptyList = Core.Photos.Count == 0;

        Core.Photos.Add(e.Results);

        if (_selectLastOnSearch)
        {
            var lastIndex = (int)Core.Photos.Count - 1;
            Core.Photos.InitPhoto = Core.Photos.Select(lastIndex);
            _ = ViewPhotoAsync(Core.Photos.InitPhoto, true, false);
        }
        else if (Core.Photos.InitPhoto is not null)
        {
            if (Core.Photos.CurrentIndex == -1)
            {
                _ = Core.Photos.Select(Core.Photos.InitPhoto.FilePath);

                var initIndex = Core.Photos.CurrentIndex;
                if (initIndex >= 0 && initIndex < Core.Photos.Items.Count)
                {
                    Core.Photos.Items[initIndex]?.Dispose();
                    Core.Photos.Items[initIndex] = Core.Photos.InitPhoto;
                    Core.Photos.Items[initIndex].IsCurrent = true;
                }
            }
        }
        else
        {
            Core.Photos.InitPhoto = Core.Photos.Select(0);
            _ = ViewPhotoAsync(Core.Photos.InitPhoto, true, false);
        }

        if (isEmptyList)
        {
            AppAPIProvider.SetFileWatcher(Core.Config.EnableFileWatcher);

            Dispatcher.UIThread.Post(() =>
            {
                PART_Gallery.ScrollToItem(Core.Photos.CurrentIndex);
            });
        }
    }

    public async Task ViewPhotoAsync(Photo? photo, bool useCache = true, bool scrollToThumbnail = true, bool resetZoom = true,
        TransitionRequest? transition = null)
    {
        var isSequential = Core.Config.BrowsingMode == BrowsingMode.Sequential;
        if (isSequential) Interlocked.Increment(ref _sequentialLoadDepth);

        Core.Photos.CancelCaching();

        _ = PART_Message.ClearAsync();

        Core.DisposeClipboardPhoto();
        Core.ImageTransform.Clear();

        if (photo is not null)
        {
            photo.ReadOptions = PhotoReadOptions.FromConfig(photo.GetDefaultFrameIndex());
        }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                PART_Viewer.EnableImagePreview = Core.Config.EnableImagePreview;

                if (scrollToThumbnail)
                {
                    PART_Gallery.ScrollToItem(Core.Photos.CurrentIndex);
                }

                await PART_Viewer.SetPhotoAsync(photo, new PhotoLoadingOptions
                {
                    UseCache = useCache,
                    ResetZoom = resetZoom,
                    Channels = Core.ColorChannels,
                    Transition = transition,
                });

                if (isSequential)
                {
                    await PART_Viewer.WaitForPhotoRenderedAsync(_sequentialRenderTimeout);
                }
            }
            finally
            {
                if (isSequential) Interlocked.Decrement(ref _sequentialLoadDepth);
            }

            Core.Photos.RequestCacheAround(Core.Photos.CurrentIndex);
        });
    }

    public void ApplyAppLayout()
    {
        var toolbarPos = Config.GetControlLayout(LayoutControl.Toolbar);
        var galleryPos = Config.GetControlLayout(LayoutControl.Gallery);

        if (toolbarPos == LayoutPosition.Top)
        {
            PART_Toolbar.ItemTooltipPlacement = PlacementMode.Bottom;

            if (galleryPos == LayoutPosition.Top)
            {
                PART_Layout.RowDefinitions = new("Auto, Auto, *");
                PART_Layout.ColumnDefinitions = new("*");

                PART_GalleryResizer.IsVisible = false;
                PART_Gallery.MaxWidth = double.PositiveInfinity;
                PART_Gallery.ViewMode = PhVirtualizingUniformPanelViewMode.FilmStrip;
                PART_Gallery.ItemTooltipPlacement = PlacementMode.Bottom;
                Grid.SetColumnSpan(PART_Toolbar, 1);

                Grid.SetRow(PART_Toolbar, 0);
                Grid.SetRow(PART_Gallery, 1);
                Grid.SetRow(PART_ViewerWrapper, 2);
                Grid.SetColumn(PART_Toolbar, 0);
                Grid.SetColumn(PART_Gallery, 0);
                Grid.SetColumn(PART_ViewerWrapper, 0);
            }
            else if (galleryPos == LayoutPosition.Bottom)
            {
                PART_Layout.RowDefinitions = new("Auto, *, Auto");
                PART_Layout.ColumnDefinitions = new("*");

                PART_GalleryResizer.IsVisible = false;
                PART_Gallery.MaxWidth = double.PositiveInfinity;
                PART_Gallery.ViewMode = PhVirtualizingUniformPanelViewMode.FilmStrip;
                PART_Gallery.ItemTooltipPlacement = PlacementMode.Top;
                Grid.SetColumnSpan(PART_Toolbar, 1);

                Grid.SetRow(PART_Toolbar, 0);
                Grid.SetRow(PART_Gallery, 2);
                Grid.SetRow(PART_GalleryResizer, 0);
                Grid.SetRow(PART_ViewerWrapper, 1);
                Grid.SetColumn(PART_Toolbar, 0);
                Grid.SetColumn(PART_Gallery, 0);
                Grid.SetColumn(PART_GalleryResizer, 0);
                Grid.SetColumn(PART_ViewerWrapper, 0);
            }
            else if (galleryPos == LayoutPosition.Left)
            {
                PART_Layout.RowDefinitions = new("Auto, *");
                PART_Layout.ColumnDefinitions = new("Auto, Auto, *");

                PART_GalleryResizer.IsVisible = true;
                PART_Gallery.ViewMode = PhVirtualizingUniformPanelViewMode.Gallery;
                PART_Gallery.ItemTooltipPlacement = PlacementMode.Pointer;
                Grid.SetColumnSpan(PART_Toolbar, 3);

                Grid.SetRow(PART_Toolbar, 0);
                Grid.SetRow(PART_Gallery, 1);
                Grid.SetRow(PART_GalleryResizer, 1);
                Grid.SetRow(PART_ViewerWrapper, 1);
                Grid.SetColumn(PART_Toolbar, 0);
                Grid.SetColumn(PART_Gallery, 0);
                Grid.SetColumn(PART_GalleryResizer, 1);
                Grid.SetColumn(PART_ViewerWrapper, 2);
            }
            else if (galleryPos == LayoutPosition.Right)
            {
                PART_Layout.RowDefinitions = new("Auto, *");
                PART_Layout.ColumnDefinitions = new("*, Auto, Auto");

                PART_GalleryResizer.IsVisible = true;
                PART_Gallery.ViewMode = PhVirtualizingUniformPanelViewMode.Gallery;
                PART_Gallery.ItemTooltipPlacement = PlacementMode.Pointer;
                Grid.SetColumnSpan(PART_Toolbar, 3);

                Grid.SetRow(PART_Toolbar, 0);
                Grid.SetRow(PART_Gallery, 1);
                Grid.SetRow(PART_GalleryResizer, 1);
                Grid.SetRow(PART_ViewerWrapper, 1);
                Grid.SetColumn(PART_Toolbar, 0);
                Grid.SetColumn(PART_Gallery, 2);
                Grid.SetColumn(PART_GalleryResizer, 1);
                Grid.SetColumn(PART_ViewerWrapper, 0);
            }
        }
        else if (toolbarPos == LayoutPosition.Bottom)
        {
            PART_Toolbar.ItemTooltipPlacement = PlacementMode.Top;

            if (galleryPos == LayoutPosition.Top)
            {
                PART_Layout.RowDefinitions = new("Auto, *, Auto");
                PART_Layout.ColumnDefinitions = new("*");

                PART_GalleryResizer.IsVisible = false;
                PART_Gallery.MaxWidth = double.PositiveInfinity;
                PART_Gallery.ViewMode = PhVirtualizingUniformPanelViewMode.FilmStrip;
                PART_Gallery.ItemTooltipPlacement = PlacementMode.Bottom;
                Grid.SetColumnSpan(PART_Toolbar, 1);

                Grid.SetRow(PART_Toolbar, 2);
                Grid.SetRow(PART_Gallery, 0);
                Grid.SetRow(PART_GalleryResizer, 0);
                Grid.SetRow(PART_ViewerWrapper, 1);
                Grid.SetColumn(PART_Toolbar, 0);
                Grid.SetColumn(PART_Gallery, 0);
                Grid.SetColumn(PART_GalleryResizer, 0);
                Grid.SetColumn(PART_ViewerWrapper, 0);
            }
            else if (galleryPos == LayoutPosition.Bottom)
            {
                PART_Layout.RowDefinitions = new("*, Auto, Auto");
                PART_Layout.ColumnDefinitions = new("*");

                PART_GalleryResizer.IsVisible = false;
                PART_Gallery.MaxWidth = double.PositiveInfinity;
                PART_Gallery.ViewMode = PhVirtualizingUniformPanelViewMode.FilmStrip;
                PART_Gallery.ItemTooltipPlacement = PlacementMode.Top;
                Grid.SetColumnSpan(PART_Toolbar, 1);

                Grid.SetRow(PART_Toolbar, 2);
                Grid.SetRow(PART_Gallery, 1);
                Grid.SetRow(PART_GalleryResizer, 0);
                Grid.SetRow(PART_ViewerWrapper, 0);
                Grid.SetColumn(PART_Toolbar, 0);
                Grid.SetColumn(PART_Gallery, 0);
                Grid.SetColumn(PART_GalleryResizer, 0);
                Grid.SetColumn(PART_ViewerWrapper, 0);
            }
            else if (galleryPos == LayoutPosition.Left)
            {
                PART_Layout.RowDefinitions = new("*, Auto");
                PART_Layout.ColumnDefinitions = new("Auto, Auto, *");

                PART_GalleryResizer.IsVisible = true;
                PART_Gallery.ViewMode = PhVirtualizingUniformPanelViewMode.Gallery;
                PART_Gallery.ItemTooltipPlacement = PlacementMode.Pointer;
                Grid.SetColumnSpan(PART_Toolbar, 3);

                Grid.SetRow(PART_Toolbar, 1);
                Grid.SetRow(PART_Gallery, 0);
                Grid.SetRow(PART_GalleryResizer, 0);
                Grid.SetRow(PART_ViewerWrapper, 0);
                Grid.SetColumn(PART_Toolbar, 0);
                Grid.SetColumn(PART_Gallery, 0);
                Grid.SetColumn(PART_GalleryResizer, 1);
                Grid.SetColumn(PART_ViewerWrapper, 2);
            }
            else if (galleryPos == LayoutPosition.Right)
            {
                PART_Layout.RowDefinitions = new("*, Auto");
                PART_Layout.ColumnDefinitions = new("*, Auto, Auto");

                PART_GalleryResizer.IsVisible = true;
                PART_Gallery.ViewMode = PhVirtualizingUniformPanelViewMode.Gallery;
                PART_Gallery.ItemTooltipPlacement = PlacementMode.Pointer;
                Grid.SetColumnSpan(PART_Toolbar, 3);

                Grid.SetRow(PART_Toolbar, 1);
                Grid.SetRow(PART_Gallery, 0);
                Grid.SetRow(PART_GalleryResizer, 0);
                Grid.SetRow(PART_ViewerWrapper, 0);
                Grid.SetColumn(PART_Toolbar, 0);
                Grid.SetColumn(PART_Gallery, 2);
                Grid.SetColumn(PART_GalleryResizer, 1);
                Grid.SetColumn(PART_ViewerWrapper, 0);
            }
        }

        UpdateGalleryWidth();
    }

    private void UpdateGalleryWidth()
    {
        if (PART_Layout.ColumnDefinitions.Count == 0) return;

        var galleryPos = Core.Config.Layout.GetValueOrDefault(LayoutControl.Gallery, LayoutPosition.Bottom);
        if (galleryPos is LayoutPosition.Top or LayoutPosition.Bottom) return;

        var galleryColIndex = Grid.GetColumn(PART_Gallery);
        var galleryResizerColIndex = Grid.GetColumn(PART_GalleryResizer);

        if (!Core.Config.ShowGallery)
        {
            PART_Layout.ColumnDefinitions[galleryColIndex].Width = new(0);
            PART_Layout.ColumnDefinitions[galleryResizerColIndex].Width = new(0);
            PART_GalleryResizer.IsVisible = false;
        }

        else
        {
            var galleryViewMinWidth = GalleryControl.CalculateWidthForGalleryView(1);
            var galleryViewWidth = GalleryControl.CalculateWidthForGalleryView(Core.Config.GalleryColumns);
            galleryViewWidth = Math.Min(galleryViewWidth, Bounds.Width * 0.8);

            PART_Layout.ColumnDefinitions[galleryColIndex].Width = new(galleryViewWidth);
            PART_Layout.ColumnDefinitions[galleryColIndex].MinWidth = galleryViewMinWidth;
            PART_Layout.ColumnDefinitions[galleryResizerColIndex].Width = new(5);
            PART_GalleryResizer.IsVisible = true;
        }
    }

    private void UpdateMotionButtonState()
    {
        var photo = PART_Viewer.Photo;
        var isLivePhoto = photo?.Error is null && (photo?.Metadata?.IsLivePhoto ?? false);
        var isFrameNavOpen = PART_ToolHost.Tool?.ToolId == FrameNavToolControl.TOOL_ID;

        PART_MotionOverlay.IsShown = isLivePhoto && !isFrameNavOpen;
    }

    private void UpdateMessageMargin()
    {
        var inset = PART_ImageInfoOverlay.CoveredHeight;
        var isBarAtBottom = PART_ImageInfoOverlay.VerticalAlignment == VerticalAlignment.Bottom;
        var topInset = isBarAtBottom ? 0 : inset;
        var bottomInset = isBarAtBottom ? inset : 0;

        PART_Message.Margin = new Thickness(
            _messageMargin.Left,
            _messageMargin.Top + topInset,
            _messageMargin.Right,
            _messageMargin.Bottom + bottomInset);
    }

    public void OpenViewerContextMenu()
    {
        var mnuContext = PART_Viewer.ContextMenu;
        if (mnuContext is null || mnuContext.IsOpen) return;

        BuildViewerContextMenu(mnuContext);
        mnuContext.Open();
    }

}
