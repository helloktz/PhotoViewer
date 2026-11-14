using PhotoViewer.Common.Types;
using ImageMagick;

namespace PhotoViewer.Common.Photoing;

public enum MagickColorConversion
{
    None,
    ToDisplayProfile,
    ToSrgb,
}

public partial class MagickDecoderOutput : PhDisposable
{
    public MagickImageCollection? MultiFrames { get; set; } = null;
    public MagickImage? SingleFrame { get; set; } = null;

    public MagickColorConversion ColorConversion { get; set; } = MagickColorConversion.None;

    protected override void OnDisposing()
    {
        base.OnDisposing();

        MultiFrames?.Dispose();
        MultiFrames = null;

        SingleFrame?.Dispose();
        SingleFrame = null;
    }

}
