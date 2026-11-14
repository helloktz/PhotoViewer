using Avalonia.Threading;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using ImageGlass.SDK.Plugins;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PhotoViewer.Plugins;

internal sealed unsafe class NativePluginAnimator : AnimatorImpl
{
    private const int MAX_CACHE_COUNT = 5;

    private readonly NativeCodecProxy _proxy;

    private volatile string _filePath;
    private readonly SKColorSpace? _colorSpace;

    private readonly SKImage?[] _frameCache;
    private readonly Queue<uint> _cachedFramesQueue = new();
    private readonly Lock _syncLock = new();

    private DispatcherTimer _timer;

    public static NativePluginAnimator Create(NativeCodecProxy proxy,
        PhotoMetadata metadata, CancellationToken token)
    {
        var codecApi = proxy.CodecApi;
        if (!PluginAbi.HasEntryPoint(codecApi, &codecApi->GetAnimationInfo)
            || !PluginAbi.HasEntryPoint(codecApi, &codecApi->FreeAnimationInfo)
            || !PluginAbi.HasEntryPoint(codecApi, &codecApi->DecodeAnimationFrame))
        {
            throw new NotSupportedException(
                $"IGE: Native codec '{proxy.CodecId}' is missing one of the animation entry points.");
        }

        var cancelHandle = PluginHostApiTable.RegisterCancellation(token);
        IGAnimationInfo info = default;
        var infoOwned = false;
        try
        {
            IGStatus status;
            try
            {
                fixed (char* pPath = metadata.FilePath)
                {
                    var pathRef = new IGStringRef { Data = pPath, Length = metadata.FilePath.Length };
                    status = codecApi->GetAnimationInfo(pathRef, &info, (void*)cancelHandle);
                }
            }
            catch (Exception ex)
            {
                proxy.FailureManager.RecordSoftFailure(proxy.Plugin.PluginId,
                    $"managed exception during GetAnimationInfo: {ex.Message}");
                throw new InvalidDataException(
                    $"IGE: Native codec '{proxy.CodecId}' threw during GetAnimationInfo of '{metadata.FilePath}'.", ex);
            }

            if (status == IGStatus.Canceled)
            {
                token.ThrowIfCancellationRequested();
            }
            if (status != IGStatus.OK)
            {
                throw new InvalidDataException(
                    $"IGE: Native codec '{proxy.CodecId}' returned status {status} for GetAnimationInfo of '{metadata.FilePath}'.");
            }
            infoOwned = true;

            if (info.FrameCount <= 0 || info.Frames == null)
            {
                throw new InvalidDataException(
                    $"IGE: Native codec '{proxy.CodecId}' reported zero frames for '{metadata.FilePath}'.");
            }

            var frames = new SKCodecFrameInfo[info.FrameCount];
            for (var i = 0; i < info.FrameCount; i++)
            {
                var f = info.Frames[i];
                frames[i] = new SKCodecFrameInfo
                {
                    Duration = f.DurationMs,
                    AlphaType = f.HasAlpha != 0 ? SKAlphaType.Unpremul : SKAlphaType.Opaque,
                };
            }

            var animator = new NativePluginAnimator(proxy, metadata, frames)
            {
                _loopCount = Math.Max(0, info.LoopCount),
            };

            metadata.AnimationLoop = (uint)Math.Max(0, info.LoopCount);

            return animator;
        }
        finally
        {
            if (infoOwned)
            {
                try
                {
                    codecApi->FreeAnimationInfo(&info);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NativePluginAnimator] FreeAnimationInfo threw: {ex.Message}");
                }
            }
            PluginHostApiTable.ReleaseCancellation(cancelHandle);
        }
    }

    private NativePluginAnimator(NativeCodecProxy proxy, PhotoMetadata metadata,
        SKCodecFrameInfo[] frames) : base(frames)
    {
        _proxy = proxy;
        _filePath = metadata.FilePath;
        _colorSpace = metadata.SkiaColorSpace;
        _frameCache = new SKImage[frames.Length];

        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.UIThread)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _timer.Tick += Timer_Tick;
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        lock (_syncLock)
        {
            StopTimer();
            _timer.Tick -= Timer_Tick;

            for (var i = 0; i < _frameCache.Length; i++)
            {
                _frameCache[i]?.Dispose();
                _frameCache[i] = null;
            }
            _cachedFramesQueue.Clear();
        }
    }

    protected override void StartTimer() => _timer.Start();

    protected override void StopTimer() => _timer.Stop();

    private void Timer_Tick(object? sender, EventArgs e) => OnTimerTicked();

    public override void UpdateFilePath(string filePath) => _filePath = filePath;

    public override SKImage? GetRenderedFrameBitmap(uint frameIndex)
    {
        lock (_syncLock)
        {
            if (IsDisposed) return null;
            if (frameIndex >= _frames.Length) return null;

            var cached = _frameCache[frameIndex];
            if (!cached.IsDisposed()) return cached;
            _frameCache[frameIndex] = null;

            var result = DecodePluginFrame((int)frameIndex);
            if (result is null) return null;

            _frameCache[frameIndex] = result;
            if (!_cachedFramesQueue.Contains(frameIndex))
            {
                _cachedFramesQueue.Enqueue(frameIndex);
            }
            while (_cachedFramesQueue.Count > MAX_CACHE_COUNT)
            {
                var evicted = _cachedFramesQueue.Dequeue();
                if (evicted != frameIndex)
                {
                    _frameCache[evicted]?.Dispose();
                    _frameCache[evicted] = null;
                }
            }

            return result;
        }
    }

    private SKImage? DecodePluginFrame(int frameIndex)
    {
        var codecApi = _proxy.CodecApi;
        IGPixelBuffer buffer = default;
        var bufferOwned = false;
        var ownershipTransferred = false;

        var filePath = _filePath;

        try
        {
            IGStatus status;
            try
            {
                fixed (char* pPath = filePath)
                {
                    var pathRef = new IGStringRef { Data = pPath, Length = filePath.Length };
                    status = codecApi->DecodeAnimationFrame(pathRef, frameIndex, &buffer, null);
                }
            }
            catch (Exception ex)
            {
                _proxy.FailureManager.RecordSoftFailure(_proxy.Plugin.PluginId,
                    $"managed exception during DecodeAnimationFrame: {ex.Message}");
                return null;
            }

            if (status != IGStatus.OK) return null;
            bufferOwned = true;

            var frameHasAlpha = _frames[frameIndex].AlphaType != SKAlphaType.Opaque;
            var image = _proxy.WrapPluginBufferAsImage(in buffer, _colorSpace, frameHasAlpha);
            ownershipTransferred = true;
            return image;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NativePluginAnimator] DecodePluginFrame[{frameIndex}] failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (bufferOwned && !ownershipTransferred)
            {
                try
                {
                    var localBuf = buffer;
                    codecApi->FreePixelBuffer(&localBuf);
                }
                catch { }
            }
        }
    }
}
