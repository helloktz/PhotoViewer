using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public sealed class SkiaCodecAdapter : PhDisposable, ICodec
{

    private static readonly string[] _supportedExtensions =
        [".bmp", ".gif", ".gifv", ".jpg", ".jpeg", ".png", ".webp"];

    public string CodecId { get; } = "skiasharp";

    public string CodecName { get; } = "SkiaSharp";

    public int MetadataPriority { get; } = 10;

    public int DecodePriority { get; } = 100;

    public int EncodePriority { get; }

    public IReadOnlyList<string> DecodingExtensions => _supportedExtensions;

    public bool SupportsEncoding { get; }

    public IReadOnlyList<string> EncodingExtensions => [];

    public bool CanLoadMetadata(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || SvgCodec.IsSvgFile(filePath)) return false;

        try
        {
            return SkiaCodec.CanPing(filePath);
        }
        catch
        {
            return false;
        }
    }

    public Task<PhotoMetadata> LoadMetadataAsync(string filePath,
        PhotoReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return SkiaCodec.LoadMetadataAsync(filePath, options, cancellationToken);
    }

    public bool CanDecode(PhotoMetadata metadata, CodecSelectionContext context)
    {
        if (metadata is null || metadata.IsVector) return false;
        if (!context.IsDestColorProfileSupported && metadata.FrameCount <= 1) return false;
        if (context.LoadOtherThumbnailOnly) return false;

        if (context.LoadRawThumbnailOnly)
        {
            try
            {
                return SkiaCodec.CanReadRawPreview(metadata,
                    context.PreviewMinWidth, context.PreviewMinHeight);
            }
            catch
            {
                return false;
            }
        }

        if (Array.IndexOf(_supportedExtensions, metadata.FileExtension) < 0) return false;

        try
        {
            return SkiaCodec.CanRead(metadata);
        }
        catch
        {
            return false;
        }
    }

    public async Task<CodecDecodeResult> DecodeAsync(PhotoMetadata metadata,
        PhotoReadOptions options,
        CodecSelectionContext context,
        CancellationToken cancellationToken = default)
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
