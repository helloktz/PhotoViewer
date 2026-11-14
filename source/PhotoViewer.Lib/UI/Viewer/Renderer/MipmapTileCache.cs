using Avalonia.Threading;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.UI.Viewer;

internal sealed class MipmapTileCache : PhDisposable
{
    public const int TILE_SIZE = 512;
    private const int MAX_CACHED_TILES = 100;
    private const int MAX_MIP_LEVEL = 6;
    private const int PROXY_MAX_DIMENSION = 4096;

    private const long MIN_PIXELS_FOR_TILING = 8192 * 8192;

    private readonly Lock _lock = new();

    private readonly Dictionary<(int x, int y, int level), SKImageRef> _tiles = [];

    private readonly Dictionary<(int x, int y, int level), LinkedListNode<(int x, int y, int level)>> _nodeMap = [];

    private readonly LinkedList<(int x, int y, int level)> _lruList = new();

    private readonly Stack<(int x, int y, int level)> _tileQueue = new();
    private readonly HashSet<(int x, int y, int level)> _pendingTiles = [];

    private readonly SKImageRef _sourceRef;

    private readonly SKColorType _colorType;

    private readonly SKColorSpace? _colorSpace;

    private readonly Action _tileReady;

    private readonly int _maxTilesPerViewer;

    private int _maxCachedTiles;
    private int _viewerCount = 1;

    private SKImageRef? _proxy;
    private bool _workerRunning;
    private bool _isStopping;

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public SKImageRef SourceRef => _sourceRef;

    private MipmapTileCache(SKImageRef sourceRef, int width, int height,
        SKColorType colorType, SKColorSpace? colorSpace, Action tileReady)
    {
        _sourceRef = sourceRef;
        _sourceRef.KeepAlive();
        SourceWidth = width;
        SourceHeight = height;
        _colorType = colorType;
        _colorSpace = colorSpace;
        _tileReady = tileReady;

        var bpp = new SKImageInfo(1, 1, colorType).BytesPerPixel;
        _maxTilesPerViewer = bpp <= 4
            ? MAX_CACHED_TILES
            : Math.Max(10, MAX_CACHED_TILES * 4 / bpp);
        _maxCachedTiles = _maxTilesPerViewer;
    }

    public static MipmapTileCache? Create(SKImageRef? sourceRef, Action tileReady)
    {
        var img = sourceRef?.Image;
        var needsTiles = NeedsTiles(img);
        if (!needsTiles) return null;

        var cache = new MipmapTileCache(sourceRef!, img!.Width, img.Height,
            img.ColorType, img.ColorSpace, tileReady);
        _ = Task.Run(cache.GenerateProxy);
        return cache;
    }

    public static bool NeedsTiles(SKImage? img)
    {
        if (img is null || img.IsDisposed()) return false;

        var pixels = (long)img.Width * img.Height;
        return pixels >= MIN_PIXELS_FOR_TILING;
    }

    public static int GetMipLevel(double zoomFactor)
    {
        if (zoomFactor >= 1.0) return 0;
        var level = (int)Math.Log2(1.0 / zoomFactor);
        return Math.Clamp(level, 0, MAX_MIP_LEVEL);
    }

    public static int GetSourceTileSize(int mipLevel)
    {
        return TILE_SIZE << mipLevel;
    }

    public void AddViewer()
    {
        lock (_lock)
        {
            _viewerCount++;
            _maxCachedTiles = _maxTilesPerViewer * _viewerCount;
        }
    }

    public void RemoveViewer()
    {
        lock (_lock)
        {
            _viewerCount = Math.Max(1, _viewerCount - 1);
            _maxCachedTiles = _maxTilesPerViewer * _viewerCount;
        }
    }

    public SKImageRef.ImageLease? AcquireProxy()
    {
        if (IsDisposed) return null;

        lock (_lock)
        {
            return _isStopping ? null : _proxy?.Acquire();
        }
    }

    public SKImageRef.ImageLease? GetOrQueueTile(int tileX, int tileY, int mipLevel)
    {
        if (IsDisposed) return null;

        var key = (tileX, tileY, mipLevel);
        var startWorker = false;

        lock (_lock)
        {
            if (_isStopping) return null;

            if (_tiles.TryGetValue(key, out var cached))
            {
                if (_nodeMap.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _lruList.AddLast(node);
                }
                return cached.Acquire();
            }

            if (_pendingTiles.Add(key))
            {
                _tileQueue.Push(key);
            }

            startWorker = !_workerRunning;
            _workerRunning = true;
        }

        if (startWorker) _ = Task.Run(ProcessPendingTiles);
        return null;
    }

    private void ProcessPendingTiles()
    {
        while (true)
        {
            (int x, int y, int level) key;

            lock (_lock)
            {
                if (_isStopping || _tileQueue.Count == 0)
                {
                    _workerRunning = false;
                    return;
                }

                key = _tileQueue.Pop();
            }

            SKImage? tile = null;
            try
            {
                tile = ExtractTile(key.x, key.y, key.level);
            }
            catch { }

            lock (_lock)
            {
                _pendingTiles.Remove(key);

                if (_isStopping || tile is null)
                {
                    tile?.Dispose();
                    continue;
                }

                _tiles[key] = new SKImageRef(tile);
                _nodeMap[key] = _lruList.AddLast(key);

                while (_tiles.Count > _maxCachedTiles && _lruList.First is not null)
                {
                    var oldest = _lruList.First.Value;
                    _lruList.RemoveFirst();
                    _nodeMap.Remove(oldest);

                    if (_tiles.Remove(oldest, out var image))
                    {
                        image.RequestDispose();
                    }
                }
            }

            RequestRedraw();
        }
    }

    private void GenerateProxy()
    {
        if (IsDisposed) return;

        var scale = (double)PROXY_MAX_DIMENSION / Math.Max(SourceWidth, SourceHeight);
        SKImage? proxy = null;

        try
        {
            proxy = ExtractFromSource(0, 0, SourceWidth, SourceHeight,
                Math.Max(1, (int)Math.Round(SourceWidth * scale)),
                Math.Max(1, (int)Math.Round(SourceHeight * scale)));
        }
        catch { }

        lock (_lock)
        {
            if (_isStopping || proxy is null)
            {
                proxy?.Dispose();
                return;
            }

            _proxy = new SKImageRef(proxy);
        }

        RequestRedraw();
    }

    private void RequestRedraw()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsDisposed) _tileReady();
        }, DispatcherPriority.Render);
    }

    private SKImage? ExtractTile(int tileX, int tileY, int mipLevel)
    {
        if (IsDisposed) return null;

        var sourceTileSize = GetSourceTileSize(mipLevel);

        var srcX = tileX * sourceTileSize;
        var srcY = tileY * sourceTileSize;
        var srcW = Math.Min(sourceTileSize, SourceWidth - srcX);
        var srcH = Math.Min(sourceTileSize, SourceHeight - srcY);

        if (srcX >= SourceWidth || srcY >= SourceHeight || srcW <= 0 || srcH <= 0)
            return null;

        var tileW = Math.Max(1, srcW * TILE_SIZE / sourceTileSize);
        var tileH = Math.Max(1, srcH * TILE_SIZE / sourceTileSize);

        return ExtractFromSource(srcX, srcY, srcW, srcH, tileW, tileH);
    }

    private SKImage? ExtractFromSource(int srcX, int srcY, int srcW, int srcH, int tileW, int tileH)
    {
        using var lease = _sourceRef.Acquire();
        var srcImage = lease?.Image;
        if (srcImage is null || srcImage.IsDisposed()) return null;

        var info = new SKImageInfo(tileW, tileH, _colorType, SKAlphaType.Premul, _colorSpace);
        using var bitmap = new SKBitmap(info);

        using (var canvas = new SKCanvas(bitmap))
        {
            var sampling = SkiaCodec.ToSamplingOptions(ImageInterpolation.CubicMitchell);

            canvas.DrawImage(srcImage,
                new SKRect(srcX, srcY, srcX + srcW, srcY + srcH),
                new SKRect(0, 0, tileW, tileH), sampling);
        }

        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        lock (_lock)
        {
            _isStopping = true;

            _proxy?.RequestDispose();
            foreach (var tile in _tiles.Values) tile.RequestDispose();
            _tiles.Clear();
            _lruList.Clear();
            _nodeMap.Clear();
            _pendingTiles.Clear();
            _tileQueue.Clear();
        }

        _sourceRef.RequestDispose();
    }

}
