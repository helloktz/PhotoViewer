using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public partial class PhotoManager
{
    private CancellationTokenSource? _cacheCts;
    private readonly Lock _cacheLock = new();

    private readonly HashSet<int> _cachedIndexes = [];

    public int CachedCount
    {
        get
        {
            lock (_cacheLock) return _cachedIndexes.Count;
        }
    }

    public double CachedMemoryMb
    {
        get
        {
            var bytes = EstimateCachedMemory(CurrentIndex);
            return Math.Round(bytes / (1024.0 * 1024.0), 1);
        }
    }

    public int[] CachedIndexSnapshot
    {
        get
        {
            lock (_cacheLock) return [.. _cachedIndexes];
        }
    }

    public bool IsCachingEnabled => Core.Config.CacheMaxFiles > 0
        && Core.Config.CacheMaxMemoryInMb > 0
        && Core.Config.CacheMaxFileSizeInMb > 0
        && Core.Config.CacheMaxDimension > 0;

    public void RequestCacheAround(int centerIndex)
    {
        if (Core.API.IsQuickBrowsing) return;

        CancellationToken token;
        lock (_cacheLock)
        {
            _cacheCts?.Cancel();
            _cacheCts?.Dispose();
            _cacheCts = new CancellationTokenSource();
            token = _cacheCts.Token;
        }

        var isSlideshow = Core.Slideshow?.IsRunning == true;

        if (!isSlideshow && !IsCachingEnabled) return;

        _ = Task.Factory.StartNew(
            () => RunCacheAroundAsync(centerIndex, isSlideshow, token),
            token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public void CancelCaching()
    {
        lock (_cacheLock)
        {
            _cacheCts?.Cancel();
            _cacheCts?.Dispose();
            _cacheCts = null;
        }
    }

    public void ClearCache(Photo? excludePhoto = null)
    {
        CancelCaching();

        lock (_cacheLock)
        {
            _cachedIndexes.Clear();
        }

        UnloadPhotosOutside([], excludePhoto);
    }

    private void UnloadPhotosOutside(HashSet<int> keepIndexes, Photo? excludePhoto, bool keepCurrent = false)
    {
        Photo[] snapshot;
        lock (_lock)
        {
            snapshot = [.. Items];
        }

        for (var i = 0; i < snapshot.Length; i++)
        {
            if (keepIndexes.Contains(i)) continue;
            if (keepCurrent && i == CurrentIndex) continue;

            var photo = snapshot[i];
            if (photo.State != PhotoState.Loaded) continue;
            if (ReferenceEquals(photo, excludePhoto)) continue;

            photo.Unload();
        }
    }

    private void TrackCached(int index)
    {
        lock (_cacheLock)
        {
            _cachedIndexes.Add(index);
        }
    }

    public void InvalidateCacheAt(int index)
    {
        lock (_cacheLock)
        {
            if (!_cachedIndexes.Remove(index)) return;
        }

        Get(index)?.Unload();
    }

    public void InvalidateCacheAt(string filePath)
    {
        var index = IndexOf(filePath);
        if (index >= 0) InvalidateCacheAt(index);
    }

    private async Task RunCacheAroundAsync(int centerIndex, bool isSlideshow, CancellationToken token)
    {
        try
        {
            var maxFiles = Core.Config.CacheMaxFiles;
            var maxMemoryBytes = (long)Core.Config.CacheMaxMemoryInMb * 1024L * 1024L;
            var maxFileSizeBytes = (long)(Core.Config.CacheMaxFileSizeInMb * 1024.0 * 1024.0);
            var maxDimension = Core.Config.CacheMaxDimension;
            var totalCount = (int)Count;

            if (totalCount == 0 || centerIndex < 0) return;

            var maxRange = Math.Min(totalCount / 2 + 1, totalCount);

            var canLoop = !isSlideshow || Core.Config.EnableLoopSlideshow;
            var indexes = GenerateSpiralIndexes(centerIndex, maxRange, totalCount,
                primaryDirection: isSlideshow ? 1 : 0, canLoop);

            var guaranteedIndex = isSlideshow ? GetForwardIndex(centerIndex, totalCount, canLoop) : -1;

            var currentPhoto = Get(centerIndex);
            var usedMemory = currentPhoto is null ? 0L : EstimatePhotoMemory(currentPhoto);

            var newCachedSet = new HashSet<int>();

            foreach (var idx in indexes)
            {
                if (token.IsCancellationRequested) return;

                if (Core.API.IsQuickBrowsing) return;

                var photo = Get(idx);
                if (photo is null) continue;

                var isGuaranteed = idx == guaranteedIndex;

                if (!isGuaranteed && maxFiles > 0 && newCachedSet.Count >= maxFiles) break;

                if (photo.State == PhotoState.Loaded)
                {
                    var photoMem = EstimatePhotoMemory(photo);
                    if (!isGuaranteed && usedMemory + photoMem > maxMemoryBytes) break;

                    usedMemory += photoMem;
                    newCachedSet.Add(idx);
                    usedMemory += await PrepareForDisplayAsync(photo, usedMemory, maxMemoryBytes, isGuaranteed, token);
                    continue;
                }

                if (idx == CurrentIndex)
                {
                    newCachedSet.Add(idx);
                    continue;
                }

                photo.ReadOptions = PhotoReadOptions.FromConfig(photo.GetDefaultFrameIndex());

                if (!isSlideshow)
                {
                    if (maxFileSizeBytes > 0 && !SatisfiesFileSizeLimit(photo.FilePath, maxFileSizeBytes))
                    {
                        continue;
                    }

                    if (maxDimension > 0)
                    {
                        await photo.LoadMetadataAsync(useCache: true, token: token);
                        if (token.IsCancellationRequested) return;

                        var (decodeW, decodeH) = photo.EstimatedDecodeSize;
                        if (decodeW > maxDimension || decodeH > maxDimension)
                        {
                            continue;
                        }
                    }
                }
                else
                {
                    await photo.LoadMetadataAsync(useCache: true, token: token);
                    if (token.IsCancellationRequested) return;
                }

                var estimatedMem = EstimatePhotoMemoryFromMetadata(photo);
                if (!isGuaranteed && usedMemory + estimatedMem > maxMemoryBytes) break;

                await photo.LoadAsync(useCache: true, skipLoadingEvent: true);

                TrackCached(idx);
                if (token.IsCancellationRequested) return;

                var decodedMem = EstimatePhotoMemory(photo);
                usedMemory += decodedMem > 0 ? decodedMem : estimatedMem;
                newCachedSet.Add(idx);
                usedMemory += await PrepareForDisplayAsync(photo, usedMemory, maxMemoryBytes, isGuaranteed, token);
            }

            if (token.IsCancellationRequested) return;

            lock (_cacheLock)
            {
                _cachedIndexes.Clear();
                foreach (var idx in newCachedSet)
                {
                    _cachedIndexes.Add(idx);
                }
            }

            var keepIndexes = new HashSet<int>(newCachedSet) { centerIndex, CurrentIndex };
            UnloadPhotosOutside(keepIndexes, null, keepCurrent: true);
        }
        catch (OperationCanceledException) {   }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌❌❌ RunCacheAroundAsync: {ex.Message}");
        }
    }

    private static List<int> GenerateSpiralIndexes(int centerIndex, int maxRange, int totalCount,
        int primaryDirection = 0, bool canLoop = true)
    {
        var result = new List<int>(maxRange * 2);
        var seen = new HashSet<int>();

        void TryAdd(int rawIndex)
        {
            int idx;
            if (canLoop)
            {
                idx = BHelper.ComputeIndexInRange(rawIndex, (uint)totalCount, true);
            }
            else
            {
                if (rawIndex < 0 || rawIndex >= totalCount) return;
                idx = rawIndex;
            }

            if (idx != centerIndex && seen.Add(idx))
            {
                result.Add(idx);
            }
        }

        if (primaryDirection > 0)
        {
            for (var i = 1; i <= maxRange; i++) TryAdd(centerIndex + i);
            for (var i = 1; i <= maxRange; i++) TryAdd(centerIndex - i);
        }
        else if (primaryDirection < 0)
        {
            for (var i = 1; i <= maxRange; i++) TryAdd(centerIndex - i);
            for (var i = 1; i <= maxRange; i++) TryAdd(centerIndex + i);
        }
        else
        {
            for (var i = 1; i <= maxRange; i++)
            {
                TryAdd(centerIndex + i);
                TryAdd(centerIndex - i);
            }
        }

        return result;
    }

    private static int GetForwardIndex(int centerIndex, int totalCount, bool canLoop)
    {
        var raw = centerIndex + 1;
        if (canLoop) return BHelper.ComputeIndexInRange(raw, (uint)totalCount, true);
        return raw < totalCount ? raw : -1;
    }

    private static bool SatisfiesFileSizeLimit(string filePath, long maxFileSizeBytes)
    {
        try
        {
            var fi = new FileInfo(filePath);
            return fi.Length <= maxFileSizeBytes;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<long> PrepareForDisplayAsync(Photo photo, long usedMemory, long maxMemoryBytes,
        bool isGuaranteed, CancellationToken token)
    {
        if (token.IsCancellationRequested) return 0;

        var pass = DisplayColorPass.For(photo.Metadata, Core.Config.EnableHdrToneMapping);
        if (pass.IsNoOp) return 0;

        var bytesPerPixel = pass.IsHdr ? 4 : photo.BytesPerPixel;
        var displayBytes = (long)photo.Width * photo.Height * bytesPerPixel;
        if (!isGuaranteed && usedMemory + displayBytes > maxMemoryBytes) return 0;

        var bytesBefore = photo.DisplayFrameBytes;
        await photo.PrepareDisplayFrameAsync(pass, token);

        return photo.DisplayFrameBytes - bytesBefore;
    }

    private static long EstimatePhotoMemory(Photo photo)
    {
        if (photo.State != PhotoState.Loaded) return 0;

        return (long)photo.Width * photo.Height * photo.BytesPerPixel + photo.DisplayFrameBytes;
    }

    private static long EstimatePhotoMemoryFromMetadata(Photo photo)
    {
        var (w, h) = photo.EstimatedDecodeSize;
        if (w == 0 || h == 0) return 8L * 1024 * 1024;

        var isDeep = !photo.WillDecodeEmbeddedPreview && photo.Metadata.BitsPerChannel > 8;

        return (long)w * h * (isDeep ? 8 : 4);
    }

    private long EstimateCachedMemory(int centerIndex)
    {
        long total = 0;

        var current = Get(centerIndex);
        if (current is not null)
        {
            total += EstimatePhotoMemory(current);
        }

        lock (_cacheLock)
        {
            foreach (var idx in _cachedIndexes)
            {
                var photo = Get(idx);
                if (photo is not null)
                {
                    total += EstimatePhotoMemory(photo);
                }
            }
        }

        return total;
    }
}
