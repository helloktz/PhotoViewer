using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Loggers;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Printing;
using PhotoViewer.Common.Types;
using PhotoViewer.Common.Windows;
using PhotoViewer.Tools;
using PhotoViewer.UI;
using PhotoViewer.UI.Viewer;
using PhotoViewer.UI.Viewer.Transitions;
using PhotoViewer.UI.Windowing;
using PhotoViewer.Windows;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public partial class AppAPIProvider
{
    private static FrozenSet<string> _desktopNativeFormats => [".bmp", ".jpg", ".jpeg", ".png", ".gif"];

    private static readonly DataFormat<byte[]> _winDropEffectFormat = DataFormat.CreateBytesPlatformFormat("Preferred DropEffect");
    private static readonly DataFormat<byte[]> _gnomeCopiedFilesFormat = DataFormat.CreateBytesPlatformFormat("x-special/gnome-copied-files");
    private static readonly DataFormat<byte[]> _kdeCutSelectionFormat = DataFormat.CreateBytesPlatformFormat("application/x-kde-cutselection");

    private WindowLayoutSnapshot? _preFullScreenLayout;

    private static TEventHandler<ViewerControl, PhotoLoadingEventArgs>? _siblingDirAnnouncer;

    private bool _isFullScreenBeforeSlideshow;
    private bool _isFramelessBeforeSlideshow;
    private bool _isWindowFitBeforeSlideshow;
    private bool _showToolbarBeforeSlideshow = true;
    private bool _showGalleryBeforeSlideshow = true;
    private bool _windowMaximizedBeforeSlideshow;
    private DispatcherTimer? _slideshowCountdownTimer;
    private IdleCursorHider? _slideshowCursorHider;

    public WindowLayoutSnapshot? PreFullScreenLayout => _preFullScreenLayout;

    private static ViewerControl Viewer => App.MainWindow.PART_MainView.PART_Viewer;
    private static ToolbarControl Toolbar => App.MainWindow.PART_MainView.PART_Toolbar;
    private static GalleryControl Gallery => App.MainWindow.PART_MainView.PART_Gallery;
    private static PhGridSplitter GalleryResizer => App.MainWindow.PART_MainView.PART_GalleryResizer;
    private static MessageOverlayControl Message => App.MainWindow.PART_MainView.PART_Message;
    private static ToolHostControl ToolHost => App.MainWindow.PART_MainView.PART_ToolHost;
    private static SlideshowCountdownOverlay SlideshowCountdown => App.MainWindow.PART_MainView.PART_SlideshowCountdown;

    internal static ViewerControl? GetViewer() => Viewer;

    public AppAPIProvider()
    {
        Core.ToolRegistry.Register(ColorPickerToolControl.TOOL_ID,
            new ToolControlAdapter(ColorPickerToolControl.TOOL_ID, v => new ColorPickerToolControl { Viewer = v }));
        Core.ToolRegistry.Register(CropImageToolControl.TOOL_ID,
            new ToolControlAdapter(CropImageToolControl.TOOL_ID, v => new CropImageToolControl { Viewer = v }));
        Core.ToolRegistry.Register(FrameNavToolControl.TOOL_ID,
            new ToolControlAdapter(FrameNavToolControl.TOOL_ID, v => new FrameNavToolControl { Viewer = v }));
        Core.ToolRegistry.Register(HdrToneMapperToolControl.TOOL_ID,
            new ToolControlAdapter(HdrToneMapperToolControl.TOOL_ID, v => new HdrToneMapperToolControl { Viewer = v }));
        Core.ToolRegistry.Register(ProjectorToolControl.TOOL_ID,
            new ToolControlAdapter(ProjectorToolControl.TOOL_ID, v => new ProjectorToolControl { Viewer = v }));

        Core.ToolRegistry.Register(ImageResizerTool.TOOL_ID, new ImageResizerTool());
        Core.ToolRegistry.Register(LosslessCompressionTool.TOOL_ID, new LosslessCompressionTool());
    }

    public static void IG_OpenMainMenu()
    {
        App.MainWindow.PART_MainView.PART_Toolbar.PART_BtnMainMenu.OpenDropdownMenu();
    }

    public static async Task IG_OpenSettingsAsync(string? configId = null)
    {
        if (string.IsNullOrWhiteSpace(configId)) configId = Core.Config.LastOpenedSetting;

        if (App.SettingsWindow is not null)
        {
            if (!string.IsNullOrWhiteSpace(configId))
            {
                App.SettingsWindow.NavigateToConfig(configId);
            }

            App.SettingsWindow.RestoreAndActivate();
            return;
        }

        App.SettingsWindow = new SettingsWindow(configId);

        try
        {
            await App.SettingsWindow.ShowAsync(null);
        }
        finally
        {
            App.SettingsWindow = null;
        }
    }

    public static void IG_Exit()
    {
        BHelper.ExitApp(false);
    }

    public static async Task IG_ApplySettingsAsync(string? jsonStr = null)
    {
        if (string.IsNullOrWhiteSpace(jsonStr))
        {
            throw new ArgumentException($$"""
                A JSON object of settings is required, e.g. { "Language": "Vietnamese.iglang.json" }.

                ----------
                👉🏼 Method: {{nameof(IG_ApplySettingsAsync)}}
                """,
                nameof(jsonStr));
        }

        var changedIds = Config.ApplyJsonOverrides(Core.Config, jsonStr);
        if (changedIds.Count == 0) return;

        var isSaved = await Core.Config.SaveAsync();
        SettingsViewModel.RunApplyActions(changedIds);

        if (!isSaved && Config.SavingException is Exception ex) throw ex;
    }

    public async Task IG_OpenFileAsync()
    {
        var supportFileExtPatterns = Core.GetSupportedFileExtensions().Select(ext => $"*{ext}")
            .ToImmutableList();

        var files = await App.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Core.Lang[LangId.Menu_MnuOpenFile],
            FileTypeFilter = [
                new FilePickerFileType(Core.Lang[LangId._OpenFileDialog]) {
                    Patterns = supportFileExtPatterns,
                },
            ],
        });

        var file = files?.ElementAtOrDefault(0);
        var filePath = file?.TryGetLocalPath();

        IG_OpenPath(filePath);
    }

    public async Task IG_OpenFolderAsync()
    {
        var dirs = await App.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions());

        var dir = dirs?.ElementAtOrDefault(0);
        var dirPath = dir?.TryGetLocalPath();

        IG_OpenPath(dirPath);
    }

    public void IG_OpenPath(string? path)
    {
        var fullPath = BHelper.ResolvePath(path);
        if (string.IsNullOrWhiteSpace(fullPath)) return;

        var imageIndex = Core.Photos.IndexOf(fullPath);

        if (imageIndex == -1 || (Core.ShellProvider?.CanUseForegroundShell() ?? false))
        {
            var disposeForegroundShell = imageIndex == -1;

            App.MainWindow.PART_MainView.PrepareLoadPhotoList([fullPath],
                currentFilePath: null, disposeForegroundShell, reloadInitPhoto: true);
        }
        else if (Core.Photos.CurrentIndex == imageIndex)
        {
        }
        else
        {
            IG_ViewByIndex(imageIndex);
        }
    }

    public static void IG_NewWindow()
    {
        if (!Core.Config.EnableMultiInstances)
        {
            _ = ModalWindow.ShowInfoAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Menu_MnuNewWindow],
                Heading = Core.Lang[LangId.Menu_MnuNewWindow],
                Description = Core.Lang[LangId.Menu_MnuNewWindow_Error],
            });
            return;
        }

        var filePath = Core.Photos.CurrentFilePath;

        var posDiff = App.MainWindow.DpiScale(10f);
        var newBounds = Core.Config.MainWindowBounds.WithX(Core.Config.MainWindowBounds.X + posDiff);
        newBounds = newBounds.WithY(Core.Config.MainWindowBounds.Y + posDiff);
        var boundStr = newBounds.ToStringDelimiter();
        var boundCmd = BHelper.BuildConfigCmdLine(nameof(Config.MainWindowBounds), boundStr);

        _ = BHelper.RunExeAsync(BHelper.AppRelaunchPath, [boundCmd, filePath]);
    }

    public static async Task IG_SaveAsync()
    {
        var srcFilePath = Core.Photos.CurrentFilePath;
        var isOpeningImageList = Core.Photos.CurrentIndex > -1;

        if (!isOpeningImageList && Core.ClipboardImage is not null)
        {
            await IG_SaveAsAsync();
            return;
        }

        if (Core.Config.EnableSaveConfirmation)
        {
            var modal = await ModalWindow.ShowWarningAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Menu_MnuSave],
                Heading = Core.Lang[LangId.Menu_MnuSave_Confirm],
                Description = srcFilePath,
                Note = Core.Lang[LangId.Menu_MnuSave_ConfirmDescription],
                IsRememberOptionVisible = true,
                Thumbnail = Core.Photos.Current?.GalleryThumbnail,
            }, ModalWindowButton.Yes_No);

            Core.Config.EnableSaveConfirmation = !modal.IsRememberOptionChecked;

            if (modal.ExitCode != DialogExitCode.OK) return;
        }

        await SaveImageAsync(srcFilePath);
    }

    public static async Task IG_SaveAsAsync()
    {
        var srcFilePath = string.Empty;
        var srcExt = ".png";
        IStorageFolder? initSaveDir = null;

        if (Core.ClipboardImage is null)
        {
            srcFilePath = Core.Photos.CurrentFilePath;
            srcExt = Core.Photos.Current?.Extension?.ToLowerInvariant() ?? srcExt;

            if (Core.Config.EnableOpenSaveAsInCurrentFolder)
            {
                var initSaveDirPath = Path.GetDirectoryName(srcFilePath);

                if (!string.IsNullOrWhiteSpace(initSaveDirPath))
                {
                    initSaveDir = await App.MainWindow.StorageProvider.TryGetFolderFromPathAsync(initSaveDirPath);
                }
            }
        }

        var suggestedExt = SavingExts.LastSavedExtension ?? srcExt;
        var destFileName = string.IsNullOrEmpty(srcFilePath)
            ? $"untitle{suggestedExt}"
            : Path.GetFileNameWithoutExtension(srcFilePath);

        var fileTypeChoices = SavingExts.FilePickerFileTypeChoices;
        var suggestedFileType = SavingExts.FindChoice(fileTypeChoices, suggestedExt);

        var result = await App.MainWindow.StorageProvider.SaveFilePickerWithResultAsync(new FilePickerSaveOptions
        {
            Title = Core.Lang[LangId.Menu_MnuSaveAs],
            FileTypeChoices = fileTypeChoices,
            ShowOverwritePrompt = !Core.Config.EnableSaveConfirmation,
            SuggestedStartLocation = initSaveDir,
            SuggestedFileName = destFileName,
            SuggestedFileType = suggestedFileType,
        });

        var destFilePath = result.File?.TryGetLocalPath() ?? string.Empty;
        if (string.IsNullOrEmpty(destFilePath)) return;

        SavingExts.LastSavedExtension = Path.GetExtension(destFilePath).ToLowerInvariant();

        if (File.Exists(destFilePath) && Core.Config.EnableSaveConfirmation)
        {
            var fi = new FileInfo(destFilePath);

            var modal = await ModalWindow.ShowWarningAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Menu_MnuSaveAs],
                Heading = Core.Lang[LangId.Menu_MnuSave_Confirm],
                Description = $"""
                {destFilePath}
                {BHelper.FormatSize(fi.Length)}
                """,
                Note = Core.Lang[LangId.Menu_MnuSave_ConfirmDescription],
                IsRememberOptionVisible = true,
            }, ModalWindowButton.Yes_No);

            Core.Config.EnableSaveConfirmation = !modal.IsRememberOptionChecked;

            if (modal.ExitCode != DialogExitCode.OK) return;
        }

        await SaveImageAsync(destFilePath);
    }

    public static async Task<bool> SaveImageAsync(string destFilePath)
    {
        var saveSource = ImageSaveSource.Undefined;
        var hasSrcPath = !string.IsNullOrEmpty(Core.Photos.CurrentFilePath);
        Exception? error = null;

        _ = Message.ShowAsync(destFilePath, Core.Lang[LangId.Menu_MnuSave_Saving]);

        var hasSelection = Viewer.EnableSelection && !Viewer.SourceSelection.IsEmpty;
        if (hasSelection)
        {
            try
            {
                using var shownState = Viewer.CaptureImageState();
                using var selectedBmp = shownState?.CopyPixels(ViewerImageRegion.Selection);
                var selectedImg = SkiaCodec.ToSKImageNoCopy(selectedBmp)!;
                using var photo = new Photo(selectedImg);

                await photo.SaveAsAsync(destFilePath, new PhotoTransform(),
                    Core.Config.ImageEditQuality, Core.Config.EnablePreserveModifiedDate);
                saveSource = ImageSaveSource.SelectedArea;
            }
            catch (Exception ex) { error = ex; }
        }

        else if (Core.ClipboardImage is not null)
        {
            try
            {
                await Core.ClipboardImage.SaveAsAsync(destFilePath, Core.ImageTransform,
                    Core.Config.ImageEditQuality, Core.Config.EnablePreserveModifiedDate);
                saveSource = ImageSaveSource.Clipboard;
            }
            catch (Exception ex) { error = ex; }
        }

        else if (Core.Photos.Current is not null)
        {
            try
            {
                await Core.Photos.Current.SaveAsAsync(destFilePath, Core.ImageTransform,
                    Core.Config.ImageEditQuality, Core.Config.EnablePreserveModifiedDate);
                saveSource = ImageSaveSource.CurrentFile;
            }
            catch (Exception ex) { error = ex; }
        }

        else
        {
            return false;
        }

        if (error is not null)
        {
            await Message.ClearAsync();

            _ = await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Menu_MnuSave],
                Heading = Core.Lang[LangId.Menu_MnuSave_Error],
                Description = $"""
                {error.Source}:
                {error.Message}

                {destFilePath}
                """
            });

            return false;
        }

        var newPhotoIndex = Core.Photos.IndexOf(destFilePath);
        if (newPhotoIndex == Core.Photos.CurrentIndex)
        {
            if (saveSource == ImageSaveSource.SelectedArea)
            {
                IG_Reload();
            }
            else if (saveSource == ImageSaveSource.Clipboard)
            {
                await LoadClipboardPhotoAsync(null);

                IG_Reload();
            }

            Core.ImageTransform.Clear();
            Viewer.ClearPhotoTransforms();
        }

        _ = Message.ShowAsync(destFilePath, Core.Lang[LangId.Menu_MnuSave_Success]);

        Core.OnPhotoSaved(new(Core.Photos.CurrentFilePath, destFilePath, saveSource));

        Gallery.LoadThumbnail(newPhotoIndex, false);

        return true;
    }

    public static async Task IG_ExportImageFrames()
    {
        if (Viewer.SourceKind == PhotoSource.None) return;
        var frameCount = Core.Photos.CurrentMetadata?.FrameCount ?? 0;
        if (frameCount < 2) return;

        var results = await App.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Core.Lang[LangId._FolderPickerTitle],
        });

        var destDirPath = results.ToArray().FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(destDirPath)) return;

        var srcFilePath = Core.Photos.CurrentFilePath;

        if (Core.ClipboardImage is not null)
        {
            srcFilePath = await Core.SavePhotoAsTempFileAsync();
        }
        if (string.IsNullOrEmpty(srcFilePath)) return;

        Core.IsBusy = true;

        var exportWindow = new ExportFramesWindow(srcFilePath, destDirPath);
        await exportWindow.ShowAsync(App.MainWindow);

        Core.IsBusy = false;
    }

    public static async Task IG_OpenWithAsync()
    {
        if (BHelper.OS != OSType.Windows)
        {
            throw new NotSupportedException($"IGE: This feature is not supported on {BHelper.OS}.");
        }

        string? filePath = null;
        var isClipboardPhoto = Core.ClipboardImage is not null;

        if (isClipboardPhoto)
        {
            _ = Message.ShowAsync(Core.Lang[LangId._CreatingFile], delayMs: 500);

            filePath = await Core.SavePhotoAsTempFileAsync();
        }
        else
        {
            filePath = Core.Photos.CurrentFilePath;
        }

        await Message.ClearAsync();
        if (!File.Exists(filePath))
        {
            _ = await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Menu_MnuOpenWith],
                Description = Core.Lang[LangId._CreatingFileError],
            });
        }
        else
        {
            Core.ShellProvider?.ShowOpenWith(filePath);
        }
    }

    public static async Task IG_PrintAsync(string? regionStr)
    {
        var isRegion = Enum.TryParse<PrintRegion>(regionStr, true, out var region);

        await IG_PrintAsync(isRegion ? region : null);
    }

    public static async Task IG_PrintAsync(PrintRegion? region = null)
    {
        var state = Viewer.CaptureImageState();
        if (state is null)
        {
            _ = await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Menu_MnuPrint],
                Description = Core.Lang[LangId.Menu_MnuPrint_Error],
            });
            return;
        }

        using var session = new PrintSession(state);
        var window = new PrintWindow(session, region ?? PrintRegion.WholeImage);

        var slideshow = Core.Slideshow;
        var isSlideshowPaused = slideshow is { IsRunning: true, IsPaused: false };
        if (isSlideshowPaused) slideshow!.Pause();

        Core.IsBusy = true;
        try
        {
            await window.ShowAsync(App.MainWindow);
        }
        finally
        {
            Core.IsBusy = false;
            if (isSlideshowPaused) slideshow!.Resume();
        }

        if (window.CompletedMessage is { } message) _ = Message.ShowAsync(message);
    }

    public static void IG_OpenLocation()
    {
        BHelper.OpenFilePath(Core.Photos.CurrentFilePath);
    }

    public static async Task IG_RenameAsync()
    {
        var oldFilePath = Core.Photos.CurrentFilePath;
        if (!File.Exists(oldFilePath)) return;

        var currentFolder = Path.GetDirectoryName(oldFilePath) ?? string.Empty;
        var ext = Path.GetExtension(oldFilePath);
        var newName = Path.GetFileNameWithoutExtension(oldFilePath);
        var title = Core.Lang[LangId.Menu_MnuRename];

        var result = await ModalWindow.ShowInputAsync(App.MainWindow, new ModalWindowOptions
        {
            Title = title,
            Description = $"""
            {oldFilePath}

            {Core.Lang[LangId.Menu_MnuRename_Description]}
            """,
            InputValue = newName,
            AcceptValue = TextBoxAcceptValue.FileNameValueOnly,
            ThumbnailIcon = StockIconId.Rename,
            Thumbnail = Core.Photos.Current?.GalleryThumbnail,
        });

        if (result.ExitCode != DialogExitCode.OK || string.IsNullOrWhiteSpace(result.InputValue)) return;

        newName = $"{result.InputValue.Trim()}{ext}";
        var newFilePath = Path.Combine(currentFolder, newName);

        try
        {
            if (string.Equals(oldFilePath, newFilePath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(oldFilePath, oldFilePath + "_temp");
                File.Move(oldFilePath + "_temp", newFilePath);
            }
            else
            {
                File.Move(oldFilePath, newFilePath);
            }

            var photoIndex = Core.Photos.IndexOf(oldFilePath);
            if (photoIndex >= 0)
            {
                Core.Photos.SetFilePath(photoIndex, newFilePath);
            }
        }
        catch (Exception ex)
        {
            _ = await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = title,
                Description = ex.Message,
            });
        }
    }

    public static async Task IG_DeleteAsync(string? moveToRecycleBinStr = "true")
    {
        var moveToRecycleBin = BHelper.ConvertStringToBool(moveToRecycleBinStr) ?? true;
        await IG_DeleteAsync(moveToRecycleBin);
    }

    public static async Task IG_DeleteAsync(bool moveToRecycleBin = true)
    {
        var filePath = Core.Photos.CurrentFilePath;
        if (!File.Exists(filePath)) return;

        var canDelete = true;
        var title = moveToRecycleBin
            ? Core.Lang[LangId.Menu_MnuMoveToRecycleBin]
            : Core.Lang[LangId.Menu_MnuDeleteFromHardDisk];

        if (Core.Config.EnableDeleteConfirmation)
        {
            var heading = moveToRecycleBin
                ? Core.Lang[LangId.Menu_MnuMoveToRecycleBin_Description]
                : Core.Lang[LangId.Menu_MnuDeleteFromHardDisk_Description];
            var thumbnailIcon = moveToRecycleBin
                ? StockIconId.RecycleBin
                : StockIconId.Delete;

            var modal = await ModalWindow.ShowWarningAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = title,
                Heading = heading,
                Description = $"""
                {filePath}
                {Core.Photos.Current?.Metadata.FileSizeFormatted}
                """,
                IsRememberOptionVisible = true,
                ThumbnailIcon = thumbnailIcon,
                Thumbnail = Core.Photos.Current?.GalleryThumbnail,
            }, ModalWindowButton.Yes_No);

            Core.Config.EnableDeleteConfirmation = !modal.IsRememberOptionChecked;

            canDelete = modal.ExitCode == DialogExitCode.OK;
        }

        if (!canDelete) return;
        Core.IsBusy = true;

        try
        {
            await IG_UnloadAsync();
            BHelper.DeleteFile(filePath, moveToRecycleBin);

            Core.Photos.Remove(filePath);
            ViewPhotoAfterCurrentRemoved();
        }
        catch (Exception ex)
        {
            await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = title,
                Description = ex.Message,
            });
        }

        Core.IsBusy = false;
    }

    public static void IG_OpenProperties()
    {
        Core.ShellProvider?.ShowFileProperties(Core.Photos.CurrentFilePath, App.MainWindow.Handle);
    }

    public void IG_ViewByIndex(string? photoIndexStr)
    {
        if (!int.TryParse(photoIndexStr, out var photoIndex)) return;

        IG_ViewByIndex(photoIndex);
    }

    public void IG_ViewByIndex(int photoIndex)
    {
        if (photoIndex < 0) return;

        var step = photoIndex - Core.Photos.CurrentIndex;
        IG_ViewByStep(step);
    }

    public void IG_ViewByStep(string? stepStr)
    {
        if (!int.TryParse(stepStr, out var step))
        {
            throw new ArgumentException($"""
                Step '{stepStr}' is not a valid integer.

                ----------
                👉🏼 Method: {nameof(IG_ViewByStep)}
                """,
                nameof(stepStr));
        }

        IG_ViewByStep(step);
    }

    public void IG_ViewByStep(int step)
    {
        _ = ViewByStep__(step, isSlideshowAdvance: false);
    }

    private Task ViewByStep__(int step, bool isSlideshowAdvance)
    {
        var isSequential = Core.Config.BrowsingMode == BrowsingMode.Sequential;
        if (isSequential)
        {
            var isLoading = App.MainWindow.PART_MainView.IsPhotoLoadInProgress;
            if (isLoading)
            {
                PhotoTrace.Mark("nav:blocked-sequential", Core.Photos.CurrentFilePath);
                return Task.CompletedTask;
            }
        }

        var canLoopBack = Core.Config.EnableSlideshow
            ? Core.Config.EnableLoopSlideshow
            : Core.Config.EnableLoopBackNavigation;

        var autoSwitchDir = Core.Config.EnableAutoSwitchSiblingDir && !Core.Config.EnableSlideshow;

        var reachedBoundary = !Core.Photos.GetByStep(step, autoSwitchDir ? false : canLoopBack, out var photo);
        if (reachedBoundary && autoSwitchDir)
        {
            var currentDir = Path.GetDirectoryName(Core.Photos.CurrentFilePath);
            var isSiblingDirOpened = TryOpenSiblingDir(currentDir, isNext: step >= 0, selectLastPhoto: step < 0);
            if (isSiblingDirOpened) return Task.CompletedTask;
            reachedBoundary = !Core.Photos.GetByStep(step, canLoopBack, out photo);
        }

        if (reachedBoundary)
        {
            var isFirst = Core.Photos.CurrentIndex == 0;

            _ = Message.ShowAsync(Core.Lang[isFirst
                ? LangId._ReachedFirstImage
                : LangId._ReachedLastImage]);
            return Task.CompletedTask;
        }

        var transition = CreateTransitionRequest__(step, isSlideshowAdvance);
        _ = App.MainWindow.PART_MainView.ViewPhotoAsync(photo, transition: transition);

        if (!isSlideshowAdvance) ResetSlideshowInterval__();

        return transition?.Completion ?? Task.CompletedTask;
    }

    private TransitionRequest? CreateTransitionRequest__(int step, bool isSlideshowAdvance)
    {
        if (IsQuickBrowsing) return null;

        var effectId = isSlideshowAdvance
            ? Core.Config.SlideshowTransition
            : Core.Config.NavigationTransition;
        var durationMs = isSlideshowAdvance
            ? Core.Config.SlideshowTransitionDuration
            : Core.Config.NavigationTransitionDuration;

        return TransitionEffects.CreateRequest(effectId, durationMs, step);
    }

    public static void IG_ViewSiblingDirPhotos(string? isNextStr)
    {
        var isNext = BHelper.ConvertStringToBool(isNextStr) ?? true;
        IG_ViewSiblingDirPhotos(isNext);
    }

    public static void IG_ViewSiblingDirPhotos(bool isNext)
    {
        var currentFilePath = Core.Photos.CurrentFilePath;
        var hasCurrentPhoto = !string.IsNullOrEmpty(currentFilePath);
        var currentDir = hasCurrentPhoto
            ? Path.GetDirectoryName(currentFilePath)
            : Core.Photos.DistinctDirs.FirstOrDefault();
        if (string.IsNullOrEmpty(currentDir)) return;

        var isSiblingDirOpened = TryOpenSiblingDir(currentDir, isNext, selectLastPhoto: false);
        if (!isSiblingDirOpened) return;

        ResetSlideshowInterval__();
    }

    private static bool TryOpenSiblingDir(string? currentDir, bool isNext, bool selectLastPhoto)
    {
        var direction = isNext ? 1 : -1;
        var supportedExts = Core.GetSupportedFileExtensions();
        var includeHidden = Core.Config.EnableHiddenImagesLoading;

        var siblingDir = BHelper.GetSiblingDir(currentDir, direction, supportedExts, includeHidden);
        if (string.IsNullOrEmpty(siblingDir)) return false;

        void OnPhotoLoaded(ViewerControl s, PhotoLoadingEventArgs e)
        {
            var isSiblingDirPhoto = BHelper.IsPathContainedIn(e.Photo.FilePath, siblingDir);
            if (!isSiblingDirPhoto) return;
            if (e.State != PhotoState.Loaded && e.Photo.Error is null) return;

            Viewer.PhotoLoading -= OnPhotoLoaded;
            _siblingDirAnnouncer = null;
            if (e.Photo.Error is not null) return;

            _ = Message.ShowAsync(Core.Lang[direction < 0
                ? LangId._SwitchedToPreviousFolder
                : LangId._SwitchedToNextFolder, siblingDir]);
        }

        if (_siblingDirAnnouncer is not null) Viewer.PhotoLoading -= _siblingDirAnnouncer;
        _siblingDirAnnouncer = OnPhotoLoaded;
        Viewer.PhotoLoading += _siblingDirAnnouncer;

        App.MainWindow.PART_MainView.PrepareLoadPhotoList([siblingDir],
            currentFilePath: null, disposeForegroundShell: true, reloadInitPhoto: true,
            selectLastPhoto);

        return true;
    }

    private static void ResetSlideshowInterval__()
    {
        if (!Core.Config.EnableSlideshow) return;

        var slideshow = Core.Slideshow;
        if (slideshow is null) return;

        if (slideshow.IsPaused)
        {
            slideshow.Resume();
        }

        slideshow.ResetInterval();
    }

    public void IG_ViewNext()
    {
        IG_ViewByStep(1);
    }

    public void IG_ViewPrevious()
    {
        IG_ViewByStep(-1);
    }

    public static void ViewPhotoAfterCurrentRemoved()
    {
        var canLoopBack = Core.Config.EnableSlideshow
            ? Core.Config.EnableLoopSlideshow
            : Core.Config.EnableLoopBackNavigation;

        var photo = Core.Photos.SelectReplacementOfCurrent(canLoopBack);

        if (photo is null && Core.Config.EnableSlideshow)
        {
            Core.API.IG_ToggleSlideshow(false);
        }

        _ = App.MainWindow.PART_MainView.ViewPhotoAsync(photo);
    }

    public async Task IG_GoToAsync()
    {
        if (Core.Photos.Count == 0) return;

        var oldIndex = Core.Photos.CurrentIndex + 1;
        var result = await ModalWindow.ShowInputAsync(App.MainWindow, new ModalWindowOptions
        {
            Title = Core.Lang[LangId.Menu_MnuGoTo],
            Description = Core.Lang[LangId.Menu_MnuGoTo_Description],
            InputValue = oldIndex.ToString(),
            AcceptValue = TextBoxAcceptValue.UnsignedIntValueOnly,
        });

        if (result.ExitCode != DialogExitCode.OK) return;

        if (int.TryParse(result.InputValue, out var newIndex))
        {
            newIndex--;

            if (newIndex != Core.Photos.CurrentIndex
                && 0 <= newIndex && newIndex < Core.Photos.Count)
            {
                IG_ViewByIndex(newIndex);
            }
        }
    }

    public void IG_GoToFirst()
    {
        IG_ViewByIndex(0);
    }

    public void IG_GoToLast()
    {
        IG_ViewByIndex((int)Core.Photos.Count - 1);
    }

    public void IG_ViewFrame(string? frameIndexStr)
    {
        if (!int.TryParse(frameIndexStr, out var frameIndex))
        {
            throw new ArgumentException($"""
                Frame index '{frameIndexStr}' is not a valid integer.

                ----------
                👉🏼 Method: {nameof(IG_ViewFrame)}
                """,
                nameof(frameIndexStr));
        }

        IG_ViewFrame(frameIndex);
    }

    public static void IG_ViewFrame(int frameIndex)
    {
        var frameCount = Core.Photos.CurrentMetadata?.FrameCount ?? 0;
        if (frameCount < 2) return;

        var safeFrameIndex = BHelper.ComputeIndexInRange(frameIndex, frameCount, true);

        Dispatcher.UIThread.Post(async () =>
        {
            await Viewer.ViewFrameAsync((uint)safeFrameIndex);
        });
    }

    public static void IG_ViewNextFrame()
    {
        var newFrameIndex = (Core.Photos.Current?.FrameIndex ?? 0) + 1;
        IG_ViewFrame(newFrameIndex);
    }

    public static void IG_ViewPreviousFrame()
    {
        var newFrameIndex = (Core.Photos.Current?.FrameIndex ?? 1) - 1;
        IG_ViewFrame(newFrameIndex);
    }

    public static void IG_ViewFirstFrame()
    {
        IG_ViewFrame(0);
    }

    public static void IG_ViewLastFrame()
    {
        var lastFrameIndex = (int)(Core.Photos.CurrentMetadata?.FrameCount ?? 1) - 1;
        IG_ViewFrame(lastFrameIndex);
    }

    public static async Task IG_CustomZoomAsync()
    {
        var oldZoom = Math.Round(Viewer.ZoomFactor * 100f, 3);

        var result = await ModalWindow.ShowInputAsync(App.MainWindow, new ModalWindowOptions
        {
            Title = Core.Lang[LangId.Menu_MnuCustomZoom],
            Description = Core.Lang[LangId.Menu_MnuCustomZoom_Description],
            InputValue = oldZoom.ToString(),
            AcceptValue = TextBoxAcceptValue.UnsignedFloatValueOnly,
            ThumbnailIcon = StockIconId.Find,
        });

        if (result.ExitCode != DialogExitCode.OK) return;

        if (float.TryParse(result.InputValue.Trim(), out var newZoom))
        {
            Viewer.ZoomFactor = newZoom / 100f;
        }
    }

    public void IG_SetZoom(string? factorStr)
    {
        if (!float.TryParse(factorStr, out var factor))
        {
            throw new ArgumentException($"""
                Zoom factor '{factorStr}' is not a valid float.

                ----------
                👉🏼 Method: {nameof(IG_SetZoom)}
                """,
                nameof(factorStr));
        }

        IG_SetZoom(factor);
    }

    public static void IG_SetZoom(float factor)
    {
        _ = Viewer.ZoomToPoint(factor);
    }

    public static void IG_SetZoomForMouseClick()
    {
        var zoomFactor = Viewer.ZoomFactor;
        var isZoomModeFactor = !Viewer.IsManualZoom && Viewer.ZoomMode != ZoomMode.LockZoom;
        var isEnlargedByZoomMode = isZoomModeFactor && zoomFactor > 1;

        if (zoomFactor < 1 || isEnlargedByZoomMode)
        {
            IG_SetZoom(1);
        }
        else
        {
            IG_Refresh();
        }
    }

    public void IG_SetZoomMode(string? modeStr)
    {
        if (!Enum.TryParse<ZoomMode>(modeStr, out var mode))
        {
            throw new ArgumentException($"""
                '{modeStr}' is not a valid zoom mode.

                ----------
                👉🏼 Method: {nameof(IG_SetZoomMode)}
                """,
                nameof(modeStr));
        }

        IG_SetZoomMode(mode);
    }

    public static void IG_SetZoomMode(ZoomMode mode)
    {
        if (mode == Core.Config.ZoomMode)
        {
            IG_Refresh();
        }
        else
        {
            Core.Config.ZoomMode = mode;
        }
    }

    public static void IG_ZoomIn()
    {
        if (Viewer.ZoomLevels.Length > 0)
        {
            Viewer.ZoomIn();
            return;
        }

        Viewer.StartDrawingAnimation(AnimationSources.ZoomIn, 100);
    }

    public static void IG_ZoomOut()
    {
        if (Viewer.ZoomLevels.Length > 0)
        {
            Viewer.ZoomOut();
            return;
        }

        Viewer.StartDrawingAnimation(AnimationSources.ZoomOut, 100);
    }

    public static void IG_PanLeft()
    {
        Viewer.StartDrawingAnimation(AnimationSources.PanLeft, 100);
    }

    public static void IG_PanRight()
    {
        Viewer.StartDrawingAnimation(AnimationSources.PanRight, 100);
    }

    public static void IG_PanUp()
    {
        Viewer.StartDrawingAnimation(AnimationSources.PanUp, 100);
    }

    public static void IG_PanDown()
    {
        Viewer.StartDrawingAnimation(AnimationSources.PanDown, 100);
    }

    public static void IG_PanToLeft()
    {
        var distanceX = Viewer.SrcRect.X * Viewer.ZoomFactor;
        var duration = 1000;
        Viewer.PanSpeed = distanceX / duration * 60;

        Viewer.StartDrawingAnimation(AnimationSources.PanLeft, duration, () =>
        {
            Viewer.PanSpeed = Core.Config.PanSpeed;
        });
    }

    public static void IG_PanToRight()
    {
        var x = Viewer.BitmapSize.Width - Viewer.SrcRect.Width;
        var distanceX = (x + Viewer.SrcRect.X) * Viewer.ZoomFactor;
        var duration = 1000;
        Viewer.PanSpeed = distanceX / duration * 60;

        Viewer.StartDrawingAnimation(AnimationSources.PanRight, duration, () =>
        {
            Viewer.PanSpeed = Core.Config.PanSpeed;
        });
    }

    public static void IG_PanToTop()
    {
        var distanceY = Viewer.SrcRect.Y * Viewer.ZoomFactor;
        var duration = 1000;
        Viewer.PanSpeed = distanceY / duration * 60;

        Viewer.StartDrawingAnimation(AnimationSources.PanUp, duration, () =>
        {
            Viewer.PanSpeed = Core.Config.PanSpeed;
        });
    }

    public static void IG_PanToBottom()
    {
        var y = Viewer.BitmapSize.Height - Viewer.SrcRect.Height;
        var distanceY = (y + Viewer.SrcRect.Y) * Viewer.ZoomFactor;
        var duration = 1000;
        Viewer.PanSpeed = distanceY / duration * 60;

        Viewer.StartDrawingAnimation(AnimationSources.PanDown, duration, () =>
        {
            Viewer.PanSpeed = Core.Config.PanSpeed;
        });
    }

    public static void IG_Refresh()
    {
        Viewer.Refresh(true, false, Core.Config.EnableWindowFit);
    }

    public static void IG_Reload(string? resetZoomBoolStr)
    {
        var resetZoom = BHelper.ConvertStringToBool(resetZoomBoolStr) ?? true;
        IG_Reload(resetZoom);
    }

    public static void IG_Reload(bool resetZoom = true)
    {
        _ = App.MainWindow.PART_MainView.ViewPhotoAsync(Core.Photos.Current, useCache: false, resetZoom: resetZoom);

        Gallery.LoadThumbnail(Core.Photos.CurrentIndex, false);
    }

    public static void IG_ReloadList()
    {
        App.MainWindow.PART_MainView.PrepareLoadPhotoList(Core.Photos.DistinctDirs,
            Core.Photos.CurrentFilePath, disposeForegroundShell: false, reloadInitPhoto: false);
    }

    public static async Task IG_UnloadAsync()
    {
        var args = new PhotoUnloadedEventArgs()
        {
            IsClipboardPhoto = Core.ClipboardImage is not null,
            Index = Core.Photos.CurrentIndex,
            FilePath = Core.Photos.CurrentFilePath,
        };

        if (args.IsClipboardPhoto)
        {
            await LoadClipboardPhotoAsync(null);

            await App.MainWindow.PART_MainView.ViewPhotoAsync(Core.Photos.Current);
        }

        else
        {
            Core.Photos.Current?.CancelLoading();

            await App.MainWindow.PART_MainView.ViewPhotoAsync(null, false);
            Core.Photos.Current?.Unload();
        }

        Core.OnPhotoUnloaded(args);
    }

    public static void IG_ToggleExplorerSortOrder(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        IG_ToggleExplorerSortOrder(enabled);
    }

    public static void IG_ToggleExplorerSortOrder(bool? enabled)
    {
        enabled ??= !Core.Config.EnableExplorerSortOrder;
        Core.Config.EnableExplorerSortOrder = enabled.Value;

        IG_ReloadList();
    }

    public void IG_SetLoadingOrderBy(string? orderByStr)
    {
        if (!Enum.TryParse<ImageOrderBy>(orderByStr, out var orderBy))
        {
            throw new ArgumentException($"""
                '{orderByStr}' is not a valid loading order.

                ----------
                👉🏼 Method: {nameof(IG_SetLoadingOrderBy)}
                """,
                nameof(orderByStr));
        }

        IG_SetLoadingOrderBy(orderBy);
    }

    public static void IG_SetLoadingOrderBy(ImageOrderBy orderBy)
    {
        if (orderBy == Core.Config.ImageLoadingOrder) return;

        Core.Config.ImageLoadingOrder = orderBy;
        IG_ReloadList();
    }

    public void IG_SetLoadingOrderType(string? orderTypeStr)
    {
        if (!Enum.TryParse<ImageOrderType>(orderTypeStr, out var orderType))
        {
            throw new ArgumentException($"""
                '{orderTypeStr}' is not a valid loading order.

                ----------
                👉🏼 Method: {nameof(IG_SetLoadingOrderType)}
                """,
                nameof(orderTypeStr));
        }

        IG_SetLoadingOrderType(orderType);
    }

    public static void IG_SetLoadingOrderType(ImageOrderType orderType)
    {
        if (orderType == Core.Config.ImageLoadingOrderType) return;

        Core.Config.ImageLoadingOrderType = orderType;
        IG_ReloadList();
    }

    public void IG_SetColorChannels(string? channelsStr)
    {
        if (!Enum.TryParse<ColorChannels>(channelsStr, out var channels))
        {
            throw new ArgumentException($"""
                '{channelsStr}' is not a valid color channel.

                ----------
                👉🏼 Method: {nameof(IG_SetColorChannels)}
                """,
                nameof(channelsStr));
        }

        IG_SetColorChannels(channels);
    }

    public static void IG_SetColorChannels(ColorChannels channels)
    {
        if (Viewer.SourceKind == PhotoSource.None || Core.IsBusy) return;

        if (Viewer.FilterColorChannels(channels, false))
        {
            Core.ColorChannels = channels;
            Viewer.Refresh(resetZoom: false);
        }
        else
        {
            _ = Message.ShowAsync(
                Core.Lang[LangId._InvalidAction],
                Core.Lang[LangId.Menu_MnuViewChannels]);
        }
    }

    public async Task IG_OpenEditingAppAsync()
    {
        string? filePath;
        if (Core.ClipboardImage != null)
        {
            _ = Message.ShowAsync(Core.Lang[LangId._CreatingFile], delayMs: 500);
            filePath = await Core.SavePhotoAsTempFileAsync();
        }
        else
        {
            filePath = Core.Photos.CurrentFilePath;
        }
        await Message.ClearAsync();

        if (!File.Exists(filePath))
        {
            _ = await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Menu_MnuOpenWith],
                Description = Core.Lang[LangId._CreatingFileError],
            });
            return;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (EditingApp.GetFromExtension(ext) is EditingApp app)
        {
            try
            {
                var args = BHelper.BuildExeArgList(app.Executable, app.Argument,
                    BHelper.GetRealPlatformPath(filePath));

                var result = await BHelper.RunExeCmd(args.Executable, args.Args, false, true);
                if (result == IgExitCode.Done)
                {
                    RunActionAfterEditing__();
                }
            }
            catch { }
        }
        else
        {
            Core.ShellProvider?.OpenDefaultEditingAppAsync(filePath, RunActionAfterEditing__);
        }
    }

    private void RunActionAfterEditing__()
    {
        if (Core.Config.AfterEditingAction == AfterEditAppAction.Minimize)
        {
            App.MainWindow.WindowState = WindowState.Minimized;
        }
        else if (Core.Config.AfterEditingAction == AfterEditAppAction.Close)
        {
            IG_Exit();
        }
    }

    public static void IG_InvertColors()
    {
        if (Viewer.SourceKind == PhotoSource.None || Core.IsBusy) return;

        if (Viewer.InvertColor(true))
        {
            Core.ImageTransform.IsColorInverted = Viewer.IsColorInverted;
        }
        else
        {
            _ = Message.ShowAsync(
                Core.Lang[LangId._InvalidAction],
                Core.Lang[LangId.Menu_MnuInvertColors]);
        }
    }

    public static async Task IG_ToggleImageAnimationAsync(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        await IG_ToggleImageAnimationAsync(enabled);
    }

    public static async Task IG_ToggleImageAnimationAsync(bool? enabled)
    {
        var meta = Viewer.Photo?.Metadata;
        if (meta?.IsLivePhoto == true && meta.EmbeddedVideoOffsetFromEnd > 0)
        {
            await meta.OpenLivePhotoVideoAsync().ConfigureAwait(false);
            return;
        }

        enabled ??= !Viewer.IsImageAnimating;

        if (enabled.Value)
        {
            Viewer.StartAnimator();
        }
        else
        {
            Viewer.StopAnimator();
        }
    }

    public void IG_Rotate(string? optionStr)
    {
        if (!Enum.TryParse<RotateOption>(optionStr, out var options))
        {
            throw new ArgumentException($"""
                '{optionStr}' is not a valid rotation option.

                ----------
                👉🏼 Method: {nameof(IG_Rotate)}
                """,
                nameof(optionStr));
        }

        IG_Rotate(options);
    }

    public static void IG_Rotate(RotateOption options)
    {
        if (Viewer.SourceKind == PhotoSource.None || Core.IsBusy) return;

        var degree = options == RotateOption.Left ? -90 : 90;

        if (Viewer.RotateImage(degree))
        {
            Core.ImageTransform.Orientation = Viewer.PhotoOrientation;
        }
        else
        {
            _ = Message.ShowAsync(
                Core.Lang[LangId._InvalidAction_Transformation],
                Core.Lang[LangId._InvalidAction]);
        }
    }

    public void IG_FlipImage(string? optionStr)
    {
        if (!Enum.TryParse<FlipOptions>(optionStr, out var options))
        {
            throw new ArgumentException($"""
                '{optionStr}' is not a valid flip option.

                ----------
                👉🏼 Method: {nameof(IG_FlipImage)}
                """,
                nameof(optionStr));
        }

        IG_FlipImage(options);
    }

    public static void IG_FlipImage(FlipOptions options)
    {
        if (Viewer.SourceKind == PhotoSource.None || Core.IsBusy) return;

        if (Viewer.FlipImage(options))
        {
            Core.ImageTransform.Orientation = Viewer.PhotoOrientation;
        }
        else
        {
            _ = Message.ShowAsync(
                Core.Lang[LangId._InvalidAction_Transformation],
                Core.Lang[LangId._InvalidAction]);
        }
    }

    public static async Task IG_SetDesktopBackgroundAsync()
    {
        await SetSystemBackgroundAsync(false);
    }

    public static async Task IG_SetLockScreenImageAsync()
    {
        if (BHelper.OS != OSType.Windows)
        {
            throw new NotSupportedException($"IGE: This feature is not supported on {BHelper.OS}.");
        }

        await SetSystemBackgroundAsync(true);
    }

    private static async Task SetSystemBackgroundAsync(bool forLockScreen)
    {
        if (Viewer.SourceKind == PhotoSource.None || Core.ShellProvider is null) return;

        var filePath = Core.Photos.CurrentFilePath;
        var ext = Core.Photos.Current?.Extension.ToLowerInvariant() ?? string.Empty;
        _ = Message.ShowAsync(Core.Lang[LangId._CreatingFile], delayMs: 500);

        var title = forLockScreen
            ? Core.Lang[LangId.Menu_MnuSetLockScreen]
            : Core.Lang[LangId.Menu_MnuSetDesktopBackground];

        if (Core.ClipboardImage is not null || !_desktopNativeFormats.Contains(ext))
        {
            filePath = await Core.SavePhotoAsTempFileAsync(".jpg");
        }
        await Message.ClearAsync();

        if (!File.Exists(filePath))
        {
            _ = await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = title,
                Description = Core.Lang[LangId._CreatingFileError],
            });

            return;
        }

        try
        {
            if (forLockScreen)
            {
                await Core.ShellProvider.SetLockScreenAsync(filePath);
            }
            else
            {
                Core.ShellProvider.SetWallpaper(filePath);
            }

            var successMsg = forLockScreen
                ? Core.Lang[LangId.Menu_MnuSetLockScreen_Success]
                : Core.Lang[LangId.Menu_MnuSetDesktopBackground_Success];
            _ = Message.ShowAsync(successMsg);
        }
        catch (Exception ex)
        {
            var heading = forLockScreen
                ? Core.Lang[LangId.Menu_MnuSetLockScreen_Error]
                : Core.Lang[LangId.Menu_MnuSetDesktopBackground_Error];

            _ = await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = title,
                Heading = heading,
                Description = ex.Message,
            });
        }
    }

    private async Task IG_PasteImageAsync()
    {
        if (App.MainWindow.Clipboard is null) return;

        using var data = await App.MainWindow.Clipboard.TryGetDataAsync();
        if (data is null) return;

        if (data.Contains(DataFormat.File))
        {
            var fileItem = await data.TryGetFileAsync();
            var filePath = fileItem?.TryGetLocalPath();

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                IG_OpenPath(filePath);
            }
            return;
        }

        if (data.Contains(DataFormat.Bitmap))
        {
            var abmp = await data.TryGetBitmapAsync();
            var skBmp = SkiaCodec.FromBitmap(abmp);
            if (skBmp is not null)
            {
                var photo = new Photo(skBmp);
                await LoadClipboardPhotoAsync(photo);
            }

            return;
        }

        if (data.Contains(DataFormat.Text))
        {
            var text = await data.TryGetTextAsync();
            var path = BHelper.ResolvePath(text);

            if (File.Exists(path) || Directory.Exists(path))
            {
                IG_OpenPath(path);
                return;
            }

            try
            {
                var photo = await MagickCodec.DecodeBase64Async(text);
                if (photo is not null)
                {
                    await LoadClipboardPhotoAsync(photo);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌ IG_PasteImageAsync: {ex.Message}");
            }
        }

    }

    public static async Task LoadClipboardPhotoAsync(Photo? photo)
    {
        Core.Photos.Current?.CancelLoading();

        await App.MainWindow.PART_MainView.ViewPhotoAsync(photo, true, false);

        Core.ClipboardImage = photo;
    }

    public static async Task IG_CopyImagePixelsAsync()
    {
        if (Viewer.SourceKind == PhotoSource.None || App.MainWindow.Clipboard is null) return;

        using var shownState = Viewer.CaptureImageState();
        using var bmp = shownState?.CopyPixels(Viewer.SourceSelection.IsEmpty
            ? ViewerImageRegion.WholeImage
            : ViewerImageRegion.Selection);
        if (bmp.IsDisposed()) return;

        await Message.ClearAsync();
        _ = Message.ShowAsync(Core.Lang[LangId.Menu_MnuCopyImagePixels_Copying], delayMs: 1000);

        try
        {
            var abmp = SkiaCodec.ToWritableBitmap(bmp);
            await App.MainWindow.Clipboard.SetBitmapAsync(abmp);

            _ = Message.ShowAsync(Core.Lang[LangId.Menu_MnuCopyImagePixels_Success]);
        }
        catch (Exception ex)
        {
            await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Menu_MnuCopyImagePixels],
                Description = ex.Message,
            });
        }
    }

    public static async Task IG_CopyImagePathAsync()
    {
        if (string.IsNullOrWhiteSpace(Core.Photos.CurrentFilePath) || App.MainWindow.Clipboard is null) return;

        try
        {
            await App.MainWindow.Clipboard.SetTextAsync(Core.Photos.CurrentFilePath);

            _ = Message.ShowAsync(Core.Lang[LangId.Menu_MnuCopyPath_Success]);
        }
        catch { }
    }

    public static async Task IG_CopyFilesAsync()
    {
        await SetFileToClipboardAsync(Core.Photos.CurrentFilePath, false);
    }

    public static async Task IG_CutFilesAsync()
    {
        if (BHelper.OS == OSType.Mac)
        {
            throw new NotSupportedException($"IGE: This feature is not supported on {BHelper.OS}.");
        }

        await SetFileToClipboardAsync(Core.Photos.CurrentFilePath, true);
    }

    public static async Task SetFileToClipboardAsync(string? filePath, bool forCutting)
    {
        if (App.MainWindow.Clipboard is null || !File.Exists(filePath)) return;

        if (forCutting)
        {
            if (!Core.Config.EnableCutMultipleFiles)
            {
                Core.StringClipboard.Clear();
            }
        }
        else
        {
            if (!Core.Config.EnableCopyMultipleFiles)
            {
                Core.StringClipboard.Clear();
            }
        }

        _ = Core.StringClipboard.Add(filePath);

        try
        {
            var dt = new DataTransfer();
            var clipboardPaths = new List<string>(Core.StringClipboard.Count);

            foreach (var path in Core.StringClipboard)
            {
                var fi = await App.MainWindow.StorageProvider.TryGetFileFromPathAsync(path);
                if (fi is null) continue;

                var dti = new DataTransferItem();
                dti.SetFile(fi);
                dt.Add(dti);

                clipboardPaths.Add(path);
            }

            AddFileOperationHint__(dt, clipboardPaths, forCutting);

            await App.MainWindow.Clipboard.SetDataAsync(dt);

            _ = Message.ShowAsync(Core.Lang[forCutting
                    ? LangId.Menu_MnuCutFile_Success
                    : LangId.Menu_MnuCopyFile_Success,
                Core.StringClipboard.Count]);
        }
        catch (Exception ex)
        {
            await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = Core.Lang[forCutting ? LangId.Menu_MnuCutFile : LangId.Menu_MnuCopyFile],
                Description = ex.Message,
            });
        }
    }

    private static void AddFileOperationHint__(DataTransfer dt, IReadOnlyList<string> filePaths, bool forCutting)
    {
        if (filePaths.Count == 0) return;
        var dti = new DataTransferItem();

        if (BHelper.OS == OSType.Windows)
        {
            var dropEffect = forCutting ? 2u : 5u;
            dti.Set(_winDropEffectFormat, BitConverter.GetBytes(dropEffect));
        }
        else if (BHelper.OS == OSType.Linux)
        {
            var uris = filePaths.Select(path => new Uri(path).AbsoluteUri);
            var opName = forCutting ? "cut" : "copy";

            dti.Set(_gnomeCopiedFilesFormat, Encoding.UTF8.GetBytes($"{opName}\n{string.Join('\n', uris)}"));
            dti.Set(_kdeCutSelectionFormat, Encoding.UTF8.GetBytes(forCutting ? "1" : "0"));
        }
        else return;

        dt.Add(dti);
    }

    public static async Task IG_ClearClipboardAsync()
    {
        Core.StringClipboard.Clear();

        if (App.MainWindow.Clipboard is not null)
        {
            await App.MainWindow.Clipboard.ClearAsync();
        }

        _ = Message.ShowAsync(Core.Lang[LangId.Menu_MnuClearClipboard_Success]);
    }

    public void IG_ToggleWindowFit(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        IG_ToggleWindowFit(enabled);
    }

    public void IG_ToggleWindowFit(bool? enabled = null)
    {
        enabled ??= !Core.Config.EnableWindowFit;
        Core.Config.EnableWindowFit = enabled.Value;

        if (Core.Config.EnableWindowFit)
        {
            if (Core.Config.EnableFullScreen)
            {
                IG_ToggleFullScreen(false);
            }
        }

        ApplyWindowFitMode(true);
    }

    public static void ApplyWindowFitMode(bool resetZoomMode = true)
    {
        if (!Core.Config.EnableWindowFit || Viewer.SourceKind == PhotoSource.None) return;

        App.MainWindow.WindowState = WindowState.Normal;

        var dpi = App.MainWindow.Dpi;
        var toolbarPos = Config.GetControlLayout(LayoutControl.Toolbar);
        var galleryPos = Config.GetControlLayout(LayoutControl.Gallery);

        var frameSize = Core.Config.EnableFrameless ? new() : new Size(2, 32);
        var gapW = 0d;
        var gapH = Toolbar.Bounds.Height;

        if (galleryPos is LayoutPosition.Left or LayoutPosition.Right)
        {
            gapW += Gallery.Bounds.Width + GalleryResizer.Bounds.Width;
        }
        else
        {
            gapH += Gallery.Bounds.Height + GalleryResizer.Bounds.Height;
        }

        var screen = App.MainWindow.Screens.ScreenFromWindow(App.MainWindow)!;
        var workArea = screen.GetActualWorkingArea().ToRect(dpi);

        var srcImgW = Viewer.BitmapSize.Width;
        var srcImgH = Viewer.BitmapSize.Height;

        var zoomFactor = Viewer.ZoomFactor;
        if (resetZoomMode)
        {
            if (Core.Config.ZoomMode == ZoomMode.LockZoom)
            {
                Viewer.SetZoomFactor(Core.Config.ZoomLockValue / 100f, false);
            }
            else
            {
                var maxViewerWidth = workArea.Width - gapW;
                var maxViewerHeight = workArea.Height - gapH;

                zoomFactor = Viewer.CalculateZoomFactor(Core.Config.ZoomMode, srcImgW, srcImgH, maxViewerWidth, maxViewerHeight);
            }
        }

        var zoomImgW = (int)(srcImgW * zoomFactor / dpi);
        var zoomImgH = (int)(srcImgH * zoomFactor / dpi);

        var viewerBounds = new Rect(0, 0,
            Math.Min(zoomImgW, workArea.Width - gapW),
            Math.Min(zoomImgH, workArea.Height - gapH));

        var workAreaWithoutFrame = new Rect(
            workArea.X + gapW / 2 + frameSize.Width / 2,
            workArea.Y + gapH / 2 + frameSize.Height / 2,
            workArea.Width - gapW - frameSize.Width,
            workArea.Height - gapH - frameSize.Height);

        viewerBounds = workAreaWithoutFrame.CenterRectEx(viewerBounds, true);

        var winBounds = new Rect(
            viewerBounds.X - gapW / 2 - frameSize.Width / 2,
            viewerBounds.Y - gapH / 2 - frameSize.Height / 2,
            viewerBounds.Width + gapW,
            viewerBounds.Height + gapH);

        if (!Core.Config.EnableCenterWindowFit)
        {
            winBounds = winBounds.WithX(App.MainWindow.Position.X / dpi);
            winBounds = winBounds.WithY(App.MainWindow.Position.Y / dpi);
        }

        App.MainWindow.MinWidth = gapW + 50;
        App.MainWindow.MinHeight = gapH + 50;

        App.MainWindow.Position = new((int)(winBounds.X * dpi), (int)(winBounds.Y * dpi));
        App.MainWindow.Width = winBounds.Width;
        App.MainWindow.Height = winBounds.Height;

        if (resetZoomMode)
        {
            Viewer.SetZoomFactor(zoomFactor, false);
        }

    }

    public void IG_ToggleFrameless(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        IG_ToggleFrameless(enabled);
    }

    public void IG_ToggleFrameless(bool? enabled = null)
    {
        if (BHelper.OS == OSType.Linux)
        {
            throw new NotSupportedException($"IGE: This feature is not supported on {BHelper.OS}.");
        }

        enabled ??= !Core.Config.EnableFrameless;

        SetFramelessMode__(enabled.Value, true);
    }
    private void SetFramelessMode__(bool enabled, bool showMessage)
    {
        Core.Config.EnableFrameless = enabled;

        if (enabled)
        {
            if (Core.Config.EnableFullScreen) IG_ToggleFullScreen(false);

            App.MainWindow.IsFrameless = true;
        }

        else
        {
            App.MainWindow.IsFrameless = false;
        }

        if (Core.Config.EnableWindowFit)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ApplyWindowFitMode(!Viewer.IsManualZoom);
            });
        }

        if (showMessage && enabled)
        {
            _ = Message.ShowAsync(
                Core.Lang[LangId.Menu_MnuFrameless_EnableDescription],
                Core.Lang[LangId.Menu_MnuFrameless]);
        }
    }

    public void IG_ToggleFullScreen(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        IG_ToggleFullScreen(enabled);
    }

    public void IG_ToggleFullScreen(bool? enabled = null)
    {
        enabled ??= !Core.Config.EnableFullScreen;
        SetFullScreenMode__(enabled.Value,
            !Core.Config.ShowToolbarInFullscreen, !Core.Config.ShowGalleryInFullscreen);
    }
    private void SetFullScreenMode__(bool enabled, bool hideToolbar = false, bool hideThumbnails = false)
    {
        Core.Config.EnableFullScreen = enabled;

        if (enabled)
        {
            _preFullScreenLayout ??= CapturePreFullScreenLayout__();

            if (Core.Config.EnableWindowFit) IG_ToggleWindowFit(false);
            if (Core.Config.EnableFrameless) SetFramelessMode__(false, false);

            App.MainWindow.WindowState = WindowState.FullScreen;

            if (hideToolbar) IG_ToggleToolbar(false);
            if (hideThumbnails) _ = IG_ToggleGalleryAsync(false);
        }

        else
        {
            var layout = _preFullScreenLayout ?? ReadWindowLayoutFromConfig__();
            _preFullScreenLayout = null;

            IG_ToggleToolbar(layout.ShowToolbar);
            _ = IG_ToggleGalleryAsync(layout.ShowGallery);

            Core.Config.EnableMainWindowMaximized = layout.IsMaximized;
            App.MainWindow.WindowState = layout.IsMaximized
                ? WindowState.Maximized
                : WindowState.Normal;

            if (layout.IsFrameless) SetFramelessMode__(true, false);

            if (layout.IsWindowFit) Dispatcher.UIThread.Post(() => IG_ToggleWindowFit(true));
        }
    }

    private WindowLayoutSnapshot CapturePreFullScreenLayout__()
    {
        var layout = App.MainWindow.CaptureWindowLayout();
        if (!Core.Config.EnableSlideshow) return layout;

        return layout with
        {
            ShowToolbar = _showToolbarBeforeSlideshow,
            ShowGallery = _showGalleryBeforeSlideshow,
        };
    }

    private static WindowLayoutSnapshot ReadWindowLayoutFromConfig__() => new()
    {
        IsMaximized = Core.Config.EnableMainWindowMaximized,
        Bounds = Core.Config.MainWindowBounds,
        ShowToolbar = Core.Config.ShowToolbar,
        ShowGallery = Core.Config.ShowGallery,
        IsFrameless = Core.Config.EnableFrameless,
        IsWindowFit = Core.Config.EnableWindowFit,
    };

    public void IG_ToggleSlideshow(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        IG_ToggleSlideshow(enabled);
    }

    public void IG_ToggleSlideshow(bool? enabled = null)
    {
        enabled ??= !Core.Config.EnableSlideshow;

        if (enabled.Value)
        {
            StartSlideshow__();
        }
        else
        {
            StopSlideshow__();
        }
    }
    private void StartSlideshow__()
    {
        if (Core.Config.EnableSlideshow) return;
        if (Core.Photos.Count == 0) return;

        _isFullScreenBeforeSlideshow = Core.Config.EnableFullScreen;
        _isFramelessBeforeSlideshow = Core.Config.EnableFrameless;
        _isWindowFitBeforeSlideshow = Core.Config.EnableWindowFit;
        _showToolbarBeforeSlideshow = Core.Config.ShowToolbar;
        _showGalleryBeforeSlideshow = Core.Config.ShowGallery;
        _windowMaximizedBeforeSlideshow = App.MainWindow.WindowState == WindowState.Maximized;

        if (Core.Config.EnableFullscreenSlideshow && !Core.Config.EnableFullScreen)
        {
            _preFullScreenLayout = CapturePreFullScreenLayout__();

            if (Core.Config.EnableWindowFit) IG_ToggleWindowFit(false);
            if (Core.Config.EnableFrameless) SetFramelessMode__(false, false);

            App.MainWindow.WindowState = WindowState.FullScreen;
            Core.Config.EnableFullScreen = true;
        }

        IG_ToggleToolbar(false);
        _ = IG_ToggleGalleryAsync(false);

        var slideshow = new SlideshowProvider();
        slideshow.NextPhotoRequested += OnSlideshowNextPhoto__;
        Core.Slideshow = slideshow;

        Core.Config.EnableSlideshow = true;
        slideshow.Start();

        SetSlideshowCountdown(Core.Config.EnableSlideshowCountdown);

        _slideshowCursorHider?.Dispose();
        _slideshowCursorHider = new IdleCursorHider(App.MainWindow, Viewer);
        _slideshowCursorHider.Start();
    }

    private void StopSlideshow__()
    {
        if (!Core.Config.EnableSlideshow) return;

        Core.Config.EnableSlideshow = false;

        SetSlideshowCountdown(false);

        _slideshowCursorHider?.Dispose();
        _slideshowCursorHider = null;

        if (Core.Slideshow is { } service)
        {
            service.NextPhotoRequested -= OnSlideshowNextPhoto__;
            service.Dispose();
            Core.Slideshow = null;
        }

        if (!Core.Photos.IsCachingEnabled)
        {
            Core.Photos.ClearCache(Viewer.Photo);
        }

        if (Core.Config.EnableFullscreenSlideshow && !_isFullScreenBeforeSlideshow)
        {
            Core.Config.EnableFullScreen = false;
            _preFullScreenLayout = null;

            Core.Config.EnableMainWindowMaximized = _windowMaximizedBeforeSlideshow;
            App.MainWindow.WindowState = _windowMaximizedBeforeSlideshow
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        IG_ToggleToolbar(_showToolbarBeforeSlideshow);
        _ = IG_ToggleGalleryAsync(_showGalleryBeforeSlideshow);

        if (_isFramelessBeforeSlideshow) SetFramelessMode__(true, false);
        if (_isWindowFitBeforeSlideshow) IG_ToggleWindowFit(true);
    }

    private Task OnSlideshowNextPhoto__()
    {
        return ViewByStep__(1, isSlideshowAdvance: true);
    }

    public void IG_ToggleSlideshowCountdown(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        IG_ToggleSlideshowCountdown(enabled);
    }

    public void IG_ToggleSlideshowCountdown(bool? enabled = null)
    {
        enabled ??= !Core.Config.EnableSlideshowCountdown;
        Core.Config.EnableSlideshowCountdown = enabled.Value;

        SetSlideshowCountdown(enabled.Value);
    }
    private void SetSlideshowCountdown(bool enabled)
    {
        if (enabled)
        {
            _slideshowCountdownTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(100),
                DispatcherPriority.Render,
                (_, _) => SlideshowCountdown.InvalidateVisual());
            _slideshowCountdownTimer.Start();
        }
        else
        {
            _slideshowCountdownTimer?.Stop();
            _slideshowCountdownTimer = null;
            SlideshowCountdown.InvalidateVisual();
        }
    }

    public static void IG_ToggleSlideshowPlayback(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        IG_ToggleSlideshowPlayback(enabled);
    }

    public static void IG_ToggleSlideshowPlayback(bool? enabled = null)
    {
        if (Core.Slideshow?.IsRunning != true) return;
        var isPaused = Core.Slideshow?.IsPaused ?? false;

        if (isPaused) Core.Slideshow?.Resume();
        else Core.Slideshow?.Pause();

        _ = Message.ShowAsync(Core.Lang[isPaused
            ? LangId._ResumeSlideshow
            : LangId._PauseSlideshow]);
    }

    public static void IG_ToggleToolbar(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        IG_ToggleToolbar(enabled);
    }

    public static void IG_ToggleToolbar(bool? enabled = null)
    {
        enabled ??= !Core.Config.ShowToolbar;
        Core.Config.ShowToolbar = enabled.Value;

        if (Core.Config.EnableWindowFit)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ApplyWindowFitMode(!Viewer.IsManualZoom);
            });
        }
    }

    public static async Task IG_ToggleGalleryAsync(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        await IG_ToggleGalleryAsync(enabled);
    }

    public static async Task IG_ToggleGalleryAsync(bool? enabled = null)
    {
        enabled ??= !Core.Config.ShowGallery;
        Core.Config.ShowGallery = enabled.Value;

        if (enabled.Value)
        {
            Gallery.ScrollToItem(Core.Photos.CurrentIndex);
        }

        App.MainWindow.PART_MainView.ApplyAppLayout();

        if (Core.Config.EnableWindowFit)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ApplyWindowFitMode(!Viewer.IsManualZoom);
            });
        }
    }

    public static void IG_ToggleCheckerboard(string? mode = null)
    {
        var isValid = Enum.TryParse<CheckerboardType>(mode, out var wantedMode);

        IG_ToggleCheckerboard(isValid ? wantedMode : null);
    }

    public static void IG_ToggleCheckerboard(CheckerboardType? mode = null)
    {
        var wantedMode = Core.Config.CheckerboardMode;

        if (mode is not null)
        {
            wantedMode = mode.Value;
        }
        else
        {
            var hasAlpha = Core.Photos.CurrentMetadata?.HasAlpha ?? false;

            if (Core.Config.CheckerboardMode == CheckerboardType.None)
            {
                wantedMode = hasAlpha
                    ? CheckerboardType.Image
                    : CheckerboardType.Client;
            }
            else if (Core.Config.CheckerboardMode == CheckerboardType.Image)
            {
                wantedMode = CheckerboardType.Client;
            }
            else if (Core.Config.CheckerboardMode == CheckerboardType.Client)
            {
                wantedMode = CheckerboardType.None;
            }
        }

        Core.Config.CheckerboardMode = wantedMode;
    }

    public static void IG_ToggleWindowTopMost(string? boolStr = null)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr);
        IG_ToggleWindowTopMost(enabled);
    }

    public static void IG_ToggleWindowTopMost(bool? enabled = null)
    {
        enabled ??= !Core.Config.EnableWindowTopMost;
        Core.Config.EnableWindowTopMost = enabled.Value;

        _ = Message.ShowAsync(Core.Lang[enabled.Value
            ? LangId.Menu_MnuToggleTopMost_Enable
            : LangId.Menu_MnuToggleTopMost_Disable]);
    }

    public static async Task IG_SetBackgroundColorAsync(string? hexColor = null)
    {
        Color? newColor = null;

        if (string.IsNullOrEmpty(hexColor))
        {
            var oldColor = Resx.Get<SolidColorBrush>(ResxId.IG_ViewerBackgroundBrush).Color;
            var defaultColor = Core.Config.EnableSlideshow
                ? Colors.Black
                : Const.COLOR_EMPTY;

            var cp = new PhColorPickerDialog(oldColor, defaultColor)
            {
                Title = Core.Lang[LangId.Menu_MnuChangeBackgroundColor],
            };
            var result = await cp.ShowAsync(App.MainWindow);

            if (result != DialogExitCode.OK) return;
            newColor = cp.SelectedColor;
        }

        else
        {
            newColor = BHelper.ColorFromHex(hexColor);
        }

        if (newColor == null) return;

        if (Core.Config.EnableSlideshow)
        {
            Core.Config.SlideshowBackgroundColor = newColor.Value.ToHex();
        }
        else
        {
            Core.Config.BackgroundColor = newColor.Value.ToHex();
        }
    }

    public static void IG_ToggleTool(string? toolId)
    {
        if (string.IsNullOrEmpty(toolId)) return;
        if (Core.ToolRegistry.Get(toolId) is not { } tool) return;

        if (tool.IsHosted)
        {
            var currentToolId = ToolHost.Tool?.ToolId;

            if (string.Equals(currentToolId, toolId, StringComparison.Ordinal))
            {
                if (ToolHost.Tool is { } currentTool)
                {
                    ToolRegistry.SaveToolSettings(currentTool);
                }

                ToolHost.CloseCurrentTool();
                Core.Config.LastOpenedTool = "";
            }
            else
            {
                IG_CloseTool(currentToolId);

                if (tool is ToolControlAdapter adapter)
                {
                    var control = adapter.CreateToolControl(Viewer);
                    ToolRegistry.LoadToolSettings(control);
                    ToolHost.OpenTool(control);

                    Core.Config.LastOpenedTool = control.ToolId;
                }
            }
        }
        else
        {
            IG_OpenTool(toolId);
        }
    }

    public static void IG_OpenTool(string? toolId)
    {
        if (string.IsNullOrEmpty(toolId)) return;
        if (Core.ToolRegistry.Get(toolId) is not { } tool) return;

        if (tool.IsHosted)
        {
            var currentToolId = ToolHost.Tool?.ToolId;
            if (string.Equals(currentToolId, toolId, StringComparison.Ordinal)) return;

            IG_CloseTool(currentToolId);

            if (tool is ToolControlAdapter adapter)
            {
                var control = adapter.CreateToolControl(Viewer);
                ToolRegistry.LoadToolSettings(control);
                ToolHost.OpenTool(control);

                Core.Config.LastOpenedTool = control.ToolId;
            }
        }
        else
        {
            if (tool is ExternalToolProxy extProxy)
            {
                if (Core.ToolRegistry.ExternalTools.IsRunning(toolId)) return;

                _ = LaunchExternalToolAsync(extProxy);
            }
            else
            {
                tool.Viewer = Viewer;
                ToolRegistry.LoadToolSettings(tool);

                var context = new ToolExecutionContext { Window = App.MainWindow };
                _ = ToolRegistry.ExecuteNonHostedToolAsync(tool, context);
            }
        }
    }

    public static void IG_CloseTool(string? toolId)
    {
        if (string.IsNullOrEmpty(toolId)) return;

        if (ToolHost.Tool is ITool currentTool
            && string.Equals(currentTool.ToolId, toolId, StringComparison.Ordinal))
        {
            ToolRegistry.SaveToolSettings(currentTool);
        }

        ToolHost.CloseTool(toolId);
        Core.Config.LastOpenedTool = "";
    }

    public static void IG_CloseCurrentTool()
    {
        if (ToolHost.Tool is ITool currentTool)
        {
            ToolRegistry.SaveToolSettings(currentTool);
        }
        ToolHost.CloseCurrentTool();
    }

    private static async Task LaunchExternalToolAsync(ExternalToolProxy proxy)
    {
        var context = new ToolExecutionContext { Window = App.MainWindow };
        var result = await proxy.TryLaunchAsync(context);
        if (!result.Success)
        {
            await ShowToolLaunchFailedAsync(proxy.Tool, result.Error);
        }
    }

    private static async Task ShowToolLaunchFailedAsync(ExternalTool tool, string? error)
    {
        var toolName = tool.DisplayName;

        var result = await ModalWindow.ShowAsync(App.MainWindow, new ModalWindowOptions
        {
            Title = toolName,
            Heading = Core.Lang[LangId.Settings_Tools_ToolLaunchFailed, toolName],
            Description = Core.Lang[LangId.Settings_Tools_ToolLaunchFailed_Description],
            Details = error,
            ThumbnailIcon = StockIconId.Warning,
        }, ModalWindowButton.Yes_No);

        if (result.ExitCode == DialogExitCode.OK)
        {
            await OpenToolSettingsAsync(tool.ToolId);
        }
    }

    private static async Task OpenToolSettingsAsync(string toolId)
    {
        if (App.SettingsWindow is not null)
        {
            App.SettingsWindow.RestoreAndActivate();
            App.SettingsWindow.NavigateToTool(toolId);
            return;
        }

        App.SettingsWindow = new SettingsWindow(SettingsNavId.Tools.ToString(), toolId);
        try
        {
            await App.SettingsWindow.ShowAsync(null);
        }
        finally
        {
            App.SettingsWindow = null;
        }
    }

    public static async Task IG_OpenAboutWindowAsync()
    {
        var dialog = new AboutWindow();
        _ = await dialog.ShowAsync(App.MainWindow);
    }

    public static async Task IG_QuickSetupAsync()
    {
        var dialog = new QuickSetupWindow();
        _ = await dialog.ShowAsync(App.MainWindow);
    }

    public static async Task IG_SetDefaultPhotoViewerAsync()
    {
        await SetDefaultPhotoViewerAsync(true);
    }

    public static async Task IG_RemoveDefaultPhotoViewerAsync()
    {
        await SetDefaultPhotoViewerAsync(false);
    }

    public static async Task<bool> RegisterAppMenuEntryAsync(bool enable, PhWindow? owner = null, Lang? lang = null)
    {
        if (Core.ShellProvider is null) return false;

        owner ??= App.MainWindow;
        lang ??= Core.Lang;

        var ok = enable
            ? await Core.ShellProvider.RegisterAppMenuEntryAsync()
            : await Core.ShellProvider.UnregisterAppMenuEntryAsync();

        if (!ok)
        {
            await ModalWindow.ShowErrorAsync(owner, new ModalWindowOptions
            {
                Title = lang[LangId.Settings_AppMenuEntry],
                Heading = lang[LangId.Settings_AppMenuEntry_Error],
            });
            return false;
        }

        await ModalWindow.ShowInfoAsync(owner, new ModalWindowOptions
        {
            Title = lang[LangId.Settings_AppMenuEntry],
            Heading = lang[enable
                ? LangId.Settings_AppMenuEntry_Success
                : LangId.Settings_AppMenuEntry_RemoveSuccess],
            Note = enable ? lang[LangId.Settings_UnmanagedSettingReminder] : null,
            NoteStyle = InfoBarSeverity.Warning,
        });

        return true;
    }

    public static async Task SetDefaultPhotoViewerAsync(bool enable, PhWindow? owner = null, Lang? lang = null)
    {
        if (Core.ShellProvider is null) return;

        owner ??= App.MainWindow;
        lang ??= Core.Lang;

        var extensions = Core.GetSupportedFileExtensions().ToArray();

        try
        {
            var scope = await Core.ShellProvider.SetDefaultPhotoViewerAsync(extensions, enable);

            if (scope is null) return;

            var scopeText = lang[scope == DefaultAppScope.LocalMachine
                ? LangId.Settings_DefaultPhotoViewer_ScopePerMachine
                : LangId.Settings_DefaultPhotoViewer_ScopePerUser];

            await ModalWindow.ShowInfoAsync(owner, new ModalWindowOptions
            {
                Title = lang[enable
                    ? LangId.Menu_MnuSetDefaultPhotoViewer
                    : LangId.Menu_MnuRemoveDefaultPhotoViewer],
                Heading = lang[enable
                    ? LangId.Menu_MnuSetDefaultPhotoViewer_Success
                    : LangId.Menu_MnuRemoveDefaultPhotoViewer_Success],
                Description = scopeText,
                Note = enable ? lang[LangId.Settings_UnmanagedSettingReminder] : null,
                NoteStyle = InfoBarSeverity.Warning,
            });
        }
        catch (Exception ex)
        {
            await ModalWindow.ShowErrorAsync(owner, new ModalWindowOptions
            {
                Title = lang[enable
                    ? LangId.Menu_MnuSetDefaultPhotoViewer
                    : LangId.Menu_MnuRemoveDefaultPhotoViewer],
                Heading = lang[enable
                    ? LangId.Menu_MnuSetDefaultPhotoViewer_Error
                    : LangId.Menu_MnuRemoveDefaultPhotoViewer_Error],
                Description = ex.Message,
                Details = ex.ToString(),
            });
        }
    }

    public static void IG_SetFileWatcher(string? boolStr)
    {
        var enabled = BHelper.ConvertStringToBool(boolStr) ?? false;
        IG_SetFileWatcher(enabled);
    }

    public static void IG_SetFileWatcher(bool enabled)
    {
        Core.Config.EnableFileWatcher = enabled;
    }

    public static void SetFileWatcher(bool enabled)
    {

        if (enabled)
        {
            var dirPath = Core.Photos.DistinctDirs.FirstOrDefault();

            if (string.IsNullOrEmpty(dirPath))
            {
                dirPath = Core.Photos.FileWatcherFolderPath;
            }

            if (!string.IsNullOrEmpty(dirPath))
            {
                Core.Photos.StartFileWatcher(dirPath);
            }
        }
        else
        {
            Core.Photos.StopFileWatcher();
        }
    }

    public static void IG_OpenContextMenu()
    {
        App.MainWindow.PART_MainView.OpenViewerContextMenu();
    }

}
