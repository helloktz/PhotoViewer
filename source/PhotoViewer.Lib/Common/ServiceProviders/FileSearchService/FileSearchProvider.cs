
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders.FileSearchService;

public partial class FileSearchProvider() : PhDisposable, IFileSearchProvider
{
    protected CancellationTokenSource? _cancelSearching;

    private static readonly bool _compareExtensionLast = BHelper.OS == OSType.Linux;

    private static readonly CompareInfo? _nameCollation = BHelper.OS == OSType.Linux ? GetLinuxCollation() : null;

    public FileSearchOptions Options { get; protected set; } = new();

    public async virtual Task SearchAsync(IEnumerable<string> dirs, FileSearchOptions options, Action<FileSearchingEventArgs>? progressFn = null)
    {
        Options = options;

        CancelSearching();
        var token = _cancelSearching.Token;

        var dirList = dirs.ToList();

        Action<FileSearchingEventArgs>? publishFn = progressFn is null
            ? null
            : e =>
            {
                if (!token.IsCancellationRequested) progressFn(e);
            };

        try
        {
            await Task.Run(() =>
            {
                foreach (var dirPath in dirList)
                {
                    if (token.IsCancellationRequested) break;
                    FindFiles(dirPath, options, publishFn, token);
                }
            }, token);
        }
        catch { }
    }

    [MemberNotNull(nameof(_cancelSearching))]
    public virtual void CancelSearching()
    {
        _cancelSearching?.Cancel();
        _cancelSearching?.Dispose();
        _cancelSearching = new();
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        CancelSearching();
    }

    protected virtual IEnumerable<FileSearchEntry> OnFiltering(IEnumerable<FileSearchEntry> fileList,
        FileSearchOptions options)
    {
        if (options.AllowedExtensions is null) return fileList;

        return fileList.Where(entry =>
        {
            var ext = Path.GetExtension(entry.FilePath).ToLowerInvariant();

            return options.AllowedExtensions.Contains(ext);
        });
    }

    protected virtual IOrderedEnumerable<FileSearchEntry> OnSorting(IEnumerable<FileSearchEntry> fileList,
        FileSearchOptions options)
    {
        return SortEntries(fileList, options);
    }

    protected void FindFiles(string dirPath, FileSearchOptions options,
        Action<FileSearchingEventArgs>? progressFn, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        var entries = EnumerateFileEntries(dirPath, options, options.SearchSubDirectories, token);

        if (token.IsCancellationRequested) return;

        var filePaths = OnFiltering(entries, options);

        if (token.IsCancellationRequested) return;

        filePaths = OnSorting(filePaths, options);

        if (token.IsCancellationRequested) return;

        progressFn?.Invoke(new FileSearchingEventArgs(filePaths.ToList()));
    }

    protected static List<FileSearchEntry> EnumerateFileEntries(string dirPath,
        FileSearchOptions options, bool searchSubDirectories, CancellationToken token)
    {
        var entries = new List<FileSearchEntry>();
        try
        {
            foreach (var file in new DirectoryInfo(dirPath).EnumerateFiles("*",
                BHelper.GetEnumerationOptions(options.IncludeHidden, searchSubDirectories)))
            {
                if (token.IsCancellationRequested) break;

                var ext = file.Extension.ToLowerInvariant();
                if (options.AllowedExtensions is not null
                    && !options.AllowedExtensions.Contains(ext)) continue;

                entries.Add(FileSearchEntry.FromFileInfo(file));
            }
        }
        catch { }

        return entries;
    }

    private static IOrderedEnumerable<FileSearchEntry> SortEntries(IEnumerable<FileSearchEntry> fileList, FileSearchOptions options)
    {
        var filePathComparer = new StringNaturalComparer(options.OrderType == ImageOrderType.Asc, StringComparison.OrdinalIgnoreCase, _compareExtensionLast, _nameCollation);
        var dirPathComparer = options.GroupByDir
            ? new StringNaturalComparer(options.OrderType == ImageOrderType.Asc, StringComparison.OrdinalIgnoreCase, collation: _nameCollation)
            : (IComparer<string?>)Comparer<string>.Create((a, b) => 0);
        var query = fileList.OrderBy(f => Path.GetDirectoryName(f.FilePath), dirPathComparer);

        if (options.OrderBy == ImageOrderBy.Random)
        {
            return query.ThenBy(_ => Guid.NewGuid());
        }

        var sorted = (options.OrderBy, options.OrderType) switch
        {
            (ImageOrderBy.FileSize, ImageOrderType.Desc) => query.ThenByDescending(f => f.FileSizeInBytes),
            (ImageOrderBy.FileSize, _) => query.ThenBy(f => f.FileSizeInBytes),
            (ImageOrderBy.DateCreated, ImageOrderType.Desc) => query.ThenByDescending(f => f.FileCreationTimeUtc),
            (ImageOrderBy.DateCreated, _) => query.ThenBy(f => f.FileCreationTimeUtc),
            (ImageOrderBy.Extension, ImageOrderType.Desc) => query.ThenByDescending(f => Path.GetExtension(f.FilePath), StringComparer.OrdinalIgnoreCase),
            (ImageOrderBy.Extension, _) => query.ThenBy(f => Path.GetExtension(f.FilePath), StringComparer.OrdinalIgnoreCase),
            (ImageOrderBy.DateAccessed, ImageOrderType.Desc) => query.ThenByDescending(f => f.FileLastAccessTimeUtc),
            (ImageOrderBy.DateAccessed, _) => query.ThenBy(f => f.FileLastAccessTimeUtc),
            (ImageOrderBy.DateModified, ImageOrderType.Desc) => query.ThenByDescending(f => f.FileLastWriteTimeUtc),
            (ImageOrderBy.DateModified, _) => query.ThenBy(f => f.FileLastWriteTimeUtc),
            _ => query,
        };

        return sorted.ThenBy(f => Path.GetFileName(f.FilePath), filePathComparer);
    }

    public static IOrderedEnumerable<string> SortFiles(IEnumerable<string> fileList, FileSearchOptions options)
    {
        var query = fileList;

        var filePathComparer = new StringNaturalComparer(options.OrderType == ImageOrderType.Asc, StringComparison.OrdinalIgnoreCase, _compareExtensionLast, _nameCollation);

        var dirPathComparer = options.GroupByDir
            ? new StringNaturalComparer(options.OrderType == ImageOrderType.Asc, StringComparison.OrdinalIgnoreCase, collation: _nameCollation)
            : (IComparer<string?>)Comparer<string>.Create((a, b) => 0);

        if (options.OrderBy == ImageOrderBy.FileSize)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenByDescending(f => new FileInfo(f).Length)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).Length)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        if (options.OrderBy == ImageOrderBy.DateCreated)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenByDescending(f => new FileInfo(f).CreationTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).CreationTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        if (options.OrderBy == ImageOrderBy.Extension)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenByDescending(f => Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        if (options.OrderBy == ImageOrderBy.DateAccessed)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenByDescending(f => new FileInfo(f).LastAccessTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).LastAccessTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        if (options.OrderBy == ImageOrderBy.DateModified)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).LastWriteTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        if (options.OrderBy == ImageOrderBy.Random)
        {
            return query
                .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                .ThenBy(_ => Guid.NewGuid());
        }

        return query
            .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
            .ThenBy(f => Path.GetFileName(f), filePathComparer);
    }

    private static CompareInfo GetLinuxCollation()
    {
        try
        {
            var locale = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } all ? all
                : Environment.GetEnvironmentVariable("LC_COLLATE") is { Length: > 0 } collate ? collate
                : Environment.GetEnvironmentVariable("LANG");

            var name = locale?.Split('.', '@')[0].Replace('_', '-');
            if (!string.IsNullOrEmpty(name) && name is not ("C" or "POSIX"))
            {
                return CultureInfo.GetCultureInfo(name).CompareInfo;
            }
        }
        catch (CultureNotFoundException) { }

        return CultureInfo.InvariantCulture.CompareInfo;
    }

}
