using System;

namespace PhotoViewer.Common.Photoing;

public class AnimatorFrameChangedEventArgs : EventArgs
{
    public required uint FrameCount { get; init; }
    public required uint CurrentFrame { get; init; }
    public required uint LoopCount { get; init; }
    public required uint CurrentLoop { get; init; }
}
