using Avalonia.Controls;
using PhotoViewer.Common;
using System;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{

    public bool EnableDrawingAnimation { get; set; } = false;

    public AnimationSources AnimationSource { get; protected set; } = AnimationSources.None;

    protected virtual void OnPrepareAnimationFrame(TimeSpan ts)
    {
        if (!EnableDrawingAnimation) return;

        CalculateFps();

        if (_lastAnimationFrameTime != TimeSpan.Zero)
        {
            var elapsedSinceUpdate = (ts - _lastAnimationFrameTime).TotalSeconds;

            OnDrawAnimationSource(elapsedSinceUpdate);
            InvalidateVisual();
        }
        _lastAnimationFrameTime = ts;

        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnPrepareAnimationFrame);
    }

    public void StartDrawingAnimation(AnimationSources sources, int durationMs = int.MaxValue, Action? callbackFn = null)
    {
        if (durationMs <= 0) return;

        AnimationSource = sources;
        EnableDrawingAnimation = true;
        _lastAnimationFrameTime = TimeSpan.Zero;

        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnPrepareAnimationFrame);

        if (durationMs < int.MaxValue)
        {
            BHelper.Debounce(durationMs, OnAnimationStopped,
                new AnimationSourceArgs(sources, callbackFn), true);
        }
    }

    private void OnAnimationStopped(AnimationSourceArgs? e)
    {
        if (e is null) return;
        StopDrawingAnimation(e.Source, e.CallbackFn);
    }

    public void StopDrawingAnimation(AnimationSources sources, Action? callbackFn = null)
    {
        AnimationSource ^= sources;
        EnableDrawingAnimation = false;
        InvalidateVisual();

        callbackFn?.Invoke();
    }

}
