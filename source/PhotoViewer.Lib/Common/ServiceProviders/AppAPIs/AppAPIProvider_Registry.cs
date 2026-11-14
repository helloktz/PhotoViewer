using PhotoViewer.Common.Actions;
using PhotoViewer.Common.Commands;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public partial class AppAPIProvider
{

    private FrozenDictionary<API, IPhCommand> _apis => new Dictionary<API, IPhCommand>()
    {
        { API.IG_OpenMainMenu,          PhCommands.Create(IG_OpenMainMenu) },

        { API.IG_OpenFolder,            PhCommands.Create(IG_OpenFolderAsync) },
        { API.IG_OpenPath,              PhCommands.Create(IG_OpenPath) },
        { API.IG_ApplySettings,         PhCommands.Create(IG_ApplySettingsAsync) },

        { API.IG_OpenFile,              PhCommands.Create(IG_OpenFileAsync) },
        { API.IG_NewWindow,             PhCommands.Create(IG_NewWindow) },
        { API.IG_Save,                  PhCommands.Create(IG_SaveAsync) },
        { API.IG_SaveAs,                PhCommands.Create(IG_SaveAsAsync) },
        { API.IG_ExportImageFrames,     PhCommands.Create(IG_ExportImageFrames) },
        { API.IG_OpenWith,              PhCommands.Create(IG_OpenWithAsync) },
        { API.IG_Print,                 PhCommands.Create(IG_PrintAsync) },
        { API.IG_OpenLocation,          PhCommands.Create(IG_OpenLocation) },
        { API.IG_Rename,                PhCommands.Create(IG_RenameAsync) },
        { API.IG_Delete,                PhCommands.Create(IG_DeleteAsync) },
        { API.IG_OpenProperties,        PhCommands.Create(IG_OpenProperties) },

        { API.IG_ViewNext,              PhCommands.Create(IG_ViewNext) },
        { API.IG_ViewPrevious,          PhCommands.Create(IG_ViewPrevious) },
        { API.IG_Goto,                  PhCommands.Create(IG_GoToAsync) },
        { API.IG_GotoFirst,             PhCommands.Create(IG_GoToFirst) },
        { API.IG_GotoLast,              PhCommands.Create(IG_GoToLast) },
        { API.IG_ViewFrame,             PhCommands.Create(IG_ViewFrame) },
        { API.IG_ViewNextFrame,         PhCommands.Create(IG_ViewNextFrame) },
        { API.IG_ViewPreviousFrame,     PhCommands.Create(IG_ViewPreviousFrame) },
        { API.IG_ViewFirstFrame,        PhCommands.Create(IG_ViewFirstFrame) },
        { API.IG_ViewLastFrame,         PhCommands.Create(IG_ViewLastFrame) },

        { API.IG_ViewByStep,            PhCommands.Create(IG_ViewByStep) },
        { API.IG_ViewByIndex,           PhCommands.Create(IG_ViewByIndex) },
        { API.IG_ViewSiblingDirPhotos,  PhCommands.Create(IG_ViewSiblingDirPhotos) },

        { API.IG_CustomZoom,            PhCommands.Create(IG_CustomZoomAsync) },
        { API.IG_SetZoom,               PhCommands.Create(IG_SetZoom) },
        { API.IG_SetZoomForMouseClick,  PhCommands.Create(IG_SetZoomForMouseClick) },
        { API.IG_ZoomIn,                PhCommands.Create(IG_ZoomIn) },
        { API.IG_ZoomOut,               PhCommands.Create(IG_ZoomOut) },
        { API.IG_SetZoomMode,           PhCommands.Create(IG_SetZoomMode) },

        { API.IG_PanLeft,               PhCommands.Create(IG_PanLeft) },
        { API.IG_PanRight,              PhCommands.Create(IG_PanRight) },
        { API.IG_PanUp,                 PhCommands.Create(IG_PanUp) },
        { API.IG_PanDown,               PhCommands.Create(IG_PanDown) },
        { API.IG_PanToLeft,             PhCommands.Create(IG_PanToLeft) },
        { API.IG_PanToRight,            PhCommands.Create(IG_PanToRight) },
        { API.IG_PanToTop,              PhCommands.Create(IG_PanToTop) },
        { API.IG_PanToBottom,           PhCommands.Create(IG_PanToBottom) },

        { API.IG_Refresh,                       PhCommands.Create(IG_Refresh) },
        { API.IG_Reload,                        PhCommands.Create(IG_Reload) },
        { API.IG_ReloadList,                    PhCommands.Create(IG_ReloadList) },
        { API.IG_Unload,                        PhCommands.Create(IG_UnloadAsync) },
        { API.IG_ToggleExplorerSortOrder,       PhCommands.Create(IG_ToggleExplorerSortOrder) },
        { API.IG_SetLoadingOrderBy,             PhCommands.Create(IG_SetLoadingOrderBy) },
        { API.IG_SetLoadingOrderType,           PhCommands.Create(IG_SetLoadingOrderType) },
        { API.IG_SetColorChannels,              PhCommands.Create(IG_SetColorChannels) },
        { API.IG_OpenEditingApp,                PhCommands.Create(IG_OpenEditingAppAsync) },
        { API.IG_InvertColors,                  PhCommands.Create(IG_InvertColors) },
        { API.IG_ToggleImageAnimation,          PhCommands.Create(IG_ToggleImageAnimationAsync) },
        { API.IG_Rotate,                        PhCommands.Create(IG_Rotate) },
        { API.IG_FlipImage,                     PhCommands.Create(IG_FlipImage) },
        { API.IG_SetDesktopBackground,          PhCommands.Create(IG_SetDesktopBackgroundAsync) },
        { API.IG_SetLockScreenImage,            PhCommands.Create(IG_SetLockScreenImageAsync) },

        { API.IG_PasteImage,            PhCommands.Create(IG_PasteImageAsync) },
        { API.IG_CopyImagePixels,       PhCommands.Create(IG_CopyImagePixelsAsync) },
        { API.IG_CopyFiles,             PhCommands.Create(IG_CopyFilesAsync) },
        { API.IG_CutFiles,              PhCommands.Create(IG_CutFilesAsync) },
        { API.IG_CopyImagePath,         PhCommands.Create(IG_CopyImagePathAsync) },
        { API.IG_ClearClipboard,        PhCommands.Create(IG_ClearClipboardAsync) },

        { API.IG_ToggleWindowFit,           PhCommands.Create(IG_ToggleWindowFit) },
        { API.IG_ToggleFrameless,           PhCommands.Create(IG_ToggleFrameless) },
        { API.IG_ToggleFullScreen,          PhCommands.Create(IG_ToggleFullScreen) },
        { API.IG_ToggleSlideshow,           PhCommands.Create(IG_ToggleSlideshow) },
        { API.IG_ToggleSlideshowPlayback,   PhCommands.Create(IG_ToggleSlideshowPlayback) },
        { API.IG_ToggleSlideshowCountdown,  PhCommands.Create(IG_ToggleSlideshowCountdown) },

        { API.IG_ToggleToolbar,         PhCommands.Create(IG_ToggleToolbar) },
        { API.IG_ToggleGallery,         PhCommands.Create(IG_ToggleGalleryAsync) },
        { API.IG_ToggleCheckerboard,    PhCommands.Create(IG_ToggleCheckerboard) },
        { API.IG_ToggleWindowTopMost,   PhCommands.Create(IG_ToggleWindowTopMost) },
        { API.IG_SetBackgroundColor,    PhCommands.Create(IG_SetBackgroundColorAsync) },

        { API.IG_CloseCurrentTool,        PhCommands.Create(IG_CloseCurrentTool) },
        { API.IG_ToggleTool,              PhCommands.Create(IG_ToggleTool) },
        { API.IG_OpenTool,                PhCommands.Create(IG_OpenTool) },
        { API.IG_CloseTool,               PhCommands.Create(IG_CloseTool) },

        { API.IG_OpenSettings,              PhCommands.Create(IG_OpenSettingsAsync) },

        { API.IG_OpenAboutWindow,           PhCommands.Create(IG_OpenAboutWindowAsync) },
        { API.IG_QuickSetup,                PhCommands.Create(IG_QuickSetupAsync) },
        { API.IG_SetDefaultPhotoViewer,     PhCommands.Create(IG_SetDefaultPhotoViewerAsync) },
        { API.IG_RemoveDefaultPhotoViewer,  PhCommands.Create(IG_RemoveDefaultPhotoViewerAsync) },

        { API.IG_Exit,                      PhCommands.Create(IG_Exit) },

        { API.IG_SetFileWatcher,            PhCommands.Create(IG_SetFileWatcher) },
        { API.IG_OpenContextMenu,           PhCommands.Create(IG_OpenContextMenu) },

    }.ToFrozenDictionary();

    public IPhCommand? GetApiCommand(string? apiName)
    {
        if (Enum.TryParse<API>(apiName, out var api))
        {
            return GetApiCommand(api);
        }

        return null;
    }

    public IPhCommand? GetApiCommand(API api)
    {
        var cmd = _apis.GetValueOrDefault(api);
        return cmd is null ? null : new LockAwareApiCommand(api, cmd);
    }

    private sealed class LockAwareApiCommand(API api, IPhCommand inner) : IPhCommand
    {
        public bool IsAsync => inner.IsAsync;

        public event EventHandler? CanExecuteChanged
        {
            add => inner.CanExecuteChanged += value;
            remove => inner.CanExecuteChanged -= value;
        }

        public bool CanExecute(object? parameter)
            => !FeatureManager.IsLocked(api) && inner.CanExecute(parameter);

        public void Execute(object? parameter)
        {
            if (!FeatureManager.IsLocked(api)) inner.Execute(parameter);
        }

        public Task ExecuteAsync(object? parameter)
            => FeatureManager.IsLocked(api) ? Task.CompletedTask : inner.ExecuteAsync(parameter);
    }

    private static readonly FrozenSet<API> _toolBlockedApis = new[]
    {
        API.IG_Delete,
        API.IG_Rename,
        API.IG_Save,
        API.IG_SaveAs,
        API.IG_NewWindow,
        API.IG_OpenWith,
        API.IG_OpenEditingApp,
        API.IG_SetDesktopBackground,
        API.IG_SetLockScreenImage,
        API.IG_SetDefaultPhotoViewer,
        API.IG_RemoveDefaultPhotoViewer,
        API.IG_ApplySettings,
        API.IG_Exit,
    }.ToFrozenSet();

    public bool IsApiAllowedForTool(string? apiName)
    {
        if (Enum.TryParse<API>(apiName, out var api))
        {
            return !_toolBlockedApis.Contains(api);
        }

        return true;
    }

    public async Task<ActionResult> RunApiAsync(API api, string? args = null)
    {
        if (FeatureManager.IsLocked(api))
            return new ActionResult(ActionExitCode.Locked);

        var cmd = GetApiCommand(api);

        return await RunApiCommandAsync_(cmd, args);
    }

    public async Task<ActionResult> RunApiAsync(string? apiName, string? args = null)
    {
        if (FeatureManager.IsLocked(apiName))
            return new ActionResult(ActionExitCode.Locked);

        var cmd = GetApiCommand(apiName);

        return await RunApiCommandAsync_(cmd, args);
    }

    private static async Task<ActionResult> RunApiCommandAsync_(IPhCommand? cmd, string? args = null)
    {
        if (cmd is null)
            return new ActionResult(ActionExitCode.ApiNotFound);

        if (!cmd.CanExecute(args))
            return new ActionResult(ActionExitCode.Cancelled);

        try
        {
            if (cmd.IsAsync)
            {
                await cmd.ExecuteAsync(args);
            }
            else
            {
                cmd.Execute(args);
            }
        }
        catch (Exception ex)
        {
            return new ActionResult(ActionExitCode.Error, ex);
        }

        return new ActionResult(ActionExitCode.Success);
    }

    public async Task RunActionAsync(SingleAction? ac, string? customArg = null)
    {
        _ = await RunActionAsync(ac, true, customArg);
    }

    public async Task<Exception?> RunActionAsync(SingleAction? ac, bool showError, string? customArg = null)
    {
        if (string.IsNullOrWhiteSpace(ac?.Executable)) return null;

        var acArgs = customArg ?? ac.Argument;
        var acResults = await RunApiAsync(ac.Executable, acArgs);

        if (acResults.ExitCode == ActionExitCode.Cancelled) return null;

        if (acResults.ExitCode == ActionExitCode.Locked) return null;

        if (acResults.ExitCode == ActionExitCode.Success)
        {
            return await RunActionAsync(ac.NextAction, showError);
        }

        Exception? error = null;
        if (acResults.ExitCode == ActionExitCode.Error && acResults.Error != null)
        {
            error = acResults.Error;
        }

        else if (acResults.ExitCode == ActionExitCode.ApiNotFound)
        {
            var args = string.Join(string.Empty, acArgs) ?? string.Empty;
            var exeInfo = BHelper.BuildExeArgList(ac.Executable, args, Core.Photos.CurrentFilePath);

            var exeCode = await BHelper.RunExeCmd(exeInfo.Executable, exeInfo.Args, false);
            if (exeCode != IgExitCode.Done)
            {
                var errorMsg = Core.Lang[LangId._UserAction_Win32ExeError, ac.Executable];
                error = new Win32Exception(errorMsg);
            }
        }

        if (error is not null && showError)
        {
            var errorTitle = Core.Lang[ac.LangKey];

            _ = await ModalWindow.ShowErrorAsync(App.MainWindow, new ModalWindowOptions
            {
                Title = errorTitle,
                Description = error.Message,
            });
        }

        return error;
    }

}
