using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public sealed class PhotoFrameReader : PhDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ICodec? _codec;
    private readonly CodecSelectionContext _context;
    private readonly PhotoReadOptions _readOptions;

    private AnimatorImpl? _animator;
    private SKImage? _firstFrame;

    public PhotoMetadata Metadata { get; }

    public int FrameCount => _animator?.Frames.Length ?? (int)Math.Max(1, Metadata.FrameCount);

    private PhotoFrameReader(PhotoMetadata meta, ICodec? codec, CodecSelectionContext context, PhotoReadOptions readOptions)
    {
        Metadata = meta;
        _codec = codec;
        _context = context;
        _readOptions = readOptions;
    }

    public static async Task<PhotoFrameReader?> OpenAsync(string filePath, PhotoReadOptions readOptions, CancellationToken token)
    {
        var meta = await LoadMetadataAsync(filePath, token).ConfigureAwait(false);
        if (meta is null) return null;

        var context = new CodecSelectionContext
        {
            EnableVectorRenderer = false,
            IsDestColorProfileSupported = Core.IsDestColorProfileSupported,
        };

        var reader = new PhotoFrameReader(meta, Core.CodecRegistry.SelectDecodeCodec(meta, context), context, readOptions);

        try
        {
            await reader.ReadFirstFrameAsync(token).ConfigureAwait(false);
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    public async Task<SKImage?> ReadFrameAsync(int frameIndex, uint maxSize, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);

        try
        {
            if (IsDisposed) return null;

            if (_animator is not null)
            {
                return SkiaCodec.CopyImage(_animator.GetRenderedFrameBitmap((uint)frameIndex));
            }

            if (frameIndex == 0 && _firstFrame is not null)
            {
                var first = _firstFrame;
                _firstFrame = null;
                return first;
            }

            return await DecodeFrameAsync(frameIndex, maxSize, token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReadFirstFrameAsync(CancellationToken token)
    {
        if (_codec is null) return;

        CodecDecodeResult? result = null;
        try
        {
            result = await _codec.DecodeAsync(Metadata, _readOptions with { FrameIndex = 0 }, _context, token).ConfigureAwait(false);

            _animator = result.Animator;
            _firstFrame = result.SingleFrame;
            result.Animator = null;
            result.SingleFrame = null;

            if (_animator is not null)
            {
                _firstFrame?.Dispose();
                _firstFrame = null;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(PhotoFrameReader)}.{nameof(ReadFirstFrameAsync)}: {ex.Message}");
        }
        finally
        {
            result?.Dispose();
        }
    }

    private async Task<SKImage?> DecodeFrameAsync(int frameIndex, uint maxSize, CancellationToken token)
    {
        var options = _readOptions with { FrameIndex = frameIndex, Width = maxSize, Height = maxSize };

        if (_codec is not null)
        {
            try
            {
                using var result = await _codec.DecodeAsync(Metadata, options, _context, token).ConfigureAwait(false);
                var frame = result.SingleFrame;
                result.SingleFrame = null;

                if (!frame.IsDisposed()) return frame;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌ {nameof(PhotoFrameReader)}.{nameof(DecodeFrameAsync)} ({_codec.CodecId}, frame {frameIndex}): {ex.Message}");
            }
        }

        using var data = await MagickCodec.DecodeImageAsync(Metadata, options, null, null, token).ConfigureAwait(false);
        return SkiaCodec.FromMagick(data.SingleFrame, MagickCodec.GetDecodedColorSpace(data, Metadata), Metadata.IsHdr);
    }

    private static async Task<PhotoMetadata?> LoadMetadataAsync(string filePath, CancellationToken token)
    {
        try
        {
            var codec = Core.CodecRegistry.SelectMetadataCodec(filePath);
            if (codec is null) return null;

            return await codec.LoadMetadataAsync(filePath, null, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(PhotoFrameReader)}.{nameof(LoadMetadataAsync)}: {ex.Message}");
            return null;
        }
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        _animator?.Dispose();
        _animator = null;
        _firstFrame?.Dispose();
        _firstFrame = null;
        Metadata.Dispose();
    }
}
