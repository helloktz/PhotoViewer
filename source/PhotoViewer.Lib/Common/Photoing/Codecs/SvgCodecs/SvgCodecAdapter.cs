using PhotoViewer.Common.Types;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public sealed class SvgCodecAdapter : PhDisposable, ICodec
{
    private static readonly string[] _supportedExtensions = [".svg", ".svgz"];

    public string CodecId { get; } = "svg.skia";

    public string CodecName { get; } = "Svg.Skia";

    public int MetadataPriority { get; } = 200;

    public int DecodePriority { get; } = 200;

    public int EncodePriority { get; }

    public IReadOnlyList<string> DecodingExtensions => _supportedExtensions;

    public bool SupportsEncoding { get; }

    public IReadOnlyList<string> EncodingExtensions => [];

    public bool CanLoadMetadata(string filePath)
    {
        return SvgCodec.IsSvgFile(filePath);
    }

    public Task<PhotoMetadata> LoadMetadataAsync(string filePath,
        PhotoReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return SvgCodec.LoadMetadataAsync(filePath, cancellationToken);
    }

    public bool CanDecode(PhotoMetadata metadata, CodecSelectionContext context)
    {
        return context.EnableVectorRenderer
            && metadata.IsVector
            && SvgCodec.IsSvgFile(metadata.FilePath);
    }

    public async Task<CodecDecodeResult> DecodeAsync(PhotoMetadata metadata,
        PhotoReadOptions options, CodecSelectionContext context, CancellationToken cancellationToken = default)
    {
        using var output = await SkiaCodec.LoadAsync(metadata, options, cancellationToken).ConfigureAwait(false);

        return CodecDecodeResultFactory.FromSkiaOutput(CodecId, output, metadata);
    }

    public bool CanEncode(string destFilePath, CodecEncodeContext context) => false;

    public Task<CodecEncodeResult> EncodeAsync(CodecEncodeRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new CodecEncodeResult(false, true));

    public bool CanEncodeMultiFrame(string destFilePath, CodecEncodeContext context) => false;

    public Task<CodecEncodeResult> EncodeMultiFrameAsync(CodecMultiFrameEncodeRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new CodecEncodeResult(false, true));
}
