namespace PhotoViewer.Common.Photoing;

public record PhotoReadOptions
{

    public uint Width { get; set; } = 0;

    public uint Height { get; set; } = 0;

    public bool OnlyLoadRawPreview { get; set; } = false;

    public bool OnlyLoadNonRawPreview { get; set; } = false;

    public int PreviewMinWidth { get; set; } = 0;

    public int PreviewMinHeight { get; set; } = 0;

    public bool CorrectRotation { get; set; } = true;

    public int FrameIndex { get; set; } = 0;

    public bool SkipDisplayProfile { get; set; } = false;

    public PhotoReadOptions()
    {
    }

    public static PhotoReadOptions FromConfig(int frameIndex = 0) => new()
    {
        FrameIndex = frameIndex,
        OnlyLoadRawPreview = Core.Config.EnableOnlyLoadRawPreview,
        OnlyLoadNonRawPreview = Core.Config.EnableOnlyLoadNonRawPreview,
        PreviewMinWidth = Core.Config.PreviewMinWidth,
        PreviewMinHeight = Core.Config.PreviewMinHeight,
    };
}
