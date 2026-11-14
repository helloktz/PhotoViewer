using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public interface ICodec : IDisposable
{

    string CodecId { get; }

    string CodecName { get; }

    int MetadataPriority { get; }

    int DecodePriority { get; }

    int EncodePriority { get; }

    IReadOnlyList<string> DecodingExtensions { get; }

    bool SupportsEncoding { get; }

    IReadOnlyList<string> EncodingExtensions { get; }

    bool CanLoadMetadata(string filePath);

    Task<PhotoMetadata> LoadMetadataAsync(string filePath,
        PhotoReadOptions? options = null,
        CancellationToken cancellationToken = default);

    bool CanDecode(PhotoMetadata metadata, CodecSelectionContext context);

    Task<CodecDecodeResult> DecodeAsync(PhotoMetadata metadata,
        PhotoReadOptions options,
        CodecSelectionContext context,
        CancellationToken cancellationToken = default);

    bool CanEncode(string destFilePath, CodecEncodeContext context);

    Task<CodecEncodeResult> EncodeAsync(CodecEncodeRequest request,
        CancellationToken cancellationToken = default);

    bool CanEncodeMultiFrame(string destFilePath, CodecEncodeContext context);

    Task<CodecEncodeResult> EncodeMultiFrameAsync(CodecMultiFrameEncodeRequest request,
        CancellationToken cancellationToken = default);
}
