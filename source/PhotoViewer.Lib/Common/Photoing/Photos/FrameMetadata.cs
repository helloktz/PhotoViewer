using ImageMagick;
using SkiaSharp;

namespace PhotoViewer.Common.Photoing;

public class FrameMetadata
{
    public MagickColor BackgroundColor { get; internal set; } = MagickColors.Transparent;
    public uint Width { get; internal set; } = 0;
    public uint Height { get; internal set; } = 0;
    public int X { get; internal set; } = 0;
    public int Y { get; internal set; } = 0;

    public SKCodecFrameInfo? Animation { get; internal set; }

}
