using PhotoViewer.UI.Viewer.Transitions;
using System;
using System.Threading;

namespace PhotoViewer.Common.Photoing;

public enum PhotoState
{

    None,

    Preview,

    Loaded,
}

public class PhotoLoadingEventArgs(PhotoState state, Photo photo, CancellationToken token) : EventArgs
{

    public PhotoState State => state;

    public Photo Photo => photo;

    public PhotoMetadata Metadata => photo.Metadata;

    public CancellationToken CancelToken => token;

}

public class PhotoLoadingOptions
{
    public bool UseCache { get; set; } = true;
    public bool ResetZoom { get; set; } = true;
    public ColorChannels Channels { get; set; } = ColorChannels.RGBA;

    public ImageOrientation Orientation { get; set; } = ImageOrientation.Identity;

    public bool IsColorInverted { get; set; }

    public TransitionRequest? Transition { get; set; }
}
