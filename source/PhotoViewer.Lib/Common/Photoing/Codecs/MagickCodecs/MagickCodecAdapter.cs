using Avalonia;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public sealed class MagickCodecAdapter : PhDisposable, ICodec
{
    private static readonly string[] _supportedExtensions = [];

    public string CodecId { get; } = "magick.net";

    public string CodecName { get; } = "Magick.NET";

    public int MetadataPriority { get; } = 100;

    public int DecodePriority { get; } = 10;

    public int EncodePriority { get; } = 10;

    public IReadOnlyList<string> DecodingExtensions => _supportedExtensions;

    public bool SupportsEncoding { get; } = true;

    public IReadOnlyList<string> EncodingExtensions => _supportedExtensions;

    public bool CanLoadMetadata(string filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath)
            && MagickCodec.CanRead(filePath);
    }

    public Task<PhotoMetadata> LoadMetadataAsync(string filePath,
        PhotoReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return MagickCodec.LoadMetadataAsync(filePath, options, null, cancellationToken);
    }

    public bool CanDecode(PhotoMetadata metadata, CodecSelectionContext context)
    {
        return !string.IsNullOrWhiteSpace(metadata?.FilePath)
            && MagickCodec.CanRead(metadata.FilePath);
    }

    public async Task<CodecDecodeResult> DecodeAsync(PhotoMetadata metadata,
        PhotoReadOptions options,
        CodecSelectionContext context,
        CancellationToken cancellationToken = default)
    {
        using var output = await MagickCodec.DecodeImageAsync(
            metadata,
            options,
            null,
            null,
            cancellationToken).ConfigureAwait(false);

        var image = SkiaCodec.FromMagick(output.SingleFrame, MagickCodec.GetDecodedColorSpace(output, metadata),
            metadata.IsHdr, out var decodeScale);

        return new CodecDecodeResult
        {
            CodecId = CodecId,
            ContentKind = image is not null ? CodecContentKind.StaticRaster : CodecContentKind.None,
            Size = new Size(image?.Width ?? 0, image?.Height ?? 0),
            DecodeScale = decodeScale,
            SingleFrame = image,
            IsHdr = metadata.IsHdr,
            HasEmbeddedColorProfile = metadata.SkiaColorSpace is not null || metadata.MagickColorProfile is not null,
            HasBakedDisplayProfile = MagickCodec.HasBakedDisplayProfile(output, metadata),
        };
    }

    public bool CanEncode(string destFilePath, CodecEncodeContext context)
    {
        return !string.IsNullOrWhiteSpace(destFilePath)
            && MagickCodec.CanWrite(destFilePath);
    }

    public async Task<CodecEncodeResult> EncodeAsync(CodecEncodeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CanEncode(request.DestFilePath, CodecEncodeContext.Default))
        {
            return new CodecEncodeResult(false, true);
        }

        try
        {
            await SkiaCodec.SaveAsync(request.Source, request.DestFilePath, null,
                request.Quality, cancellationToken).ConfigureAwait(false);
            return new CodecEncodeResult(true, false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new CodecEncodeResult(false, false, ex.Message);
        }
    }

    public bool CanEncodeMultiFrame(string destFilePath, CodecEncodeContext context) => false;

    public Task<CodecEncodeResult> EncodeMultiFrameAsync(CodecMultiFrameEncodeRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new CodecEncodeResult(false, true));
}
