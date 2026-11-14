using Avalonia;
using Avalonia.Platform;
using PhotoViewer.Common.Types;
using System;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public interface IShellProvider : IDisposable
{

    object? ForegroundShell { get; set; }

    bool CanUseForegroundShell();

    object? GetForegroundWindowView();

    string? GetTargetPathFromShortcut(string? lnkFilePath);

    void OpenFilePath(string? filePath);

    void OpenFolderPath(string? dirPath);

    void DeleteFile(string filePath, bool moveToRecycleBin = true);

    void ShowOpenWith(string filePath);

    void ShowFileProperties(string filePath, nint windowHandle);

    void SetWallpaper(string filePath);

    Task SetLockScreenAsync(string filePath);

    Task OpenDefaultEditingAppAsync(string filePath, Action? callbackFn = null);

    Task<DefaultAppScope?> SetDefaultPhotoViewerAsync(string[] extensions, bool enable);

    void RepairDefaultViewerRegistration() { }

    DefaultAppScope GetDefaultViewerScope() => DefaultAppScope.CurrentUser;

    bool IsDefaultViewerConfigurable => true;

    bool IsPackagedApp => false;

    bool CanRegisterAppMenuEntry => false;

    Task<bool> RegisterAppMenuEntryAsync() => Task.FromResult(false);

    Task<bool> UnregisterAppMenuEntryAsync() => Task.FromResult(false);

    string GetActualPath(string path) => path;

    PixelRect GetScreenWorkingArea(Screen screen) => screen.WorkingArea;

    bool HasPreciseScrollingDeltas() => false;

    void DetachIme(nint windowHandle) { }

    void SetTitleBarDarkMode(nint windowHandle, bool isDark) { }

    void PreventSleep(string reason) { }

    void AllowSleep() { }
}
