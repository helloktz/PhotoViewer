using Avalonia.Threading;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Threading;

namespace PhotoViewer.Common.Photoing;

public sealed class SvgAnimator : AnimatorImpl
{
    private readonly SKSvg _svgDocument;
    private readonly Lock _viewerLock;
    private readonly Action<SKPicture?> _onPictureChanged;
    private readonly Action _invalidateVisual;
    private readonly Lock _syncLock = new();
    private DispatcherTimer _timer;

    private SKPicture? _lastKnownPicture;

    private bool _hasNotifiedFirstFrame;

    public SvgAnimator(
        SKSvg svgDocument,
        Lock viewerLock,
        Action<SKPicture?> onPictureChanged,
        Action invalidateVisual)
        : base([new SKCodecFrameInfo()])
    {
        _svgDocument = svgDocument;
        _viewerLock = viewerLock;
        _onPictureChanged = onPictureChanged;
        _invalidateVisual = invalidateVisual;
        _loopCount = 0;
        _lastKnownPicture = svgDocument.Picture;

        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.UIThread);
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += Timer_Tick;
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        lock (_syncLock)
        {
            StopTimer();
            _timer.Tick -= Timer_Tick;
        }

        _lastKnownPicture = null;
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

    protected override void OnTimerTicked()
    {
        if (_isPaused || IsDisposed) return;

        var now = _stopwatch.Elapsed;
        var delta = now - _lastFrameTime;
        _lastFrameTime = now;

        bool changed;

        lock (_viewerLock)
        {
            _svgDocument.AdvanceAnimation(delta);

            if (_svgDocument.HasPendingAnimationFrame)
            {
                _svgDocument.FlushPendingAnimationFrame();
            }

            var newPicture = _svgDocument.Picture;
            changed = !ReferenceEquals(_lastKnownPicture, newPicture);

            if (changed)
            {
                _lastKnownPicture = newPicture;
                _onPictureChanged(newPicture);
            }
        }

        if (changed)
        {
            _invalidateVisual();

            if (!_hasNotifiedFirstFrame)
            {
                _hasNotifiedFirstFrame = true;
                OnFrameChanged(new AnimatorFrameChangedEventArgs
                {
                    CurrentFrame = 0,
                    CurrentLoop = (uint)_currentLoop,
                    FrameCount = 1,
                    LoopCount = (uint)_loopCount,
                });
            }
        }
    }

    public override SKImage? GetRenderedFrameBitmap(uint frameIndex)
    {
        lock (_viewerLock)
        {
            var picture = _svgDocument.Picture;
            if (picture is null) return null;

            return SvgCodec.RasterizeThumbnail(picture, 1024);
        }
    }

    public void SeekToTime(TimeSpan time)
    {
        lock (_viewerLock)
        {
            _svgDocument.SetAnimationTime(time);

            if (_svgDocument.HasPendingAnimationFrame)
            {
                _svgDocument.FlushPendingAnimationFrame();
            }

            var newPicture = _svgDocument.Picture;
            _lastKnownPicture = newPicture;
            _onPictureChanged(newPicture);
        }

        _lastFrameTime = _stopwatch.Elapsed;

        _invalidateVisual();

        OnFrameChanged(new AnimatorFrameChangedEventArgs
        {
            CurrentFrame = 0,
            CurrentLoop = (uint)_currentLoop,
            FrameCount = 1,
            LoopCount = (uint)_loopCount,
        });
    }

    public void ResetAnimation()
    {
        lock (_viewerLock)
        {
            _svgDocument.ResetAnimation();

            if (_svgDocument.HasPendingAnimationFrame)
            {
                _svgDocument.FlushPendingAnimationFrame();
            }

            var newPicture = _svgDocument.Picture;
            _lastKnownPicture = newPicture;
            _onPictureChanged(newPicture);
        }

        ResetTimer();
        _invalidateVisual();
    }
}
