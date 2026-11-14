using Avalonia.Threading;
using PhotoViewer.Common.Extensions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PhotoViewer.Common.Photoing;

public class SkiaAnimator : AnimatorImpl
{
    private const int MAX_CACHE_COUNT = 5;

    private const int NO_FRAME = -1;

    private const int CATCH_UP_BUDGET_US = 8_000;

    private const int MIN_POLL_INTERVAL_MS = 2;
    private const int MAX_POLL_INTERVAL_MS = 16;

    private readonly SKCodec _codec;
    private readonly SKImage?[] _frameCache;
    private readonly Queue<uint> _cachedFramesQueue = new();
    private readonly Lock _syncLock = new();

    private SKBitmap? _compositeBitmap;

    private int _lastRenderedFrameIndex = -1;

    private int _chainedDecodeUs;
    private DispatcherTimer _timer;

    public SkiaAnimator(SKCodec? srcCodec, SKCodecFrameInfo[] frames) : base(frames)
    {
        _codec = srcCodec ?? throw new ArgumentNullException(nameof(srcCodec));
        _frameCache = new SKImage[frames.Length];

        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.UIThread);
        _timer.Interval = TimeSpan.FromMilliseconds(GetPollIntervalMs(frames));
        _timer.Tick += Timer_Tick;
    }

    private static int GetPollIntervalMs(SKCodecFrameInfo[] frames)
    {
        var shortestDelayMs = int.MaxValue;
        foreach (var frame in frames)
        {
            if (frame.Duration > 0 && frame.Duration < shortestDelayMs) shortestDelayMs = frame.Duration;
        }

        if (shortestDelayMs == int.MaxValue) return MAX_POLL_INTERVAL_MS;

        return Math.Clamp(shortestDelayMs / 4, MIN_POLL_INTERVAL_MS, MAX_POLL_INTERVAL_MS);
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        lock (_syncLock)
        {
            StopTimer();
            _timer.Tick -= Timer_Tick;

            _compositeBitmap?.Dispose();
            _compositeBitmap = null;

            for (int i = 0; i < _frameCache.Length; i++)
            {
                _frameCache[i]?.Dispose();
                _frameCache[i] = null;
            }
            _cachedFramesQueue.Clear();

            _codec.Dispose();
        }
    }

    protected override int MaxFramesPerTick
    {
        get
        {
            if (_chainedDecodeUs <= 0) return MAX_FRAMES_PER_TICK;

            return Math.Clamp(CATCH_UP_BUDGET_US / _chainedDecodeUs, 1, MAX_FRAMES_PER_TICK);
        }
    }

    protected override void StartTimer()
    {
        _timer.Start();
    }

    protected override void StopTimer()
    {
        _timer.Stop();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        OnTimerTicked();
    }

    public override SKImage? GetRenderedFrameBitmap(uint frameIndex)
    {
        lock (_syncLock)
        {
            if (IsDisposed || _codec.IsDisposed()) return null;

            var cached = _frameCache[frameIndex];
            if (!cached.IsDisposed()) return cached;

            _frameCache[frameIndex] = null;

            if (_compositeBitmap is null)
            {
                var info = _codec.Info;
                var compositeType = SkiaCodec.IsHighBitDepthColorType(info.ColorType)
                    ? info.ColorType
                    : SKColorType.Bgra8888;
                _compositeBitmap = new SKBitmap(info.Width, info.Height, compositeType, SKAlphaType.Premul);
                _lastRenderedFrameIndex = -1;
            }

            ComposeUpTo((int)frameIndex);

            var frameImage = SKImage.FromBitmap(_compositeBitmap);
            _frameCache[frameIndex] = frameImage;

            if (!_cachedFramesQueue.Contains(frameIndex))
            {
                _cachedFramesQueue.Enqueue(frameIndex);
            }

            while (_cachedFramesQueue.Count > MAX_CACHE_COUNT)
            {
                var evictedIndex = _cachedFramesQueue.Dequeue();

                if (evictedIndex != frameIndex)
                {
                    _frameCache[evictedIndex]?.Dispose();
                    _frameCache[evictedIndex] = null;
                }
            }

            return frameImage;
        }
    }

    private void ComposeUpTo(int frameIndex)
    {
        var from = _lastRenderedFrameIndex + 1;

        if (frameIndex > from && CanChainRange(from, frameIndex))
        {
            for (var i = from; i < frameIndex; i++)
            {
                RenderFrame(i);

                if (_lastRenderedFrameIndex != i) break;
            }
        }

        RenderFrame(frameIndex);
    }

    private bool CanChainRange(int from, int to)
    {
        for (var i = from; i <= to; i++)
        {
            if (!_codec.GetFrameInfo(i, out var meta)) return false;
            if (meta.RequiredFrame < 0 || meta.RequiredFrame > i - 1) return false;
            if (!HasNoRestorePrevious(meta.RequiredFrame, i - 1)) return false;
        }

        return true;
    }

    private bool HasNoRestorePrevious(int from, int to)
    {
        for (var i = from; i <= to; i++)
        {
            if (!_codec.GetFrameInfo(i, out var meta)) return false;
            if (meta.DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious) return false;
        }

        return true;
    }

    private void RenderFrame(int frameIndex)
    {
        if (_compositeBitmap is null) return;
        if (frameIndex < 0 || frameIndex >= _frames.Length) return;

        var info = _compositeBitmap.Info;

        var requiredFrame = _codec.GetFrameInfo(frameIndex, out var frameMeta)
            ? frameMeta.RequiredFrame
            : NO_FRAME;

        var canChainFromPrevious = frameIndex > 0
            && _lastRenderedFrameIndex == frameIndex - 1
            && requiredFrame >= 0
            && requiredFrame <= frameIndex - 1
            && HasNoRestorePrevious(requiredFrame, frameIndex - 1);

        var options = canChainFromPrevious
            ? new SKCodecOptions(frameIndex, frameIndex - 1)
            : new SKCodecOptions(frameIndex);

        if (!canChainFromPrevious) _compositeBitmap.Erase(SKColors.Transparent);

        var frameInfo = new SKImageInfo(info.Width, info.Height, info.ColorType, info.AlphaType);
        var startedAt = Stopwatch.GetTimestamp();
        var result = _codec.GetPixels(frameInfo, _compositeBitmap.GetPixels(), _compositeBitmap.RowBytes, options);

        _lastRenderedFrameIndex = result == SKCodecResult.Success ? frameIndex : NO_FRAME;

        if (canChainFromPrevious && result == SKCodecResult.Success)
        {
            var costUs = (int)(Stopwatch.GetElapsedTime(startedAt).Ticks / TimeSpan.TicksPerMicrosecond);
            _chainedDecodeUs = _chainedDecodeUs <= 0 ? costUs : ((_chainedDecodeUs * 3) + costUs) / 4;
        }
    }

}
