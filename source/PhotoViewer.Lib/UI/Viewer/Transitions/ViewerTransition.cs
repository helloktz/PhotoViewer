using PhotoViewer.Common.Types;
using SkiaSharp;
using System;

namespace PhotoViewer.UI.Viewer.Transitions;

internal sealed class ViewerTransition(TransitionRequest request, SKPicture fromFrame) : PhDisposable
{

    public TransitionRequest Request { get; } = request;

    public SKPicture FromFrame { get; } = fromFrame;

    private bool _isFromFrameTaken;

    public long HoldStartTick { get; } = Environment.TickCount64;

    public TimeSpan? StartTime { get; set; }

    public float Progress { get; set; }

    public SKPicture TakeFromFrame()
    {
        _isFromFrameTaken = true;
        return FromFrame;
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        if (!_isFromFrameTaken) FromFrame.Dispose();
        Request.Complete();
    }

}
