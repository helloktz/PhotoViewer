namespace PhotoViewer.Common.Photoing;

internal static class CodecDecodeResultFactory
{
    public static CodecDecodeResult FromSkiaOutput(string codecId,
        SkiaDecoderOutput output, PhotoMetadata metadata)
    {
        var result = new CodecDecodeResult
        {
            CodecId = codecId,
            Size = output.Size,
            DecodeScale = output.DecodeScale,
            IsHdr = metadata.IsHdr,
            HasEmbeddedColorProfile = metadata.SkiaColorSpace is not null || metadata.MagickColorProfile is not null,
        };

        if (output.VectorSource is not null)
        {
            result.ContentKind = CodecContentKind.Vector;
            result.PreferDirectRender = true;
            result.VectorSource = output.VectorSource;
            output.VectorSource = null;
            return result;
        }

        if (output.Animator is not null)
        {
            result.ContentKind = CodecContentKind.Animation;
            result.Animator = output.Animator;
            output.Animator = null;
            return result;
        }

        if (output.SingleFrame is not null)
        {
            result.ContentKind = CodecContentKind.StaticRaster;
            result.SingleFrame = output.SingleFrame;
            output.SingleFrame = null;
        }

        return result;
    }
}
