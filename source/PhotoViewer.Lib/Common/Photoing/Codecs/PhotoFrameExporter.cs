using PhotoViewer.Common.Extensions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public static class PhotoFrameExporter
{

    public static async IAsyncEnumerable<(int FrameNumber, int FrameCount, string FileName)> SaveFramesAsync(
        string srcFilePath, string destFolder,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        Directory.CreateDirectory(destFolder);

        var meta = await LoadMetadataAsync(srcFilePath, token).ConfigureAwait(false);
        var context = CreateSelectionContext();
        var codec = meta is null ? null : Core.CodecRegistry.SelectDecodeCodec(meta, context);

        var isMagickDecoder = codec is null
            || ReferenceEquals(codec, Core.CodecRegistry.FallbackDecodeCodec);

        AnimatorImpl? animator = null;
        SKImage? pendingFrame = null;

        if (meta is not null && codec is not null && !isMagickDecoder)
        {
            var firstResult = await TryDecodeAsync(codec, meta, context, 0, token).ConfigureAwait(false);

            if (firstResult is not null)
            {
                animator = firstResult.Animator;
                pendingFrame = firstResult.SingleFrame;

                firstResult.Animator = null;
                firstResult.SingleFrame = null;
                firstResult.Dispose();

                if (animator is not null)
                {
                    pendingFrame?.Dispose();
                    pendingFrame = null;
                }
            }
        }

        if (animator is null && pendingFrame is null)
        {
            meta?.Dispose();

            await foreach (var info in MagickCodec.SaveFramesAsync(srcFilePath, destFolder, token)
                .ConfigureAwait(false))
            {
                yield return info;
            }
            yield break;
        }

        try
        {
            var frameCount = animator?.Frames.Length ?? (int)Math.Max(1, meta!.FrameCount);
            var srcFileName = Path.GetFileNameWithoutExtension(srcFilePath);
            var numberFormat = $"D{frameCount.ToString().Length}";

            for (var i = 0; i < frameCount; i++)
            {
                if (token.IsCancellationRequested) break;

                var frameNumber = i + 1;
                var fileName = $"{srcFileName} - {frameNumber.ToString(numberFormat)}.png";
                var destFilePath = Path.Combine(destFolder, fileName);

                var animatorFrame = animator?.GetRenderedFrameBitmap((uint)i);
                SKImage? ownedFrame = null;

                if (animatorFrame is null)
                {
                    ownedFrame = pendingFrame;
                    pendingFrame = null;

                    ownedFrame ??= await TryDecodeFrameAsync(codec!, meta!, context, i, token)
                        .ConfigureAwait(false);
                }

                var frame = animatorFrame ?? ownedFrame;

                try
                {
                    await WriteFrameAsync(frame, destFilePath, token).ConfigureAwait(false);
                }
                finally
                {
                    ownedFrame?.Dispose();
                }

                yield return (frameNumber, frameCount, fileName);
            }
        }
        finally
        {
            animator?.Dispose();
            pendingFrame?.Dispose();
            meta?.Dispose();
        }
    }

    private static async Task<PhotoMetadata?> LoadMetadataAsync(string srcFilePath, CancellationToken token)
    {
        try
        {
            var codec = Core.CodecRegistry.SelectMetadataCodec(srcFilePath);
            if (codec is null) return null;

            return await codec.LoadMetadataAsync(srcFilePath, null, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(LoadMetadataAsync)}: {ex.Message}");
            return null;
        }
    }

    private static CodecSelectionContext CreateSelectionContext()
    {
        return new CodecSelectionContext
        {
            EnableVectorRenderer = Core.Config.EnableVectorRenderer,
            IsDestColorProfileSupported = Core.IsDestColorProfileSupported,
        };
    }

    private static async Task<CodecDecodeResult?> TryDecodeAsync(ICodec codec, PhotoMetadata meta,
        CodecSelectionContext context, int frameIndex, CancellationToken token)
    {
        try
        {
            var options = new PhotoReadOptions() { FrameIndex = frameIndex };
            return await codec.DecodeAsync(meta, options, context, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(TryDecodeAsync)} ({codec.CodecId}, frame {frameIndex}): {ex.Message}");
            return null;
        }
    }

    private static async Task<SKImage?> TryDecodeFrameAsync(ICodec codec, PhotoMetadata meta,
        CodecSelectionContext context, int frameIndex, CancellationToken token)
    {
        var result = await TryDecodeAsync(codec, meta, context, frameIndex, token).ConfigureAwait(false);
        if (result is null) return null;

        var frame = result.SingleFrame;

        result.SingleFrame = null;
        result.Dispose();

        return frame;
    }

    private static async Task WriteFrameAsync(SKImage? frame, string destFilePath, CancellationToken token)
    {
        if (frame.IsDisposed()) return;

        try
        {
            using var encoded = frame.Encode(SKEncodedImageFormat.Png, 100);

            if (encoded is null || encoded.Size == 0)
            {
                await SkiaCodec.SaveAsync(frame, destFilePath, null, 100, token).ConfigureAwait(false);
                return;
            }

            await using var fileStream = File.Create(destFilePath);
            encoded.SaveTo(fileStream);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
        {
            Debug.WriteLine($"❌❌❌ {nameof(WriteFrameAsync)} ({destFilePath}): {ex.Message}");
        }
    }

}
