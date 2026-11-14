using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace PhotoViewer.Win32.Common;

public static class Win32DefaultAppApi
{
    private const string InstallerRegistryKey = @"Software\helloktz\PhotoViewer";

    public static async Task<DefaultAppScope?> SetDefaultPhotoViewerAsync(string[] extensions, bool enable)
    {
        if (Win32AppIdentity.IsPackaged && !Win32AppIdentity.IsUnvirtualizedResources) return null;

        var scope = GetScope();
        var root = scope == DefaultAppScope.LocalMachine
            ? Registry.LocalMachine
            : Registry.CurrentUser;

        try
        {
            if (enable)
            {
                RegisterAppAndExtensions(root, extensions);
                PurgeShadowingPerUserRegistration(scope, extensions);
            }
            else
            {
                UnregisterAppAndExtensions(root, extensions);
            }

            NotifyShellAssocChanged();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            if (Win32AppIdentity.IsPackaged || IsProcessElevated()) throw;

            var exitCode = await RelaunchElevatedAsync(extensions, enable);

            if (enable && exitCode == (int)IgExitCode.Done)
            {
                PurgeShadowingPerUserRegistration(scope, extensions);
                NotifyShellAssocChanged();
            }
        }

        return scope;
    }

    private static void PurgeShadowingPerUserRegistration(DefaultAppScope scope, string[] extensions)
    {
        if (scope != DefaultAppScope.LocalMachine) return;

        try
        {
            var root = Registry.CurrentUser;

            var stale = new HashSet<string>(GetRegisteredExtensions(root), StringComparer.OrdinalIgnoreCase);
            foreach (var ext in extensions) stale.Add(ext);
            if (stale.Count == 0) return;

            UnregisterAppAndExtensions(root, [.. stale]);
        }
        catch { }
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static DefaultAppScope GetScope()
    {
        if (Win32AppIdentity.IsPackaged) return DefaultAppScope.CurrentUser;

        var exeDir = Path.GetDirectoryName(BHelper.AppExePath);
        if (string.IsNullOrEmpty(exeDir)) return DefaultAppScope.CurrentUser;

        if (IsInstalledAt(Registry.LocalMachine, exeDir)) return DefaultAppScope.LocalMachine;
        if (IsInstalledAt(Registry.CurrentUser, exeDir)) return DefaultAppScope.CurrentUser;

        string[] machineRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];

        foreach (var machineRoot in machineRoots)
        {
            if (BHelper.IsPathContainedIn(exeDir, machineRoot))
            {
                return DefaultAppScope.LocalMachine;
            }
        }

        return DefaultAppScope.CurrentUser;
    }

    private static bool IsInstalledAt(RegistryKey root, string exeDir)
    {
        using var installerKey = root.OpenSubKey(InstallerRegistryKey);
        if (installerKey?.GetValue("InstallLocation") is not string installDir || !Path.IsPathFullyQualified(installDir))
        {
            return false;
        }

        return Path.TrimEndingDirectorySeparator(exeDir).Equals(
            Path.TrimEndingDirectorySeparator(installDir), StringComparison.OrdinalIgnoreCase);
    }

    private static string LaunchCommandExe => BHelper.AppExePath;

    public static void RepairDefaultViewerRegistration()
    {
        try
        {
            if (Win32AppIdentity.IsPackaged && !Win32AppIdentity.IsUnvirtualizedResources) return;

            if (GetScope() != DefaultAppScope.CurrentUser) return;

            var root = Registry.CurrentUser;
            var extensions = GetRegisteredExtensions(root);
            if (extensions.Length == 0) return;

            var registeredExe = GetRegisteredCommandExe(root, extensions[0]);
            var isCommandAlive = registeredExe.Length > 0 && File.Exists(registeredExe);

            if (isCommandAlive)
            {
                var isOurCommand = registeredExe.Equals(LaunchCommandExe, StringComparison.OrdinalIgnoreCase);
                if (!isOurCommand || IsRegisteredIconAlive(root, extensions)) return;
            }

            RefreshRegisteredPaths(root, extensions);
            NotifyShellAssocChanged();
        }
        catch { }
    }

    private static void RefreshRegisteredPaths(RegistryKey root, string[] extensions)
    {
        using (var key = root.OpenSubKey($@"Software\{BHelper.AppName}\Capabilities", writable: true))
        {
            key?.SetValue("ApplicationIcon", $"\"{BHelper.AppExePath}\", 0");
        }

        using var classesKey = root.OpenSubKey(@"Software\Classes", writable: true);
        if (classesKey is null) return;

        foreach (var ext in extensions)
        {
            var extNoDot = ext.TrimStart('.').ToUpperInvariant();
            RegisterProgId(classesKey, GetProgId(extNoDot), extNoDot);
        }
    }

    private static string[] GetRegisteredExtensions(RegistryKey root)
    {
        using var faKey = root.OpenSubKey($@"Software\{BHelper.AppName}\Capabilities\FileAssociations");
        if (faKey is null) return [];

        return faKey.GetValueNames().Where(name => name.StartsWith('.')).ToArray();
    }

    private static string GetRegisteredCommandExe(RegistryKey root, string ext)
    {
        var extNoDot = ext.TrimStart('.').ToUpperInvariant();
        var progId = GetProgId(extNoDot);

        using var cmdKey = root.OpenSubKey($@"Software\Classes\{progId}\shell\open\command");
        if (cmdKey?.GetValue("") is not string command) return string.Empty;

        return ExtractCommandExePath(command);
    }

    private static bool IsRegisteredIconAlive(RegistryKey root, string[] extensions)
    {
        foreach (var ext in extensions)
        {
            var extNoDot = ext.TrimStart('.').ToUpperInvariant();
            var progId = GetProgId(extNoDot);

            using var iconKey = root.OpenSubKey($@"Software\Classes\{progId}\DefaultIcon");

            if (iconKey?.GetValue("") is not string iconPath || iconPath.Length == 0) continue;

            return File.Exists(iconPath);
        }

        return true;
    }

    private static string ExtractCommandExePath(string command)
    {
        if (!command.StartsWith('"')) return string.Empty;

        var closingQuote = command.IndexOf('"', 1);

        return closingQuote > 1 ? command[1..closingQuote] : string.Empty;
    }

    private static void RegisterAppAndExtensions(RegistryKey root, string[] extensions)
    {
        var capabilitiesPath = $@"Software\{BHelper.AppName}\Capabilities";
        var classesKey = root.OpenSubKey(@"Software\Classes", writable: true);

        var dropped = GetRegisteredExtensions(root).Except(extensions, StringComparer.OrdinalIgnoreCase).ToArray();
        UnregisterExtensions(classesKey, dropped);

        using (var key = root.OpenSubKey(@"Software\RegisteredApplications", writable: true))
        {
            key?.SetValue(BHelper.AppName, capabilitiesPath);
        }

        using (var key = root.CreateSubKey(capabilitiesPath, writable: true))
        {
            key.SetValue("ApplicationName", BHelper.AppDisplayName);
            key.SetValue("ApplicationIcon", $"\"{BHelper.AppExePath}\", 0");
            key.SetValue("ApplicationDescription", "A Fast, Seamless Photo Viewer");

            using var faKey = key.CreateSubKey("FileAssociations", writable: true);

            foreach (var ext in dropped) faKey.DeleteValue(ext, throwOnMissingValue: false);

            foreach (var ext in extensions)
            {
                var extNoDot = ext.TrimStart('.').ToUpperInvariant();
                var progId = GetProgId(extNoDot);
                faKey.SetValue(ext, progId);

                RegisterProgId(classesKey, progId, extNoDot);
                AssociateExtensionDefault(classesKey, ext, progId);
                ClearUserChoice(ext);
            }
        }

        classesKey?.Dispose();
    }

    private static string GetProgId(string extNoDot) => $"{BHelper.AppName}.AssocFile.{extNoDot}";

    private static string GetFriendlyTypeName(string extNoDot) => Core.Lang[LangId.Settings_FileTypeName, BHelper.AppDisplayName, extNoDot];

    private static void RegisterProgId(RegistryKey? classesKey, string progId, string extNoDot)
    {
        if (classesKey is null) return;

        using var progIdKey = classesKey.CreateSubKey(progId, writable: true);

        var friendlyTypeName = GetFriendlyTypeName(extNoDot);
        progIdKey.SetValue("", friendlyTypeName);

        progIdKey.SetValue("FriendlyTypeName", friendlyTypeName);

        var iconPath = ResolveExtIconPath(extNoDot);
        if (!string.IsNullOrEmpty(iconPath))
        {
            using var iconKey = progIdKey.CreateSubKey("DefaultIcon", writable: true);
            iconKey.SetValue("", iconPath);
        }
        else
        {
            progIdKey.DeleteSubKeyTree("DefaultIcon", throwOnMissingSubKey: false);
        }

        using var shellKey = progIdKey.CreateSubKey("shell", writable: true);
        using var openKey = shellKey.CreateSubKey("open", writable: true);
        openKey.SetValue("FriendlyAppName", BHelper.AppDisplayName);

        using var commandKey = openKey.CreateSubKey("command", writable: true);
        commandKey.SetValue("", $"\"{LaunchCommandExe}\" \"%1\"");
    }

    private static string? ResolveExtIconPath(string extNoDot)
    {
        var userIcon = BHelper.GetRealPlatformConfigDir(Dir.ExtIcons, $"{extNoDot}.ico");
        if (File.Exists(userIcon)) return userIcon;

        var bundledIcon = BHelper.BaseDir(Dir.BundledExtIcons, $"{extNoDot}.ico");
        return File.Exists(bundledIcon) ? bundledIcon : null;
    }

    private static void AssociateExtensionDefault(RegistryKey? classesKey, string ext, string progId)
    {
        if (classesKey is null) return;

        using var extKey = classesKey.CreateSubKey(ext, writable: true);
        extKey.SetValue("", progId);

        using var openWith = extKey.CreateSubKey("OpenWithProgids", writable: true);
        openWith.SetValue(progId, string.Empty);
    }

    private static void ClearUserChoice(string ext, string? onlyIfProgId = null)
    {
        try
        {
            using var fileExts = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}", writable: true);
            if (fileExts is null) return;

            if (onlyIfProgId is not null)
            {
                using var uc = fileExts.OpenSubKey("UserChoice");
                if (uc?.GetValue("ProgId") as string != onlyIfProgId) return;
            }

            fileExts.DeleteSubKey("UserChoice", throwOnMissingSubKey: false);
        }
        catch { }
    }

    private static void UnregisterAppAndExtensions(RegistryKey root, string[] extensions)
    {
        var registeredExtensions = GetRegisteredExtensions(root).Union(extensions, StringComparer.OrdinalIgnoreCase);
        using var classesKey = root.OpenSubKey(@"Software\Classes", writable: true);
        UnregisterExtensions(classesKey, registeredExtensions);

        using (var key = root.OpenSubKey(@"Software\RegisteredApplications", writable: true))
        {
            key?.DeleteValue(BHelper.AppName, throwOnMissingValue: false);
        }

        using (var key = root.OpenSubKey("Software", writable: true))
        {
            key?.DeleteSubKeyTree(BHelper.AppName, throwOnMissingSubKey: false);
        }
    }

    private static void UnregisterExtensions(RegistryKey? classesKey, IEnumerable<string> extensions)
    {
        if (classesKey is null) return;

        foreach (var ext in extensions)
        {
            var extNoDot = ext.TrimStart('.').ToUpperInvariant();
            var progId = GetProgId(extNoDot);

            classesKey.DeleteSubKeyTree(progId, throwOnMissingSubKey: false);

            using var extKey = classesKey.OpenSubKey(ext, writable: true);
            if (extKey is not null)
            {
                if (extKey.GetValue("") as string == progId)
                {
                    extKey.DeleteValue("", throwOnMissingValue: false);
                }

                using var openWith = extKey.OpenSubKey("OpenWithProgids", writable: true);
                openWith?.DeleteValue(progId, throwOnMissingValue: false);
            }

            ClearUserChoice(ext, progId);
        }
    }

    private static unsafe void NotifyShellAssocChanged()
    {
        PInvoke.SHChangeNotify(
            SHCNE_ID.SHCNE_ASSOCCHANGED,
            SHCNF_FLAGS.SHCNF_IDLIST,
            null, null);
    }

    private static async Task<int> RelaunchElevatedAsync(string[] extensions, bool enable)
    {
        var cmd = enable
            ? AppCmds.SET_DEFAULT_PHOTO_VIEWER
            : AppCmds.REMOVE_DEFAULT_PHOTO_VIEWER;
        var extArg = string.Join(";", extensions);
        string[] args = [cmd, extArg];

        return await BHelper.RunExeAsync(BHelper.AppExePath, args, asAdmin: true, waitForExit: true);
    }

}
