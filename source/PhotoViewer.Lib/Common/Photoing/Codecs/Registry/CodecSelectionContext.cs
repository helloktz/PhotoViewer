namespace PhotoViewer.Common.Photoing;

public sealed class CodecSelectionContext
{

    public bool EnableVectorRenderer { get; init; }

    public bool IsDestColorProfileSupported { get; init; }

    public bool LoadRawThumbnailOnly { get; init; }

    public bool LoadOtherThumbnailOnly { get; init; }

    public int PreviewMinWidth { get; init; }

    public int PreviewMinHeight { get; init; }
}
