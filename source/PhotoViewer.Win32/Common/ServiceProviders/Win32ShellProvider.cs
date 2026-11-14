using D2Phap;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Windowing;
using Microsoft.VisualBasic.FileIO;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System.UserProfile;

namespace PhotoViewer.Win32.Common.ServiceProviders;

public class Win32ShellProvider : PhDisposable, IShellProvider
{
    private readonly EggShell _shell = new();
    private static ShellAppView? _foregroundShell = null;
    private static string _foregroundShellPath = string.Empty;

    private static string Win32SearchFileExtension => ".search-ms";

    private static readonly Lazy<string?> _localCacheDir = new(() =>
    {
        try { return ApplicationData.Current.LocalCacheFolder.Path; }
        catch { return null; }
    });

    public object? ForegroundShell
    {
        get => _foregroundShell;
        set
        {
            _foregroundShell = null;
            _foregroundShell = (ShellAppView?)value;

            try
            {
                _foregroundShellPath = _foregroundShell?.GetTabViewPath() ?? string.Empty;
            }
            catch
            {
                _foregroundShellPath = string.Empty;
                _foregroundShell = null;
            }
        }
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        AllowSleep();
        ForegroundShell = null;
    }

    public bool CanUseForegroundShell()
    {
        var inputImageDirPath = Path.GetDirectoryName(Core.InputImagePathFromArgs) ?? string.Empty;
        var isFromSearchWindow = _foregroundShellPath.StartsWith(EggShell.SEARCH_MS_PROTOCOL, StringComparison.OrdinalIgnoreCase);
        var isFromSavedSearch = _foregroundShellPath.EndsWith(Win32SearchFileExtension, StringComparison.OrdinalIgnoreCase);
        var isFromSameDir = inputImageDirPath.Equals(_foregroundShellPath, StringComparison.OrdinalIgnoreCase);

        var isShellOnlyList = isFromSearchWindow || isFromSavedSearch;

        var useForegroundWindow = _foregroundShell is not null
            && !string.IsNullOrEmpty(Core.InputImagePathFromArgs)
            && (isShellOnlyList || (isFromSameDir && Core.Config.EnableExplorerSortOrder));

        return useForegroundWindow;
    }

    public object? GetForegroundWindowView()
    {
        var ev = _shell.GetForegroundWindowView();

        return ev;
    }

    public string? GetTargetPathFromShortcut(string? lnkFilePath)
    {
        if (string.IsNullOrWhiteSpace(lnkFilePath)) return null;

        return EggShell.GetTargetPathFromShortcut(lnkFilePath);
    }

    public void OpenFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        try
        {
            _shell.SelectFileFromExplorer(filePath);
        }
        catch
        {
            using var proc = Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
    }

    public void OpenFolderPath(string? dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath)) return;

        try
        {
            Directory.CreateDirectory(dirPath);
        }
        catch { }

        try
        {
            using var proc = Process.Start("explorer.exe", $"\"{dirPath}\"");
        }
        catch { }
    }

    public void DeleteFile(string filePath, bool moveToRecycleBin = true)
    {
        var option = moveToRecycleBin ? RecycleOption.SendToRecycleBin : RecycleOption.DeletePermanently;

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, option);
        }
        catch (OperationCanceledException) { }
    }

    public void ShowOpenWith(string filePath)
    {
        var args = $"shell32.dll,OpenAs_RunDLL {GetActualPath(filePath)}";

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = args,
            UseShellExecute = true,
        });
    }

    public void ShowFileProperties(string filePath, nint windowHandle)
    {
        EggShell.DisplayFileProperties(filePath, windowHandle);
    }

    public void SetWallpaper(string filePath)
    {
        Win32DesktopApi.SetWallpaper(GetActualPath(filePath), WallpaperStyle.Current);
    }

    public async Task SetLockScreenAsync(string filePath)
    {
        var sFile = await StorageFile.GetFileFromPathAsync(filePath);
        await LockScreen.SetImageFileAsync(sFile);
    }

    public async Task OpenDefaultEditingAppAsync(string filePath, Action? callbackFn = null)
    {
        filePath = GetActualPath(filePath);

        if (Environment.OSVersion.Version.Major == 10
            && Environment.OSVersion.Version.Build >= 22000)
        {
            var mspaint11 = @"%LocalAppData%\Microsoft\WindowsApps\mspaint.exe";
            var mspaint11Path = BHelper.ResolvePath(mspaint11);

            if (!File.Exists(mspaint11Path))
            {
                _ = await ModalWindow.ShowInfoAsync(null, new ModalWindowOptions
                {
                    Title = Core.Lang[LangId.Menu_MnuEdit],
                    Heading = Core.Lang[LangId.Menu_MnuEdit_AppNotFound],
                    Description = filePath,
                });
                return;
            }

            using var p11 = new Process();
            p11.StartInfo.FileName = mspaint11Path;
            p11.StartInfo.Arguments = $"\"{filePath}\"";
            p11.StartInfo.UseShellExecute = true;

            try
            {
                p11.Start();
                callbackFn?.Invoke();
            }
            catch (Exception ex)
            {
                _ = await ModalWindow.ShowErrorAsync(null, new ModalWindowOptions
                {
                    Title = string.Format(Core.Lang[LangId.Menu_MnuEdit], "(Microsoft Paint)"),
                    Description = ex.Message + $"\r\n\r\n{filePath}",
                });
            }

            return;
        }

        var win32ErrorMsg = string.Empty;

        using var p10 = new Process();
        p10.StartInfo.FileName = $"\"{filePath}\"";
        p10.StartInfo.Verb = "edit";

        try
        {
            p10.Start();
            callbackFn?.Invoke();
        }
        catch (Win32Exception ex)
        {
            win32ErrorMsg = ex.Message;
        }
        catch { }

        if (string.IsNullOrEmpty(win32ErrorMsg)) return;

        using var p = new Process();
        p.StartInfo.FileName = BHelper.ResolvePath("mspaint.exe");
        p.StartInfo.Arguments = $"\"{filePath}\"";
        p.StartInfo.UseShellExecute = true;

        try
        {
            p.Start();
            callbackFn?.Invoke();
        }
        catch (Win32Exception)
        {
            _ = await ModalWindow.ShowErrorAsync(null, new ModalWindowOptions
            {
                Title = string.Format(Core.Lang[LangId.Menu_MnuEdit]),
                Description = win32ErrorMsg + $"\r\n\r\n{filePath}",
            });
        }
        catch { }

    }

    public async Task<DefaultAppScope?> SetDefaultPhotoViewerAsync(string[] extensions, bool enable)
    {
        return await Win32DefaultAppApi.SetDefaultPhotoViewerAsync(extensions, enable);
    }

    public void RepairDefaultViewerRegistration() => Win32DefaultAppApi.RepairDefaultViewerRegistration();

    public DefaultAppScope GetDefaultViewerScope() => Win32DefaultAppApi.GetScope();

    public bool IsDefaultViewerConfigurable => !Win32AppIdentity.IsPackaged || Win32AppIdentity.IsUnvirtualizedResources;

    public bool IsPackagedApp => Win32AppIdentity.IsPackaged;

    public string GetActualPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        var localCache = _localCacheDir.Value;
        if (localCache is null) return path;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!BHelper.IsPathContainedIn(path, localAppData)) return path;

        var rel = Path.GetRelativePath(localAppData, path);
        var container = Path.Combine(localCache, "Local", rel);

        return Directory.Exists(container) || File.Exists(container) ? container : path;
    }

    public void DetachIme(nint windowHandle) => Win32ImeApi.DetachIme(windowHandle);

    public void SetTitleBarDarkMode(nint windowHandle, bool isDark) => Win32WindowApi.SetTitleBarDarkMode(windowHandle, isDark);

    public void PreventSleep(string reason) => Win32PowerApi.PreventSleep();

    public void AllowSleep() => Win32PowerApi.AllowSleep();

}
