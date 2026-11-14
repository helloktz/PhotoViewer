using Avalonia;
using PhotoViewer.Common.Types;
using SkiaSharp;

namespace PhotoViewer.Common.Photoing;

public sealed class CodecDecodeResult : PhDisposable
{

    public string CodecId { get; set; } = string.Empty;

    public CodecContentKind ContentKind { get; set; } = CodecContentKind.None;

    public Size Size { get; set; } = new();

    public double DecodeScale { get; set; } = 1;

    public SKImage? SingleFrame { get; set; } = null;

    public AnimatorImpl? Animator { get; set; } = null;

    public SkiaVectorSource? VectorSource { get; set; } = null;

    public bool IsHdr { get; set; } = false;

    public bool HasEmbeddedColorProfile { get; set; } = false;

    public bool HasBakedDisplayProfile { get; set; } = false;

    public bool PreferDirectRender { get; set; } = false;

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
