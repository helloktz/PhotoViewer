using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Printing;

public interface IPrintImageSource
{

    int Count { get; }

    PrintCaption GetCaption(int index);

    SKPicture? GetPicture(int index);

    SKImageRef.ImageLease? TryGetImage(int index, int longSide);

    Task<SKImageRef.ImageLease?> GetImageAsync(int index, int longSide, CancellationToken token);
}

public sealed record PrintItem(int FrameIndex, PrintRegion Region, PrintCaption Caption);

public readonly record struct PrintCaption(string Name, string Page)
{
    public static PrintCaption Empty { get; } = new(string.Empty, string.Empty);

    public override string ToString() => string.IsNullOrEmpty(Page) ? Name : $"{Name} {Page}";
}

public sealed class PrintSession : PhDisposable, IPrintImageSource
{
    private static readonly int[] _buckets = [256, 512, 1024, 2048, 4096, 8192, 16384];
    private const int FULL_SIZE = int.MaxValue;
    private const long CACHE_BUDGET_BYTES = 256L * 1024 * 1024;

    private readonly ViewerImageState _state;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly SemaphoreSlim _decodeGate = new(2, 2);
    private readonly Dictionary<(int Frame, PrintRegion Region, int Bucket), SKImageRef> _cache = [];
    private readonly LinkedList<(int, PrintRegion, int)> _lru = new();
    private readonly HashSet<(int, PrintRegion, int)> _pending = [];
    private long _cacheBytes;

    private Task<PhotoFrameReader?>? _readerTask;
    private SKSvg? _svg;
    private IReadOnlyList<PrintItem> _items = [];

    public event Action? ImageReady;

    public ViewerImageState State => _state;

    public int FrameCount { get; }

    public IReadOnlyList<PrintItem> Items => _items;

    public int Count => _items.Count;

    public PrintSession(ViewerImageState state)
    {
        _state = state;

        var meta = state.Photo.Metadata;
        FrameCount = state.Photo.IsClipboard ? 1 : (int)Math.Max(1, meta?.FrameCount ?? 1);
        SetItems([state.FrameIndex], PrintRegion.WholeImage);
    }

    public void SetItems(IReadOnlyList<int> frameIndexes, PrintRegion region)
    {
        var frames = region == PrintRegion.WholeImage ? frameIndexes : [_state.FrameIndex];
        var name = GetPhotoName();

        _items = frames
            .Where(i => i >= 0 && i < FrameCount)
            .Select(i => new PrintItem(i, region, new PrintCaption(name, FrameCount > 1 ? $"({i + 1}/{FrameCount})" : string.Empty)))
            .ToList();
    }

    public float GetAspect(int index)
    {
        if (index < 0 || index >= _items.Count) return 1;
        var item = _items[index];

        var shown = _state.Image;
        if (item.FrameIndex == _state.FrameIndex && !shown.IsDisposed())
        {
            var rect = _state.GetRegionRect(ToViewerRegion(item.Region));
            if (rect.Width > 0 && rect.Height > 0) return rect.Width / (float)rect.Height;
        }

        lock (_lock)
        {
            foreach (var (key, img) in _cache)
            {
                if (key.Frame != item.FrameIndex || key.Region != item.Region || img.Image.IsDisposed()) continue;
                return img.Image.Width / (float)img.Image.Height;
            }
        }

        var meta = _state.Photo.Metadata;
        return meta is { Width: > 0, Height: > 0 } ? meta.Width / (float)meta.Height : 1;
    }

    public PrintCaption GetCaption(int index) => index >= 0 && index < _items.Count ? _items[index].Caption : PrintCaption.Empty;

    public SKPicture? GetPicture(int index)
    {
        if (!_state.IsVector || index < 0 || index >= _items.Count) return null;

        lock (_lock)
        {
            if (_svg is null && !IsDisposed)
            {
                try
                {
                    _svg = SvgCodec.LoadSvg(_state.Photo.FilePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌❌❌ {nameof(PrintSession)}.{nameof(GetPicture)}: {ex.Message}");
                }
            }

            return _svg?.Picture;
        }
    }

    public SKImageRef.ImageLease? TryGetImage(int index, int longSide)
    {
        if (index < 0 || index >= _items.Count || IsDisposed) return null;

        var item = _items[index];
        var bucket = GetBucket(longSide);

        lock (_lock)
        {
            var exact = Acquire((item.FrameIndex, item.Region, bucket));
            if (exact is not null) return exact;

            RequestLoad(item, bucket);

            foreach (var b in _buckets.Where(b => b > bucket).Append(FULL_SIZE).Concat(_buckets.Where(b => b < bucket).Reverse()))
            {
                var other = Acquire((item.FrameIndex, item.Region, b));
                if (other is not null) return other;
            }
        }

        return null;
    }

    public async Task<SKImageRef.ImageLease?> GetImageAsync(int index, int longSide, CancellationToken token)
    {
        if (index < 0 || index >= _items.Count || IsDisposed) return null;

        var item = _items[index];
        var bucket = GetBucket(longSide);
        var key = (item.FrameIndex, item.Region, bucket);

        lock (_lock)
        {
            var cached = Acquire(key);
            if (cached is not null) return cached;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _cancel.Token);
        await LoadAsync(item, bucket, linked.Token).ConfigureAwait(false);

        lock (_lock)
        {
            return Acquire(key);
        }
    }

    public async Task<SKImage?> BuildFullImageAsync(int frameIndex, PrintRegion region, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _cancel.Token);
        return await BuildSourceImageAsync(new PrintItem(frameIndex, region, PrintCaption.Empty), FULL_SIZE, linked.Token).ConfigureAwait(false);
    }

    private SKImageRef.ImageLease? Acquire((int, PrintRegion, int) key)
    {
        if (!_cache.TryGetValue(key, out var imgRef)) return null;

        var lease = imgRef.Acquire();
        if (lease is null) return null;

        _lru.Remove(key);
        _lru.AddFirst(key);
        return lease;
    }

    private void RequestLoad(PrintItem item, int bucket)
    {
        if (!_pending.Add((item.FrameIndex, item.Region, bucket))) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await LoadAsync(item, bucket, _cancel.Token).ConfigureAwait(false);
                if (!IsDisposed) ImageReady?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌ {nameof(PrintSession)}.{nameof(RequestLoad)}: {ex.Message}");
            }
            finally
            {
                lock (_lock) _pending.Remove((item.FrameIndex, item.Region, bucket));
            }
        });
    }

    private async Task LoadAsync(PrintItem item, int bucket, CancellationToken token)
    {
        var key = (item.FrameIndex, item.Region, bucket);

        await _decodeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key)) return;
            }

            var image = await BuildSourceImageAsync(item, bucket, token).ConfigureAwait(false);
            if (image is null) return;

            lock (_lock)
            {
                if (IsDisposed || _cache.ContainsKey(key))
                {
                    image.Dispose();
                    return;
                }

                _cache[key] = new SKImageRef(image);
                _lru.AddFirst(key);
                _cacheBytes += image.Info.BytesSize64;
                TrimCache(key);
            }
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private void TrimCache((int, PrintRegion, int) keep)
    {
        while (_cacheBytes > CACHE_BUDGET_BYTES && _lru.Last is { } node && !node.Value.Equals(keep))
        {
            _lru.RemoveLast();
            if (!_cache.Remove(node.Value, out var imgRef)) continue;

            _cacheBytes -= imgRef.Image?.Info.BytesSize64 ?? 0;

            imgRef.RequestDispose();
        }
    }

    private async Task<SKImage?> BuildSourceImageAsync(PrintItem item, int bucket, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (CanUseShownImage(item))
        {
            return await Task.Run(() => ScaleShownImage(item, bucket), token).ConfigureAwait(false);
        }

        var reader = await GetReaderAsync(token).ConfigureAwait(false);
        if (reader is null) return null;

        var box = bucket == FULL_SIZE ? 0u : (uint)bucket;
        using var frame = await reader.ReadFrameAsync(item.FrameIndex, box, token).ConfigureAwait(false);
        if (frame.IsDisposed()) return null;

        return await Task.Run(() => EditDecodedFrame(frame, reader.Metadata, item, bucket), token).ConfigureAwait(false);
    }

    private bool CanUseShownImage(PrintItem item)
    {
        if (_state.Image.IsDisposed() || _state.IsVector) return false;
        if (_state.Photo.IsClipboard) return true;

        return item.FrameIndex == _state.FrameIndex && !_state.IsPreview && _state.HasExactColorTag;
    }

    private SKImage? ScaleShownImage(PrintItem item, int bucket)
    {
        var shown = _state.Image;
        if (shown.IsDisposed()) return null;

        var rect = _state.GetRegionRect(ToViewerRegion(item.Region));
        if (rect.IsEmpty) rect = SKRectI.Create(shown.Width, shown.Height);

        return Resample(shown, rect, bucket);
    }

    private SKImage? EditDecodedFrame(SKImage frame, PhotoMetadata meta, PrintItem item, int bucket)
    {
        SKImage? toneMapped = null;
        if (_state.HdrToneMapping is { } options && meta.IsHdr)
        {
            toneMapped = HdrToneMapper.ToneMapToSdr(frame, meta.HdrTransferFn, options, meta.ContentPeakNits);
        }

        try
        {
            var source = toneMapped ?? frame;
            using var edited = SkiaCodec.ApplyImageEdits(source, _state.Channels, _state.IsColorInverted, _state.Orientation);
            var shownFrame = edited ?? source;

            var rect = SKRectI.Create(shownFrame.Width, shownFrame.Height);
            if (item.Region != PrintRegion.WholeImage && _state.Image is { } shown && !shown.IsDisposed())
            {
                var region = _state.GetRegionRect(ToViewerRegion(item.Region));
                var sx = shownFrame.Width / (float)shown.Width;
                var sy = shownFrame.Height / (float)shown.Height;
                var scaled = SKRectI.Round(new SKRect(region.Left * sx, region.Top * sy, region.Right * sx, region.Bottom * sy));
                if (scaled.Width > 0 && scaled.Height > 0) rect = scaled;
            }

            return Resample(shownFrame, rect, bucket);
        }
        finally
        {
            toneMapped?.Dispose();
        }
    }

    private static SKImage? Resample(SKImage src, SKRectI rect, int bucket)
    {
        var longSide = Math.Max(rect.Width, rect.Height);
        var scale = bucket == FULL_SIZE || longSide <= bucket ? 1f : bucket / (float)longSide;
        var w = Math.Max(1, (int)Math.Round(rect.Width * scale));
        var h = Math.Max(1, (int)Math.Round(rect.Height * scale));

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque, SKColorSpace.CreateSrgb());
        if (info.BytesSize64 > int.MaxValue) return null;

        using var surface = SKSurface.Create(info);
        if (surface is null) return null;

        surface.Canvas.Clear(SKColors.White);
        var sampling = scale < 1 ? new SKSamplingOptions(SKCubicResampler.Mitchell) : new SKSamplingOptions(SKFilterMode.Linear);
        surface.Canvas.DrawImage(src, SKRect.Create(rect.Left, rect.Top, rect.Width, rect.Height), SKRect.Create(w, h), sampling);

        return surface.Snapshot();
    }

    private Task<PhotoFrameReader?> GetReaderAsync(CancellationToken token)
    {
        lock (_lock)
        {
            _readerTask ??= OpenReaderAsync();
            return _readerTask.WaitAsync(token);
        }
    }

    private async Task<PhotoFrameReader?> OpenReaderAsync()
    {
        var path = _state.Photo.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        try
        {
            var options = PhotoReadOptions.FromConfig() with { SkipDisplayProfile = true };
            return await PhotoFrameReader.OpenAsync(path, options, _cancel.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(PrintSession)}.{nameof(OpenReaderAsync)}: {ex.Message}");
            return null;
        }
    }

    private static int GetBucket(int longSide)
    {
        if (longSide <= 0) return _buckets[0];

        foreach (var b in _buckets)
        {
            if (longSide <= b) return b;
        }

        return FULL_SIZE;
    }

    private static ViewerImageRegion ToViewerRegion(PrintRegion region) => region switch
    {
        PrintRegion.Selection => ViewerImageRegion.Selection,
        PrintRegion.VisibleArea => ViewerImageRegion.VisibleArea,
        _ => ViewerImageRegion.WholeImage,
    };

    private string GetPhotoName()
    {
        var path = _state.Photo.FilePath;
        return string.IsNullOrEmpty(path) ? "Photo Viewer" : Path.GetFileName(path);
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        _cancel.Cancel();

        lock (_lock)
        {
            foreach (var imgRef in _cache.Values) imgRef.RequestDispose();
            _cache.Clear();
            _lru.Clear();
            _cacheBytes = 0;

            _svg?.Dispose();
            _svg = null;
        }

        _ = _readerTask?.ContinueWith(t => t.Result?.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
        _state.Dispose();
    }
}
