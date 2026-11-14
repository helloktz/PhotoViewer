using Avalonia;
using Avalonia.Controls;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common;

public partial class BHelper
{
    private static string Win32ShortcutExtension => ".lnk";

    private static readonly Lazy<(string Mounted, string Real)?> _appDirMount = new(ReadAppDirMount);

    public static string BasePath => GetRealPlatformPath(AppDomain.CurrentDomain.BaseDirectory);

    public static string ConfigPath => ConfigMode.IsPortable
        ? BaseDir()
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string BaseDir(params string[] paths)
    {
        var newPaths = paths.ToList();
        newPaths.Insert(0, BasePath);
        var path = Path.Combine([.. newPaths]);

        return ToProcessPath(path);
    }

    public static string ConfigDir(params string[] paths)
    {
        var isDirCreated = false;

        if (paths.Length > 0)
        {
            var firstPath = paths[0];
            var isBuiltinDir = firstPath.Equals(Dir.Themes, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.ExtIcons, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Plugins, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Cache, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Temporary, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Logs, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Transitions, StringComparison.OrdinalIgnoreCase);

            if (isBuiltinDir)
            {
                var builtinConfigPath = Path.Combine(ConfigPath, firstPath);
                TryCreateDirectory(builtinConfigPath);
                isDirCreated = true;
            }
        }

        if (!isDirCreated)
        {
            TryCreateDirectory(ConfigPath);
        }

        var newPaths = paths.ToList();
        newPaths.Insert(0, ConfigPath);
        var path = Path.Combine([.. newPaths]);

        return path;
    }

    private static void TryCreateDirectory(string dirPath)
    {
        try
        {
            Directory.CreateDirectory(dirPath);
        }
        catch { }
    }

    public static string GetRealPlatformConfigDir(params string[] paths)
    {
        return GetRealPlatformPath(ConfigDir(paths));
    }

    private static (string Mounted, string Real)? ReadAppDirMount()
    {
        if (!IsFlatpakSandbox) return null;

        try
        {
            const string key = "app-path=";

            foreach (var line in File.ReadLines("/.flatpak-info"))
            {
                if (!line.StartsWith(key, StringComparison.Ordinal)) continue;

                var realPath = line[key.Length..].Trim().TrimEnd('/');
                return string.IsNullOrEmpty(realPath) ? null : ("/app", realPath);
            }
        }
        catch { }

        return null;
    }

    public static string GetRealPlatformPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? string.Empty;

        var appDir = _appDirMount.Value;
        var realPath = appDir is null
            ? path
            : SwapPathPrefix(path, appDir.Value.Mounted, appDir.Value.Real);

        return Core.ShellProvider?.GetActualPath(realPath) ?? realPath;
    }

    private static string ToProcessPath(string path)
    {
        var appDir = _appDirMount.Value;

        return appDir is null
            ? path
            : SwapPathPrefix(path, appDir.Value.Real, appDir.Value.Mounted);
    }

    private static string SwapPathPrefix(string path, string from, string to)
    {
        if (!path.StartsWith(from, StringComparison.Ordinal)) return path;

        var rest = path[from.Length..];
        if (rest.Length > 0 && rest[0] != Path.DirectorySeparatorChar) return path;

        return to + rest;
    }

    public static bool CheckPathWritable(PathType type, string path)
    {
        try
        {
            if (type == PathType.File)
            {
                using (File.OpenWrite(path)) { }
            }

            else
            {
                var isDirExist = Directory.Exists(path);

                if (!isDirExist)
                {
                    Directory.CreateDirectory(path);
                }

                var sampleFile = Path.Combine(path, "test_write_file.temp");

                using (File.Create(sampleFile)) { }
                File.Delete(sampleFile);

                if (!isDirExist)
                {
                    Directory.Delete(path, true);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static PathType CheckPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return PathType.Unknown;

        try
        {
            var attrs = File.GetAttributes(path);

            if (attrs.HasFlag(FileAttributes.Directory))
            {
                return PathType.Dir;
            }

            return PathType.File;
        }
        catch { }

        return PathType.Unknown;
    }

    public static bool IsPathContainedIn(string? path, string? root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;

        string fullPath, fullRoot;
        try
        {
            fullPath = Path.GetFullPath(path);
            fullRoot = Path.GetFullPath(root);
        }
        catch
        {
            return false;
        }

        var sep = Path.DirectorySeparatorChar;
        if (!fullRoot.EndsWith(sep)) fullRoot += sep;

        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return (fullPath + sep).StartsWith(fullRoot, comparison);
    }

    public static (List<string> DirPaths, List<string> FilePaths) GetDistinctDirsFromPaths(IEnumerable<string> pathList)
    {
        if (!pathList.Any()) return ([], []);

        var hashedDirsList = new HashSet<string>();
        var hashedFilesList = new HashSet<string>();

        foreach (var path in pathList)
        {
            var pathType = BHelper.CheckPath(path);
            if (pathType == PathType.Unknown) continue;

            if (pathType == PathType.Dir)
            {
                hashedDirsList.Add(path);
            }
            else
            {
                string? dir;

                if (string.Equals(Path.GetExtension(path), Win32ShortcutExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (Core.ShellProvider is null) continue;

                    var shortcutPath = Core.ShellProvider.GetTargetPathFromShortcut(path);
                    if (string.IsNullOrEmpty(shortcutPath)) continue;

                    var shortcutPathType = BHelper.CheckPath(shortcutPath);
                    if (shortcutPathType == PathType.Unknown) continue;

                    if (shortcutPathType == PathType.Dir)
                    {
                        dir = shortcutPath;
                    }
                    else
                    {
                        hashedFilesList.Add(shortcutPath);
                        dir = Path.GetDirectoryName(shortcutPath) ?? "";
                    }
                }
                else
                {
                    hashedFilesList.Add(path);
                    dir = Path.GetDirectoryName(path) ?? null;
                }

                if (string.IsNullOrEmpty(dir)) continue;
                hashedDirsList.Add(dir);
            }
        }

        return ([.. hashedDirsList], [.. hashedFilesList]);
    }

    public static string? GetSiblingDir(string? currentPath, int direction,
        ICollection<string> allowedExtensions, bool includeHidden)
    {
        if (string.IsNullOrEmpty(currentPath)) return null;

        var currentDir = CheckPath(currentPath) == PathType.File
            ? Path.GetDirectoryName(currentPath)
            : currentPath;
        if (string.IsNullOrEmpty(currentDir)) return null;

        currentDir = currentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentDir = Directory.GetParent(currentDir)?.FullName;
        if (string.IsNullOrEmpty(parentDir)) return null;

        try
        {
            var siblingDirs = Directory.GetDirectories(parentDir)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var currentIndex = siblingDirs.FindIndex(d =>
                string.Equals(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    currentDir, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0) return null;

            for (var i = currentIndex + direction; i >= 0 && i < siblingDirs.Count; i += direction)
            {
                if (IsPathSkippedByAttributes(siblingDirs[i], includeHidden)) continue;
                if (DirContainsImage(siblingDirs[i], allowedExtensions, includeHidden)) return siblingDirs[i];
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return null;
    }

    public static bool DirContainsImage(string? dir, ICollection<string> allowedExtensions, bool includeHidden)
    {
        if (string.IsNullOrEmpty(dir)) return false;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", GetEnumerationOptions(includeHidden)))
            {
                if (allowedExtensions.Contains(Path.GetExtension(file))) return true;
            }
        }
        catch { }

        return false;
    }

    public static EnumerationOptions GetEnumerationOptions(bool includeHidden, bool recurse = false)
    {
        return new EnumerationOptions()
        {
            IgnoreInaccessible = true,
            AttributesToSkip = GetSkippedFileAttributes(includeHidden),
            RecurseSubdirectories = recurse,
        };
    }

    public static FileAttributes GetSkippedFileAttributes(bool includeHidden)
    {
        var attrs = FileAttributes.System;
        if (!includeHidden) attrs |= FileAttributes.Hidden;

        return attrs;
    }

    public static bool IsPathSkippedByAttributes(string? path, bool includeHidden)
    {
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            return (File.GetAttributes(path) & GetSkippedFileAttributes(includeHidden)) != 0;
        }
        catch { return false; }
    }

    public static bool IsPathSkippedUnderRoot(string? path, string? rootDir, bool includeHidden)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (IsPathSkippedByAttributes(path, includeHidden)) return true;
        if (string.IsNullOrEmpty(rootDir)) return false;

        var root = rootDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = Path.GetDirectoryName(path);

        while (!string.IsNullOrEmpty(dir)
            && !string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root, StringComparison.OrdinalIgnoreCase))
        {
            if (IsPathSkippedByAttributes(dir, includeHidden)) return true;
            dir = Path.GetDirectoryName(dir);
        }

        return false;
    }

    public static string ResolvePath(string? inputPath)
    {
        if (string.IsNullOrEmpty(inputPath))
            return inputPath ?? string.Empty;

        var path = inputPath;
        const string protocol = Const.APP_PROTOCOL + ":";

        if (path.StartsWith(protocol))
        {
            path = Uri.UnescapeDataString(path)[protocol.Length..];
        }

        if (path.Length > 2 && path.StartsWith('"') && path.EndsWith('"'))
        {
            path = path[1..^1];
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            path = Environment.ExpandEnvironmentVariables(path);
        }

        if (string.Equals(Path.GetExtension(inputPath), Win32ShortcutExtension, StringComparison.OrdinalIgnoreCase))
        {
            path = Core.ShellProvider?.GetTargetPathFromShortcut(path) ?? path;
        }

        if (OS == OSType.Mac
            && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(path))
        {
            var exeName = GetMacOsAppExecutableName(path)
                ?? Path.GetFileNameWithoutExtension(path.TrimEnd('/'));

            var innerExe = Path.Combine(path, "Contents", "MacOS", exeName);
            if (File.Exists(innerExe)) path = innerExe;
        }

        return ToAbsolutePath(path);
    }

    private static string ToAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path ?? string.Empty;
        if (Path.IsPathFullyQualified(path)) return path;

        try
        {
            var fullPath = Path.GetFullPath(path);

            return File.Exists(fullPath) || Directory.Exists(fullPath) ? fullPath : path;
        }
        catch { return path; }
    }

    private static string? GetMacOsAppExecutableName(string appBundlePath)
    {
        var plistPath = Path.Combine(appBundlePath, "Contents", "Info.plist");
        if (!File.Exists(plistPath)) return null;

        try
        {
            var doc = System.Xml.Linq.XDocument.Load(plistPath);
            var dict = doc.Root?.Element("dict");
            if (dict is null) return null;

            var elements = dict.Elements().ToList();
            for (var i = 0; i < elements.Count - 1; i++)
            {
                if (elements[i].Name.LocalName == "key"
                    && elements[i].Value == "CFBundleExecutable"
                    && elements[i + 1].Name.LocalName == "string")
                {
                    var name = elements[i + 1].Value.Trim();
                    return string.IsNullOrEmpty(name) ? null : name;
                }
            }
        }
        catch { }

        return null;
    }

    public static string BuildConfigCmdLine(string configName, object? configValue)
    {
        if (configValue == null) return string.Empty;

        return $"{Const.CONFIG_CMD_PREFIX}{configName}=\"{configValue}\"";
    }

    public static async Task OpenSystemSettingsAsync(Visual? visual, string settingsUri)
    {
        if (!Uri.TryCreate(settingsUri, UriKind.Absolute, out var uri)
            || uri.Scheme != "ms-settings") return;

        try
        {
            var launcher = TopLevel.GetTopLevel(visual)?.Launcher;
            if (launcher is not null) await launcher.LaunchUriAsync(uri);
        }
        catch { }
    }

    public static void OpenFilePath(string? filePath)
    {
        if (Core.ShellProvider is null) return;

        Core.ShellProvider.OpenFilePath(filePath);
    }

    public static void OpenFolderPath(string? dirPath)
    {
        if (Core.ShellProvider is null) return;

        Core.ShellProvider.OpenFolderPath(dirPath);
    }

    public static void DeleteFile(string filePath, bool moveToRecycleBin = true)
    {
        if (Core.ShellProvider is not null)
        {
            Core.ShellProvider.DeleteFile(filePath, moveToRecycleBin);
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch { }
    }

}
