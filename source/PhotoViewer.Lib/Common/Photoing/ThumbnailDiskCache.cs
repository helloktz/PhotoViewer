using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using SkiaSharp;
using System;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

internal static class ThumbnailDiskCache
{
    private static string? _cacheDir;
    private static InterlockedBool _cleanupScheduled = new(false);
    private static readonly Lock _evictionLock = new();

    private static string CacheDir => _cacheDir ??= BHelper.ConfigDir(Dir.Cache, Dir.Cache_Thumbnails);

    public static async Task<SKImage?> TryGetAsync(string filePath, int thumbSize, DateTime sourceLastWriteTimeUtc, CancellationToken token = default)
    {
        if (Core.Config.GalleryCacheSizeInMb == 0)
        {
            ScheduleCleanupOnce();
            return null;
        }
        if (string.IsNullOrEmpty(filePath) || sourceLastWriteTimeUtc == default) return null;

        var cachePath = GetCacheFilePath(filePath, thumbSize);

        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(cachePath)) return null;

                var cacheTime = File.GetLastWriteTimeUtc(cachePath);
                if (cacheTime < sourceLastWriteTimeUtc) return null;

                using var data = SKData.Create(cachePath);
                if (data is null) return null;

                using var bmp = SKBitmap.Decode(data);
                if (bmp is null) return null;

                var image = SKImage.FromBitmap(bmp);
                if (image.IsDisposed()) return null;

                try
                {
                    File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow);
                }
                catch { }

                return image;
            }
            catch
            {
                return null;
            }
        }, token).ConfigureAwait(false);
    }

    public static async Task PutAsync(string filePath,
        int thumbSize, SKImage image, CancellationToken token = default)
    {
        if (Core.Config.GalleryCacheSizeInMb == 0)
        {
            ScheduleCleanupOnce();
            return;
        }
        if (string.IsNullOrEmpty(filePath)) return;
        if (image.IsDisposed()) return;

        var cachePath = GetCacheFilePath(filePath, thumbSize);

        await Task.Run(() =>
        {
            try
            {
                if (image.IsDisposed()) return;

                using var encoded = image.Encode(SKEncodedImageFormat.Webp, 80);
                if (encoded is null || encoded.Size == 0) return;

                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                using var fs = File.Create(cachePath);
                encoded.SaveTo(fs);
            }
            catch { }
        }, token).ConfigureAwait(false);

        _ = Task.Run(TryEvict, CancellationToken.None);
    }

    public static void Invalidate(string filePath)
    {
        if (Core.Config.GalleryCacheSizeInMb == 0) return;
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var pathHash = ComputePathHash(filePath);
            var dir = CacheDir;
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.EnumerateFiles(dir, $"{pathHash}_*.webp"))
            {
                try
                {
                    File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
    }

    public static Exception? Clear()
    {
        try
        {
            var dir = CacheDir;
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }

            _cacheDir = null;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static string ComputePathHash(string filePath)
    {
        Span<byte> hashBytes = stackalloc byte[16];
        XxHash128.Hash(MemoryMarshal.AsBytes(filePath.AsSpan()), hashBytes);

        return Convert.ToHexStringLower(hashBytes);
    }

    private static string GetCacheFilePath(string filePath, int thumbSize)
    {
        var pathHash = ComputePathHash(filePath);
        return Path.Combine(CacheDir, $"{pathHash}_{thumbSize}.webp");
    }

    private static void TryEvict()
    {
        if (!_evictionLock.TryEnter()) return;

        try
        {
            var maxBytes = (long)Core.Config.GalleryCacheSizeInMb * 1024L * 1024L;
            if (maxBytes <= 0) return;

            var dir = new DirectoryInfo(CacheDir);
            if (!dir.Exists) return;

            var files = dir.GetFiles("*.webp");
            var totalSize = 0L;
            for (var i = 0; i < files.Length; i++)
            {
                totalSize += files[i].Length;
            }

            if (totalSize <= maxBytes) return;

            Array.Sort(files, static (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));

            for (var i = 0; i < files.Length && totalSize > maxBytes; i++)
            {
                totalSize -= files[i].Length;
                try
                {
                    files[i].Delete();
                }
                catch { }
            }
        }
        catch { }
        finally
        {
            _evictionLock.Exit();
        }
    }

    private static void ScheduleCleanupOnce()
    {
        if (_cleanupScheduled.SetTrue())
        {
            _ = Task.Run(Clear, CancellationToken.None);
        }
    }

}
