using Avalonia;
using PhotoViewer.Common.Types;
using SkiaSharp;

namespace PhotoViewer.Common.Photoing;

public partial class SkiaDecoderOutput : PhDisposable
{
    public SKImage? SingleFrame { get; set; } = null;
    public SkiaAnimator? Animator { get; set; } = null;
    public SkiaVectorSource? VectorSource { get; set; } = null;

    public Size Size { get; set; } = new();

    public double DecodeScale { get; set; } = 1;

    protected override void OnDisposing()
    {
        base.OnDisposing();

        SingleFrame?.Dispose();
        SingleFrame = null;

        Animator?.Dispose();
        Animator = null;

        VectorSource?.Dispose();
        VectorSource = null;
    }
}
