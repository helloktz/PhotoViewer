using Avalonia.Threading;
using PhotoViewer.Common.Types;
using SkiaSharp;
using System;
using System.Diagnostics;

namespace PhotoViewer.Common.Photoing;

public abstract class AnimatorImpl : PhDisposable
{
    protected SKCodecFrameInfo[] _frames;
    protected int _frameCount = 0;
    protected int _loopCount = 0;
    protected int _currentFrame = 0;
    protected int _currentLoop = 0;

    protected bool _isDecoded = false;
    protected bool _isPaused = true;
    protected Stopwatch _stopwatch = new();
    protected TimeSpan _lastFrameTime = TimeSpan.Zero;
    protected TimeSpan _pauseStartTime = TimeSpan.Zero;
    private bool _needsInitialFrame = true;

    private static readonly TimeSpan MAX_CATCH_UP = TimeSpan.FromSeconds(1);

    protected const int MAX_FRAMES_PER_TICK = 4;

    private static readonly TimeSpan MAX_BACKLOG = TimeSpan.FromMilliseconds(100);

    protected virtual int MaxFramesPerTick => MAX_FRAMES_PER_TICK;

    public event TEventHandler<AnimatorImpl, AnimatorFrameChangedEventArgs>? FrameChanged;

    public event TEventHandler<AnimatorImpl, EventArgs>? Stopped;

    public SKCodecFrameInfo[] Frames => _frames;

    public uint CurrentFrame => (uint)_currentFrame;

    public uint CurrentLoop => (uint)_currentLoop;

    public uint LoopCount => (uint)_loopCount;

    public bool IsPlaying => !_isPaused;

    public AnimatorImpl(SKCodecFrameInfo[] frames)
    {
        _frames = frames;
        _frameCount = frames.Length;
    }

    protected abstract void StartTimer();

    protected abstract void StopTimer();

    public abstract SKImage? GetRenderedFrameBitmap(uint frameIndex);

    public void Restart()
    {
        Pause();

        ResetTimer();
        _stopwatch.Restart();

        Play();
    }

    public void Play()
    {
        if (!_isDecoded)
        {
            _isDecoded = true;

            _stopwatch.Restart();
            _lastFrameTime = TimeSpan.Zero;
        }

        if (_loopCount > 0 && _currentLoop >= _loopCount)
        {
            _currentFrame = 0;
            _currentLoop = 0;
            _needsInitialFrame = true;
        }

        if (_isPaused)
        {
            var pausedDuration = _stopwatch.Elapsed - _pauseStartTime;
            _lastFrameTime += pausedDuration;
        }

        _isPaused = false;
        StartTimer();

        if (_needsInitialFrame)
        {
            _needsInitialFrame = false;
            _lastFrameTime = _stopwatch.Elapsed;

            RaiseFrameChanged();
        }
    }

    public void Pause()
    {
        if (!_isDecoded || _isPaused) return;

        StopTimer();
        _isPaused = true;
        _pauseStartTime = _stopwatch.Elapsed;
    }

    public void SeekToFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _frameCount) return;

        _currentFrame = frameIndex;
        _needsInitialFrame = false;

        _lastFrameTime = _stopwatch.Elapsed;

        RaiseFrameChanged();
    }

    public virtual void UpdateFilePath(string filePath) { }

    protected override void OnDisposing()
    {
        StopTimer();

        _currentFrame = 0;
        _frameCount = 0;
        _loopCount = 0;
        _currentLoop = 0;

        foreach (var eventDelegate in FrameChanged?.GetInvocationList() ?? [])
        {
            FrameChanged -= (TEventHandler<AnimatorImpl, AnimatorFrameChangedEventArgs>)eventDelegate;
        }

        foreach (var eventDelegate in Stopped?.GetInvocationList() ?? [])
        {
            Stopped -= (TEventHandler<AnimatorImpl, EventArgs>)eventDelegate;
        }

        base.OnDisposing();
    }

    protected void ResetTimer()
    {
        _stopwatch.Reset();

        _lastFrameTime = TimeSpan.Zero;
        _pauseStartTime = TimeSpan.Zero;

        _currentFrame = 0;
        _currentLoop = 0;
        _needsInitialFrame = true;
    }

    protected virtual void OnTimerTicked()
    {
        if (_isPaused || _frameCount == 0) return;

        var now = _stopwatch.Elapsed;
        var maxFrames = Math.Max(1, MaxFramesPerTick);
        var advanced = 0;
        var hasFinishedLastLoop = false;

        while (advanced < maxFrames)
        {
            var frameDelay = GetFrameDelay(_currentFrame);

            if (now - _lastFrameTime < frameDelay) break;

            if (advanced > 0 && now - _lastFrameTime < MAX_BACKLOG) break;

            _lastFrameTime += frameDelay;
            _currentFrame++;
            advanced++;

            if (_currentFrame >= _frameCount)
            {
                _currentFrame = 0;
                _currentLoop++;

                if (_loopCount > 0 && _currentLoop >= _loopCount)
                {
                    _currentFrame = _frameCount - 1;
                    hasFinishedLastLoop = true;
                    break;
                }
            }
        }

        if (advanced == 0) return;

        if (now - _lastFrameTime > MAX_CATCH_UP)
        {
            _lastFrameTime = now;
        }

        RaiseFrameChanged();

        if (hasFinishedLastLoop)
        {
            Pause();

            OnStopped(EventArgs.Empty);
        }
    }

    protected virtual TimeSpan GetFrameDelay(int frameIndex)
    {
        var frameMeta = _frames[frameIndex];
        var delayMs = frameMeta.Duration;

        if (delayMs <= 10)
        {
            delayMs = 100;
        }

        return TimeSpan.FromMilliseconds(delayMs);
    }

    private void RaiseFrameChanged()
    {
        OnFrameChanged(new AnimatorFrameChangedEventArgs()
        {
            CurrentFrame = (uint)_currentFrame,
            CurrentLoop = (uint)_currentLoop,
            FrameCount = (uint)_frameCount,
            LoopCount = (uint)_loopCount,
        });
    }

    protected virtual void OnFrameChanged(AnimatorFrameChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            FrameChanged?.Invoke(this, e);
        });
    }

    protected virtual void OnStopped(EventArgs e)
    {
        Stopped?.Invoke(this, e);
    }

}
