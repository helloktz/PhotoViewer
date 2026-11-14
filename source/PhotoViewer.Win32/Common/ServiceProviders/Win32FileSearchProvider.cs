using Avalonia.Threading;
using D2Phap;
using PhotoViewer.Common;
using PhotoViewer.Common.ServiceProviders.FileSearchService;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Win32.Common.ServiceProviders;

public partial class Win32FileSearchProvider : FileSearchProvider
{

    public override async Task SearchAsync(IEnumerable<string> dirs, FileSearchOptions options, Action<FileSearchingEventArgs>? progressFn = null)
    {
        Options = options;

        CancelSearching();
        var token = _cancelSearching.Token;

        Action<FileSearchingEventArgs>? publish = progressFn is null
            ? null
            : e => Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested) progressFn(e);
            });

        if (options.ForegroundShell != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;

                try
                {
                    var foregroundView = (ShellAppView?)options.ForegroundShell;
                    FindFiles_WithShell(foregroundView, null, options, publish, token);
                }
                catch (COMException) { }
            });

            return;
        }

        var fvMap = new ConcurrentDictionary<string, ShellAppView?>();
        var dirList = dirs.ToList();

        if (options.UseExplorerSortOrder)
        {
            foreach (var dirPath in dirList)
            {
                if (token.IsCancellationRequested) return;

                ShellAppView? fv = null;
                try
                {
                    fv = await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var dirView = new EggShell().FindOpeningView(dirPath, true);
                        return dirView;
                    });
                }
                catch
                {
                }

                _ = fvMap.TryAdd(dirPath, fv);
            }
        }

        await Task.Run(() =>
        {
            foreach (var dirPath in dirList)
            {
                if (token.IsCancellationRequested) break;
                var folderShellView = fvMap.GetValueOrDefault(dirPath);

                if (folderShellView != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            FindFiles_WithShell(folderShellView, dirPath, options, publish, token);
                        }
                    });
                }

                else
                {
                    FindFiles(dirPath, options, publish, token);
                }
            }
        });

        fvMap.Clear();
    }

    private void FindFiles_WithShell(ShellAppView? fv, string? rootDir,
        FileSearchOptions options, Action<FileSearchingEventArgs>? progressFn,
        CancellationToken token)
    {
        var shellPaths = fv?.GetTabViewItems();

        rootDir ??= fv?.GetTabViewPath();

        if (shellPaths is null)
        {
            if (!string.IsNullOrWhiteSpace(rootDir))
            {
                FindFiles(rootDir, options, progressFn, token);
            }
            return;
        }

        var isFileSystemDirectory = !string.IsNullOrWhiteSpace(rootDir)
            && Path.IsPathFullyQualified(rootDir)
            && Directory.Exists(rootDir);
        var entriesByPath = isFileSystemDirectory
            ? EnumerateFileEntries(rootDir!, options, false, token).ToDictionary(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, FileSearchEntry>(StringComparer.OrdinalIgnoreCase);

        if (token.IsCancellationRequested) return;

        var entries = shellPaths
            .Select(path =>
            {
                if (path.StartsWith(EggShell.SPECIAL_DIR_PREFIX, StringComparison.InvariantCultureIgnoreCase)) return null;

                if (options.AllowedExtensions is not null)
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (!options.AllowedExtensions.Contains(ext)) return null;
                }

                try
                {
                    var entry = entriesByPath.GetValueOrDefault(path) ?? FileSearchEntry.FromPath(path);
                    var attrs = entry.Attributes;

                    if (attrs.HasFlag(FileAttributes.Directory)) return null;

                    if ((attrs & BHelper.GetSkippedFileAttributes(options.IncludeHidden)) != 0) return null;

                    return entry;
                }
                catch
                {
                    return null;
                }
            })
            .Where(entry => entry is not null)
            .Cast<FileSearchEntry>()
            .ToList();

        if (token.IsCancellationRequested) return;

        var results = options.UseExplorerSortOrder
            ? entries
            : OnSorting(entries, options).ToList();

        progressFn?.Invoke(new FileSearchingEventArgs(results));

        if (token.IsCancellationRequested) return;

        if (options.SearchSubDirectories && isFileSystemDirectory)
        {
            var subDirList = Directory.EnumerateDirectories(rootDir!, "*",
                BHelper.GetEnumerationOptions(options.IncludeHidden));

            foreach (var dirPath in subDirList)
            {
                FindFiles(dirPath, options, progressFn, token);
            }
        }
    }

}
