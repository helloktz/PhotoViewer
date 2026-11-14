using Avalonia.Controls;
using PhotoViewer.Common.Extensions;
using PhotoViewer.UI.Viewer.Transitions;
using SkiaSharp;
using System;
using System.Threading;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{
    private const int MAX_TRANSITION_HOLD_MS = 3000;

    internal ViewerTransition? _transition;

    private Lock TransitionLock => MirrorSource?._lock ?? _lock;

    public void CompleteTransition()
    {
        CompleteTransition(TransitionLock);
    }

    private void CompleteTransition(Lock transitionLock)
    {
        ViewerTransition? transition;
        lock (transitionLock)
        {
            transition = _transition;
            _transition = null;
        }

        if (transition is null) return;

        transition.Dispose();
        InvalidateVisual();
    }

    private void BeginTransition(TransitionRequest? request)
    {
        TransitionStarting?.Invoke(this, request);

        SKPicture? heldFrame = null;
        lock (TransitionLock)
        {
            if (request is not null && _transition is { StartTime: null } holding)
            {
                heldFrame = holding.TakeFromFrame();
            }
        }

        CompleteTransition();
        if (request is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        var canPlay = topLevel is not null && IsEffectivelyVisible;
        var fromFrame = canPlay
            ? heldFrame ?? CaptureTransitionFrame()
            : null;

        if (!canPlay) heldFrame?.Dispose();

        if (fromFrame is null)
        {
            request.Complete();
            return;
        }

        var transition = new ViewerTransition(request, fromFrame);
        lock (TransitionLock)
        {
            _transition = transition;
        }

        topLevel!.RequestAnimationFrame(ts => OnTransitionFrame(ts, transition));
    }

    private SKPicture? CaptureTransitionFrame()
    {
        if (MirrorSource is { } source) return CaptureMirrorFrame(source);

        lock (_lock)
        {
            var hasImage = _imgRender is not null || _imgSource is not null;
            var hasVector = _svgPicture is not null && !_svgPicture.IsDisposed();
            if (!hasImage && !hasVector) return null;
            if (DrawingArea.Width < 1 || DrawingArea.Height < 1) return null;

            using var renderer = new PhotoRenderer(this, null);
            return renderer.RecordFrame();
        }
    }

    private void OnTransitionFrame(TimeSpan ts, ViewerTransition transition)
    {
        bool hasTarget;
        lock (TransitionLock)
        {
            if (!ReferenceEquals(_transition, transition)) return;

            hasTarget = HasTransitionTarget();
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            CompleteTransition();
            return;
        }

        if (transition.StartTime is null)
        {
            var heldMs = Environment.TickCount64 - transition.HoldStartTick;
            if (!hasTarget && heldMs < MAX_TRANSITION_HOLD_MS)
            {
                topLevel.RequestAnimationFrame(t => OnTransitionFrame(t, transition));
                return;
            }

            transition.StartTime = ts;
        }

        var elapsedMs = (ts - transition.StartTime.Value).TotalMilliseconds;
        var linear = Math.Clamp(elapsedMs / transition.Request.DurationMs, 0, 1);

        lock (TransitionLock)
        {
            transition.Progress = (float)EaseInOutCubic(linear);
        }

        if (linear >= 1)
        {
            CompleteTransition();
            return;
        }

        InvalidateVisual();
        topLevel.RequestAnimationFrame(t => OnTransitionFrame(t, transition));
    }

    private bool HasTransitionTarget()
    {
        if (MirrorSource is { } source) return source.HasTransitionTarget();

        if (Photo is null || Photo.Error is not null) return true;

        return _imgSource is not null || _svgPicture is not null;
    }

    private static double EaseInOutCubic(double t)
    {
        return t < 0.5
            ? 4 * t * t * t
            : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

}
