using PhotoViewer.Common.Extensions;
using SkiaSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

internal static class CodecEncodePipeline
{

    public static async Task<bool> TryEncodeAsync(Photo photo, string destFilePath,
        PhotoTransform? transform, uint quality, CancellationToken token)
    {
        if (photo is null || string.IsNullOrEmpty(destFilePath)) return false;

        var codec = Core.CodecRegistry.SelectEncodeCodec(destFilePath);

        if (codec is null || codec is MagickCodecAdapter) return false;

        var isMultiFrame = !photo.IsClipboard
            && photo.Metadata.FrameCount > 1
            && ReferenceEquals(Core.CodecRegistry.SelectMultiFrameEncodeCodec(destFilePath), codec);

        var tempPath = SaveStaging.BuildTempPath(destFilePath);
        SaveStaging.SweepStaleTempFiles(destFilePath);

        try
        {
            var result = isMultiFrame
                ? await EncodeMultiFrameAsync(codec, photo, tempPath, transform, quality, token).ConfigureAwait(false)
                : await EncodeSingleFrameAsync(codec, photo, tempPath, transform, quality, token).ConfigureAwait(false);

            if (result.Unsupported)
            {
                SaveStaging.TryDelete(tempPath);
                return false;
            }
            if (!result.Succeeded)
            {
                SaveStaging.TryDelete(tempPath);
                throw new InvalidDataException(
                    $"IGE: '{codec.CodecName}' could not write the image. {result.Error}".TrimEnd());
            }
        }
        catch
        {
            SaveStaging.TryDelete(tempPath);
            throw;
        }

        var (promoted, promoteError) = await SaveStaging.PromoteAsync(tempPath, destFilePath, token).ConfigureAwait(false);
        if (!promoted)
        {
            throw new IOException($"IGE: could not finalize '{destFilePath}'. {promoteError}".TrimEnd());
        }

        return true;
    }

    private static async Task<CodecEncodeResult> EncodeSingleFrameAsync(ICodec codec, Photo photo,
        string tempPath, PhotoTransform? transform, uint quality, CancellationToken token)
    {
        var (source, ownsSource) = await GetSingleFrameAsync(photo, transform, token).ConfigureAwait(false);
        if (source is null) return new CodecEncodeResult(false, false, "no pixels to encode");

        try
        {
            return await codec.EncodeAsync(new CodecEncodeRequest
            {
                DestFilePath = tempPath,
                Source = source,
                Quality = quality,
                SourceFilePath = photo.IsClipboard ? null : photo.FilePath,
                SourceIccProfile = GetIccProfile(photo),
            }, token).ConfigureAwait(false);
        }
        finally
        {
            if (ownsSource) source.Dispose();
        }
    }

    private static async Task<(SKImage? Image, bool Owned)> GetSingleFrameAsync(Photo photo,
        PhotoTransform? transform, CancellationToken token)
    {
        if (photo.IsClipboard)
        {
            if (photo.Bitmap is not SKImage img || img.IsDisposed()) return (null, false);

            var transformed = SkiaCodec.TransformImage(img, transform);
            return transformed is not null ? (transformed, true) : (img, false);
        }

        var frameIndex = (uint)Math.Max(0, photo.ReadOptions.FrameIndex);
        var decoded = await photo.DecodeStaticFrameAsync(frameIndex, token).ConfigureAwait(false);
        if (decoded is null) return (null, false);

        var withTransform = SkiaCodec.TransformImage(decoded, transform);
        if (withTransform is null) return (decoded, true);

        decoded.Dispose();
        return (withTransform, true);
    }

    private static async Task<CodecEncodeResult> EncodeMultiFrameAsync(ICodec codec, Photo photo,
        string tempPath, PhotoTransform? transform, uint quality, CancellationToken token)
    {
        var frameCount = (int)Math.Max(1, photo.Metadata.FrameCount);
        var animator = photo.Bitmap as AnimatorImpl;
        var wasPlaying = animator?.IsPlaying == true;

        if (wasPlaying) animator!.Pause();

        try
        {
            return await codec.EncodeMultiFrameAsync(new CodecMultiFrameEncodeRequest
            {
                DestFilePath = tempPath,
                FrameCount = frameCount,
                IsAnimated = animator is not null,
                LoopCount = (int)(animator?.LoopCount ?? photo.Metadata.AnimationLoop),
                Quality = quality,
                SourceFilePath = photo.FilePath,
                SourceIccProfile = GetIccProfile(photo),
                GetFrameAsync = (index, ct) => GetMultiFrameAsync(photo, animator, index, transform, ct),
            }, token).ConfigureAwait(false);
        }
        finally
        {
            if (wasPlaying) animator!.Play();
        }
    }

    private static async Task<CodecEncodeFrame?> GetMultiFrameAsync(Photo photo, AnimatorImpl? animator,
        int index, PhotoTransform? transform, CancellationToken token)
    {
        if (animator is not null)
        {
            var frame = animator.GetRenderedFrameBitmap((uint)index);
            if (frame.IsDisposed()) return null;

            var duration = index < animator.Frames.Length ? animator.Frames[index].Duration : 0;
            var shaped = ApplyFrameTransform(frame, transform, index);

            return shaped is not null
                ? new CodecEncodeFrame(shaped, duration, true)
                : new CodecEncodeFrame(frame, duration, false);
        }

        var decoded = await photo.DecodeStaticFrameAsync((uint)index, token).ConfigureAwait(false);
        if (decoded is null) return null;

        var transformed = ApplyFrameTransform(decoded, transform, index);
        if (transformed is null) return new CodecEncodeFrame(decoded, 0, true);

        decoded.Dispose();
        return new CodecEncodeFrame(transformed, 0, true);
    }

    private static SKImage? ApplyFrameTransform(SKImage frame, PhotoTransform? transform, int index)
    {
        if (transform is null) return null;
        if (transform.FrameIndex != -1 && transform.FrameIndex != index) return null;

        return SkiaCodec.TransformImage(frame, transform);
    }

    private static byte[]? GetIccProfile(Photo photo)
    {
        try
        {
            return photo.Metadata.MagickColorProfile?.ToByteArray();
        }
        catch
        {
            return null;
        }
    }

}
