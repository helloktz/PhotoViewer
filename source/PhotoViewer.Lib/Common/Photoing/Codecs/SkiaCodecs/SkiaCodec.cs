using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cysharp.Collections;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer;
using ImageMagick;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public static partial class SkiaCodec
{

    private const long MAX_PIXEL_BUFFER_BYTES = int.MaxValue;

    private static readonly float[] NATIVE_DECODE_SCALES =
        [7 / 8f, 6 / 8f, 5 / 8f, 4 / 8f, 3 / 8f, 2 / 8f, 1 / 8f];

    private const long MIN_DRAW_BAND_PIXELS = 1 << 18;

    public static async Task<PhotoMetadata> LoadMetadataAsync(string? filePath,
        PhotoReadOptions? options = null, CancellationToken token = default)
    {
        filePath ??= string.Empty;
        var meta = new PhotoMetadata(filePath);

        if (string.IsNullOrWhiteSpace(filePath)) return meta;

        if (SvgCodec.IsSvgFile(filePath))
        {
            return await SvgCodec.LoadMetadataAsync(filePath, token);
        }

        using var decoder = SKCodec.Create(filePath);
        if (decoder.IsDisposed()) return meta;

        var frameIndex = options?.FrameIndex ?? 0;

        if (frameIndex >= decoder.FrameCount) frameIndex = 0;
        else if (frameIndex < 0) frameIndex = (int)decoder.FrameCount - 1;

        meta.FrameCount = (uint)decoder.FrameCount;
        if (token.IsCancellationRequested) return meta;

        var readingTask = Task.Run(() =>
        {
            try
            {
                var skiaFramesInfo = SkiaCodec.GetFramesMetadata(meta.FilePath);
                meta.CanAnimate = skiaFramesInfo?.Count > 0;

                meta.Frames = skiaFramesInfo?.Select(aniInfo => new FrameMetadata
                {
                    Animation = aniInfo,
                }).ToImmutableList() ?? [];

                meta.OriginalWidth = meta.Width = (uint)decoder.Info.Width;
                meta.OriginalHeight = meta.Height = (uint)decoder.Info.Height;

                meta.HasAlpha = !decoder.Info.IsOpaque;
            }
            catch { }
            if (token.IsCancellationRequested) return;

            try
            {
                meta.SkiaColorSpace = decoder.Info.ColorSpace;
                using var colorProfile = decoder.Info.ColorSpace.ToProfile();

                if (colorProfile.Size > 0)
                {
                    var bytes = new byte[colorProfile.Size];
                    unsafe
                    {
                        new ReadOnlySpan<byte>((void*)colorProfile.Buffer, (int)colorProfile.Size)
                            .CopyTo(bytes);
                    }

                    var photoProfile = new PhotoColorProfile(bytes);

                    meta.ColorProfileName = photoProfile.GetIccDescription();
                    if (photoProfile.ProfileData is not null)
                    {
                        meta.MagickColorProfile = new ColorProfile(photoProfile.ProfileData);
                    }
                }

                meta.ColorSpace = decoder.Info.ColorSpace.IsSrgb
                    ? ImageMagick.ColorSpace.sRGB
                    : ImageMagick.ColorSpace.Undefined;
            }
            catch { }

            meta.BitsPerChannel = GetBitsPerChannel(decoder.Info.ColorType);
            MagickCodec.DetectHdrInfo(meta);

            if (!token.IsCancellationRequested)
            {
                var liveInfo = LivePhotoDetector.Detect(meta.FilePath);
                meta.EmbeddedVideoOffsetFromEnd = liveInfo.IsLivePhoto
                    ? liveInfo.EmbeddedVideoOffsetFromEnd
                    : 0;
            }
        }, token).ConfigureAwait(false);

        await readingTask;

        return meta;
    }

    public static async Task<SkiaDecoderOutput> LoadAsync(PhotoMetadata meta,
        PhotoReadOptions options, CancellationToken token)
    {
        return await Task.Run(() => Load(meta, options), token).ConfigureAwait(false);
    }

    public static SkiaDecoderOutput Load(PhotoMetadata meta, PhotoReadOptions options)
    {
        if (meta.IsVector && SvgCodec.IsSvgFile(meta.FilePath))
        {
            var svgResult = new SkiaDecoderOutput();
            var svgDoc = SvgCodec.LoadSvg(meta.FilePath);
            var picture = svgDoc.Picture;

            if (picture is not null)
            {
                var intrinsicSize = SvgCodec.GetIntrinsicSize(picture);
                svgResult.Size = new Size(intrinsicSize.Width, intrinsicSize.Height);

                var maxDim = (int)Math.Max(intrinsicSize.Width, intrinsicSize.Height);
                var rasterized = SvgCodec.RasterizeThumbnail(picture, Math.Min(maxDim, 1024));

                svgResult.VectorSource = new SkiaVectorSource(svgDoc, intrinsicSize, rasterized);
            }
            else
            {
                svgDoc.Dispose();
            }

            return svgResult;
        }

        var codec = SKCodec.Create(meta.FilePath);
        var result = new SkiaDecoderOutput();
        result.Size = new Size(codec.Info.Width, codec.Info.Height);

        if (codec.FrameCount > 0)
        {
            codec.Dispose();
            codec = CreateInMemoryCodec(meta.FilePath);

            var frames = meta.Frames.Select(f => f.Animation ?? CreateUnknownFrameInfo()).ToArray();
            result.Animator = new SkiaAnimator(codec, frames);
            return result;
        }

        var decodeInfo = GetDecodableImageInfo(codec, out var decodeScale);

        if (TryGetRequestedDecodeInfo(codec, decodeInfo, options, out var requestedInfo))
        {
            decodeScale *= (double)requestedInfo.Width / decodeInfo.Width;
            decodeInfo = requestedInfo;
        }

        result.DecodeScale = decodeScale;
        result.Size = new Size(decodeInfo.Width, decodeInfo.Height);

        using var bmpFrame = new SKBitmap(decodeInfo);
        var frameIndex = Math.Min(0, options.FrameIndex);
        var codecOption = new SKCodecOptions(frameIndex);

        if (codec.GetPixels(decodeInfo, bmpFrame.GetPixels(), codecOption) == SKCodecResult.Success)
        {
            var origin = codec.EncodedOrigin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default
                ? meta.Orientation
                : codec.EncodedOrigin;

            if (options.CorrectRotation)
            {
                if (TryApplyOrientation(bmpFrame, origin, out var bmpOriented))
                {
                    if (bmpOriented is not null)
                    {
                        using (bmpOriented)
                        {
                            result.Size = new Size(bmpOriented.Width, bmpOriented.Height);
                            result.SingleFrame = ToSKImageNoCopy(bmpOriented);
                        }
                        bmpFrame.Dispose();
                    }
                }

                if (bmpOriented is null)
                {
                    result.SingleFrame = ToSKImageNoCopy(bmpFrame);
                }
            }
            else
            {
                result.SingleFrame = ToSKImageNoCopy(bmpFrame);
            }
        }

        codec.Dispose();
        codec = null;

        return result;
    }

    private static unsafe SKCodec? CreateInMemoryCodec(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var length = fs.Length;

        using var data = SKData.Create(length);
        var buffer = (byte*)data.Data;

        for (long offset = 0; offset < length;)
        {
            var chunk = (int)Math.Min(length - offset, int.MaxValue);
            fs.ReadExactly(new Span<byte>(buffer + offset, chunk));
            offset += chunk;
        }

        return SKCodec.Create(data);
    }

    private static SKImageInfo GetDecodableImageInfo(SKCodec codec, out double scale)
    {
        scale = 1;
        var fullInfo = codec.Info;
        if (FitsInPixelBuffer(fullInfo)) return fullInfo;

        foreach (var candidate in NATIVE_DECODE_SCALES)
        {
            var size = codec.GetScaledDimensions(candidate);

            if (size.Width >= fullInfo.Width || size.Width <= 0 || size.Height <= 0) continue;

            var scaledInfo = fullInfo.WithSize(size.Width, size.Height);
            if (!FitsInPixelBuffer(scaledInfo)) continue;

            scale = (double)size.Width / fullInfo.Width;
            return scaledInfo;
        }

        var megaPixels = (double)fullInfo.Width * fullInfo.Height / 1_000_000;
        throw new NotSupportedException(
            $"The image is too large to open: {fullInfo.Width:n0}x{fullInfo.Height:n0} "
            + $"({megaPixels:n0} MP) needs {(double)fullInfo.Width * fullInfo.Height * fullInfo.BytesPerPixel / 1024 / 1024 / 1024:n2} GB "
            + $"in one buffer, but the renderer cannot address more than 2 GB per image. "
            + $"This format cannot be decoded at a reduced size.");
    }

    private static bool FitsInPixelBuffer(SKImageInfo info)
    {
        return (long)info.Width * info.Height * info.BytesPerPixel <= MAX_PIXEL_BUFFER_BYTES;
    }

    private static double GetPixelBufferFitScale(long width, long height, int bytesPerPixel)
    {
        var bytes = width * height * bytesPerPixel;
        if (bytes <= 0 || bytes <= MAX_PIXEL_BUFFER_BYTES) return 1;

        return Math.Sqrt((double)MAX_PIXEL_BUFFER_BYTES / bytes) * 0.999;
    }

    public static SKImage? LoadThumbnail(string filePath, int maxDimension)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        try
        {
            using var codec = SKCodec.Create(filePath);
            if (codec.IsDisposed()) return null;

            var origW = codec.Info.Width;
            var origH = codec.Info.Height;
            if (origW <= 0 || origH <= 0) return null;

            var scale = Math.Min((float)maxDimension / origW, (float)maxDimension / origH);
            scale = Math.Min(scale, 1f);

            var scaledSize = codec.GetScaledDimensions(scale);

            var info = new SKImageInfo(scaledSize.Width, scaledSize.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bmp = new SKBitmap(info);

            var result = codec.GetPixels(info, bmp.GetPixels());
            if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
            {
                return null;
            }

            return ToSKImage(bmp);
        }
        catch { return null; }
    }

    public static async Task SaveAsync(SKImage? srcImg, string destFilePath,
        PhotoTransform? transform = null, uint quality = 100, CancellationToken token = default)
    {
        if (srcImg.IsDisposed()) return;

        try
        {
            using var transformedImg = TransformImage(srcImg, transform);
            var dstImg = transformedImg ?? srcImg;
            if (dstImg.IsDisposed() || token.IsCancellationRequested) return;

            if (MagickCodec.CanWrite(destFilePath))
            {
                using var imgM = ToMagick(dstImg);
                if (imgM is null) return;

                imgM.Quality = quality;
                MagickCodec.SetPngCompressionLevel(imgM, destFilePath, quality);
                await imgM.WriteAsync(destFilePath, token);
            }
            else
            {
                throw new FormatException("IGE: Unsupported image format.");
            }

        }
        catch (OperationCanceledException) { }
    }

    public static bool CanPing(string srcFilePath)
    {
        using var codec = SKCodec.Create(srcFilePath);
        if (codec.IsDisposed()) return false;

        return true;
    }

    public static bool CanRead(string srcFilePath)
    {
        using var meta = new PhotoMetadata()
        {
            FilePath = srcFilePath,
        };

        return CanRead(meta);
    }

    public static bool CanRead(PhotoMetadata meta)
    {
        using var codec = SKCodec.Create(meta.FilePath);
        if (codec.IsDisposed()) return false;

        var isMultiFrames = meta.FrameCount > 1;
        if (isMultiFrames) return codec.FrameCount > 1;

        return true;
    }

    private static bool TryGetRequestedDecodeInfo(SKCodec codec, SKImageInfo currentInfo,
        PhotoReadOptions options, out SKImageInfo output)
    {
        output = currentInfo;
        if (options.Width == 0 || options.Height == 0) return false;
        if (currentInfo.Width <= options.Width && currentInfo.Height <= options.Height) return false;

        var scale = Math.Min((float)options.Width / currentInfo.Width,
            (float)options.Height / currentInfo.Height);
        var size = codec.GetScaledDimensions(scale);
        if (size.Width <= 0 || size.Height <= 0 || size.Width >= currentInfo.Width) return false;

        output = currentInfo.WithSize(size.Width, size.Height);
        return true;
    }

    public static bool CanReadRawPreview(PhotoMetadata meta, int minWidth, int minHeight)
    {
        using var codec = SKCodec.Create(meta.FilePath);
        if (codec.IsDisposed()) return false;

        return codec.Info.Width >= minWidth && codec.Info.Height >= minHeight;
    }

    private static int GetBitsPerChannel(SKColorType colorType) => colorType switch
    {
        SKColorType.Rgba8888 or SKColorType.Bgra8888
            or SKColorType.Rgb888x or SKColorType.Gray8
            or SKColorType.Bgr101010xXR => 8,
        SKColorType.Rgba1010102 or SKColorType.Bgra1010102
            or SKColorType.Rgb101010x or SKColorType.Bgr101010x => 10,
        SKColorType.Rgba16161616
            or SKColorType.RgbaF16 or SKColorType.RgbaF16Clamped => 16,
        SKColorType.RgbaF32 => 32,
        _ => 8,
    };

    public static bool IsHighBitDepthColorType(SKColorType colorType) => colorType is
        SKColorType.RgbaF16 or SKColorType.RgbaF16Clamped
        or SKColorType.RgbaF32
        or SKColorType.Rgba16161616
        or SKColorType.Rgba1010102 or SKColorType.Bgra1010102
        or SKColorType.Rgb101010x or SKColorType.Bgr101010x;

    public static List<SKCodecFrameInfo>? GetFramesMetadata(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        using var data = SKData.Create(filePath);
        using var codec = SKCodec.Create(data);
        if (codec == null) return null;

        int frameCount = codec.FrameCount;
        var metadataList = new List<SKCodecFrameInfo>(frameCount);
        var readCount = 0;

        for (int i = 0; i < frameCount; i++)
        {
            if (codec.GetFrameInfo(i, out var info))
            {
                metadataList.Add(info);
                readCount++;
            }
            else
            {
                metadataList.Add(CreateUnknownFrameInfo());
            }
        }

        if (frameCount > 0 && readCount == 0) return null;

        return metadataList;
    }

    private static SKCodecFrameInfo CreateUnknownFrameInfo() => new()
    {
        RequiredFrame = -1,

        Duration = 0,

        DisposalMethod = SKCodecAnimationDisposalMethod.Keep,
    };

    public static SKImage? TransformImage(SKImage? imgSrc, PhotoTransform? transform)
    {
        if (imgSrc.IsDisposed() || transform is null || !transform.HasChanges) return null;

        var result = OrientImage(imgSrc, transform.Orientation);

        if (transform.IsColorInverted)
        {
            var inverted = InvertImageColors(result ?? imgSrc);
            result?.Dispose();
            result = inverted;
        }

        return result;
    }

    public static SKImage? OrientImage(SKImage? imgSrc, ImageOrientation orientation)
    {
        if (imgSrc.IsDisposed() || orientation.IsIdentity) return null;

        var (w, h) = (imgSrc.Width, imgSrc.Height);
        var (outW, outH) = orientation.GetSize(w, h);

        var info = new SKImageInfo(outW, outH, imgSrc.ColorType, imgSrc.AlphaType, imgSrc.ColorSpace);
        using var surface = SKSurface.Create(info);
        if (surface.IsDisposed()) return null;

        var canvas = surface.Canvas;
        canvas.Translate(outW / 2f, outH / 2f);
        canvas.RotateDegrees(orientation.Rotation);
        if (orientation.IsMirrored) canvas.Scale(-1, 1);
        canvas.Translate(-w / 2f, -h / 2f);
        canvas.DrawImage(imgSrc, 0, 0, SKSamplingOptions.Default);

        return surface.Snapshot();
    }

    public static SKImage? ApplyImageEdits(SKImage? imgSrc, ColorChannels channels, bool isColorInverted, ImageOrientation orientation)
    {
        if (imgSrc.IsDisposed()) return null;

        SKImage? result = null;

        if (!channels.HasFlag(ColorChannels.RGBA))
        {
            result = FilterImageColorChannels(imgSrc, channels);
        }

        if (isColorInverted)
        {
            var inverted = InvertImageColors(result ?? imgSrc);
            result?.Dispose();
            result = inverted;
        }

        var oriented = OrientImage(result ?? imgSrc, orientation);
        if (oriented is not null)
        {
            result?.Dispose();
            result = oriented;
        }

        return result;
    }

    public static SKImage? InvertImageColors(SKImage? imgSrc)
    {
        if (imgSrc.IsDisposed()) return null;

        float[] invertMatrix =
        [
            -1,  0,  0,  0,  1,
             0, -1,  0,  0,  1,
             0,  0, -1,  0,  1,
             0,  0,  0,  1,  0,
        ];

        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(invertMatrix),
        };

        var info = new SKImageInfo(imgSrc.Width, imgSrc.Height, imgSrc.ColorType, imgSrc.AlphaType, imgSrc.ColorSpace);
        using var surface = SKSurface.Create(info);
        if (surface.IsDisposed()) return null;

        surface.Canvas.DrawImage(imgSrc, 0, 0, SKSamplingOptions.Default, paint);
        return surface.Snapshot();
    }

    public static SKImage? FilterImageColorChannels(SKImage? imgSrc, ColorChannels colors)
    {
        if (imgSrc.IsDisposed()) return null;

        var redOnly = colors.HasFlag(ColorChannels.R)
            && !colors.HasFlag(ColorChannels.G)
            && !colors.HasFlag(ColorChannels.B);
        var greenOnly = colors.HasFlag(ColorChannels.G)
            && !colors.HasFlag(ColorChannels.R)
            && !colors.HasFlag(ColorChannels.B);
        var blueOnly = colors.HasFlag(ColorChannels.B)
            && !colors.HasFlag(ColorChannels.G)
            && !colors.HasFlag(ColorChannels.R);
        var alphaOnly = colors == ColorChannels.A;
        var keepAlpha = colors.HasFlag(ColorChannels.A) && !alphaOnly;

        var red = !alphaOnly && colors.HasFlag(ColorChannels.R) ? 1f : 0f;
        var green = !alphaOnly && colors.HasFlag(ColorChannels.G) ? 1f : 0f;
        var blue = !alphaOnly && colors.HasFlag(ColorChannels.B) ? 1f : 0f;

        var mRed = redOnly ? 1f : 0f;
        var mGreen = greenOnly ? 1f : 0f;
        var mBlue = blueOnly ? 1f : 0f;

        var fromAlpha = alphaOnly ? 1f : 0f;
        var alphaScale = alphaOnly ? 0f : 1f;
        var alphaOffset = alphaOnly ? 1f : 0f;

        var blendWithBlack = !keepAlpha && !alphaOnly;

        #pragma warning disable format
        float[] matrix =
        [
            red,    mGreen, mBlue,  fromAlpha,      0,
            mRed,   green,  mBlue,  fromAlpha,      0,
            mRed,   mGreen, blue,   fromAlpha,      0,
            0,      0,      0,      alphaScale,     alphaOffset,
        ];
        #pragma warning restore format

        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(matrix),
        };

        var info = new SKImageInfo(imgSrc.Width, imgSrc.Height, imgSrc.ColorType, imgSrc.AlphaType, imgSrc.ColorSpace);
        using var surface = SKSurface.Create(info);
        if (surface.IsDisposed()) return null;

        if (blendWithBlack)
        {
            surface.Canvas.Clear(SKColors.Black);
        }

        surface.Canvas.DrawImage(imgSrc, 0, 0, SKSamplingOptions.Default, paint);
        return surface.Snapshot();
    }

    public static SKImage? RotateImage(SKImage? imgSrc, double degree)
    {
        if (imgSrc.IsDisposed()) return null;

        degree = ((degree % 360) + 360) % 360;
        if (degree == 0) return null;

        var w = imgSrc.Width;
        var h = imgSrc.Height;

        var rad = degree * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(rad));
        var sin = Math.Abs(Math.Sin(rad));
        var outW = (int)Math.Ceiling(w * cos + h * sin - 1e-6);
        var outH = (int)Math.Ceiling(w * sin + h * cos - 1e-6);

        var info = new SKImageInfo(outW, outH, imgSrc.ColorType, imgSrc.AlphaType, imgSrc.ColorSpace);
        using var surface = SKSurface.Create(info);
        if (surface.IsDisposed()) return null;

        var canvas = surface.Canvas;

        canvas.Translate(outW / 2f, outH / 2f);
        canvas.RotateDegrees((float)degree);
        canvas.Translate(-w / 2f, -h / 2f);
        canvas.DrawImage(imgSrc, 0, 0, SKSamplingOptions.Default);

        return surface.Snapshot();
    }

    public static SKImage? FlipImage(SKImage? imgSrc, FlipOptions options)
    {
        if (imgSrc.IsDisposed() || options == FlipOptions.None) return null;

        var w = imgSrc.Width;
        var h = imgSrc.Height;
        var info = new SKImageInfo(w, h, imgSrc.ColorType, imgSrc.AlphaType, imgSrc.ColorSpace);
        using var surface = SKSurface.Create(info);
        if (surface.IsDisposed()) return null;

        var canvas = surface.Canvas;

        if (options.HasFlag(FlipOptions.Horizontal))
        {
            canvas.Scale(-1, 1, w / 2f, 0);
        }
        if (options.HasFlag(FlipOptions.Vertical))
        {
            canvas.Scale(1, -1, 0, h / 2f);
        }

        canvas.DrawImage(imgSrc, 0, 0, SKSamplingOptions.Default);
        return surface.Snapshot();
    }

    public static bool TryApplyOrientation(SKImage? imgSrc, SKEncodedOrigin? orientation, out SKImage? output)
    {
        output = null;
        if (imgSrc.IsDisposed()) return false;

        var bmpSrc = SKBitmap.FromImage(imgSrc);

        if (TryApplyOrientation(bmpSrc, orientation, out var bmpOriented))
        {
            bmpSrc?.Dispose();
            bmpSrc = null;

            output = SKImage.FromBitmap(bmpOriented);
            return true;
        }

        return false;
    }

    public static bool TryApplyOrientation(SKBitmap? bmpSrc, SKEncodedOrigin? orientation, out SKBitmap? output)
    {
        output = null;
        if (bmpSrc.IsDisposed()) return false;

        var origin = orientation ?? SKEncodedOrigin.TopLeft;
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return false;

        var swapDims = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

        var w = swapDims ? bmpSrc.Height : bmpSrc.Width;
        var h = swapDims ? bmpSrc.Width : bmpSrc.Height;

        var info = bmpSrc.Info.WithSize(w, h);
        var bmpOutput = new SKBitmap(info);

        try
        {
            using (var surface = SKSurface.Create(info, bmpOutput.GetPixels(), info.RowBytes))
            {
                var canvas = surface.Canvas;
                switch (origin)
                {
                    case SKEncodedOrigin.TopRight:
                        canvas.Translate(w, 0);
                        canvas.Scale(-1, 1);
                        break;

                    case SKEncodedOrigin.BottomRight:
                        canvas.RotateDegrees(180, w * 0.5f, h * 0.5f);
                        break;

                    case SKEncodedOrigin.BottomLeft:
                        canvas.Translate(0, h);
                        canvas.Scale(1, -1);
                        break;

                    case SKEncodedOrigin.LeftTop:
                        canvas.RotateDegrees(90);
                        canvas.Scale(1, -1);
                        break;

                    case SKEncodedOrigin.RightTop:
                        canvas.Translate(w, 0);
                        canvas.RotateDegrees(90);
                        break;

                    case SKEncodedOrigin.RightBottom:
                        canvas.Translate(w, h);
                        canvas.RotateDegrees(-90);
                        canvas.Scale(1, -1);
                        break;

                    case SKEncodedOrigin.LeftBottom:
                        canvas.Translate(0, h);
                        canvas.RotateDegrees(-90);
                        break;
                }

                canvas.DrawBitmap(bmpSrc, 0, 0, SKSamplingOptions.Default);
                canvas.Flush();
            }

            output = bmpOutput;
            return true;
        }
        catch
        {
            bmpOutput.Dispose();
        }

        return false;
    }

    public static Orientation ToMagickOrientation(SKEncodedOrigin orientation)
    {
        return orientation switch
        {
            SKEncodedOrigin.TopLeft => Orientation.TopLeft,
            SKEncodedOrigin.TopRight => Orientation.TopRight,
            SKEncodedOrigin.BottomRight => Orientation.BottomRight,
            SKEncodedOrigin.BottomLeft => Orientation.BottomLeft,
            SKEncodedOrigin.LeftTop => Orientation.LeftTop,
            SKEncodedOrigin.RightTop => Orientation.RightTop,
            SKEncodedOrigin.RightBottom => Orientation.RightBottom,
            SKEncodedOrigin.LeftBottom => Orientation.LeftBottom,
            _ => Orientation.Undefined,
        };
    }

    public static SKSamplingOptions ToSamplingOptions(this ImageInterpolation interpolation)
    {
        return interpolation switch
        {
            ImageInterpolation.Nearest => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
            ImageInterpolation.NearestMipmapNearest => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.Nearest),
            ImageInterpolation.NearestMipmapLinear => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.Linear),

            ImageInterpolation.Linear => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
            ImageInterpolation.LinearMipmapNearest => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Nearest),
            ImageInterpolation.LinearMipmapLinear => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),

            ImageInterpolation.CubicMitchell => new SKSamplingOptions(SKCubicResampler.Mitchell),
            ImageInterpolation.CubicCatmullRom => new SKSamplingOptions(SKCubicResampler.CatmullRom),

            _ => SKSamplingOptions.Default,
        };
    }

    public static bool TryApplyColorSpace(SKImage? imgSrc, SKColorSpace? destColorSpace, out SKImage? output)
    {
        output = null;
        if (imgSrc.IsDisposed()) return false;

        if (destColorSpace is not null)
        {
            var info = new SKImageInfo(imgSrc.Width, imgSrc.Height,
                imgSrc.ColorType, imgSrc.AlphaType, destColorSpace);
            using var bmp = new SKBitmap();
            var isAllocated = bmp.TryAllocPixels(info);
            if (!isAllocated) return false;

            var isDrawn = TryDrawInBands(imgSrc, bmp);
            if (!isDrawn) return false;

            var converted = ToSKImageNoCopy(bmp);
            if (converted is null) return false;

            output = converted;
            return true;
        }

        return false;
    }

    public static bool TryDrawInBands(SKImage image, SKBitmap dest)
    {
        var pixels = dest.GetPixels();
        if (pixels == IntPtr.Zero) return false;

        var info = dest.Info;
        var rowBytes = dest.RowBytes;
        var bandCount = GetDrawBandCount(image, info);

        var surfaces = new SKSurface?[bandCount];
        var canvases = new SKCanvas[bandCount];
        var tops = new int[bandCount];
        try
        {
            for (var i = 0; i < bandCount; i++)
            {
                var top = (int)((long)info.Height * i / bandCount);
                var bottom = (int)((long)info.Height * (i + 1) / bandCount);
                var bandInfo = info.WithSize(info.Width, bottom - top);

                var surface = SKSurface.Create(bandInfo, pixels + (nint)top * rowBytes, rowBytes);
                if (surface is null) return false;

                surfaces[i] = surface;
                canvases[i] = surface.Canvas;
                tops[i] = top;
            }

            Parallel.For(0, bandCount, i =>
            {
                canvases[i].DrawImage(image, 0, -tops[i], SKSamplingOptions.Default);
            });

            return true;
        }
        finally
        {
            foreach (var surface in surfaces)
            {
                surface?.Dispose();
            }
        }
    }

    private static int GetDrawBandCount(SKImage image, SKImageInfo info)
    {
        if (image.IsLazyGenerated) return 1;
        if (image.IsTextureBacked) return 1;

        var bySize = (long)info.Width * info.Height / MIN_DRAW_BAND_PIXELS;
        var byCores = Math.Min(info.Height, Environment.ProcessorCount * 2);
        var maxBands = Math.Max(1, byCores);

        return (int)Math.Clamp(bySize, 1, maxBands);
    }

    public static SKImage? ScaleDown(SKImage? imgSrc, double maxSize)
    {
        if (imgSrc.IsDisposed() || maxSize <= 0) return null;

        var newSize = BHelper.ResizeRatio(new Size(imgSrc.Width, imgSrc.Height), maxSize);
        var newWidth = Math.Max(1, (int)newSize.Width);
        var newHeight = Math.Max(1, (int)newSize.Height);
        if (newWidth >= imgSrc.Width && newHeight >= imgSrc.Height) return null;

        var info = new SKImageInfo(newWidth, newHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bmpDest = new SKBitmap(info);
        using var pixmap = bmpDest.PeekPixels();
        if (pixmap is null) return null;

        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        if (!imgSrc.ScalePixels(pixmap, sampling)) return null;

        return ToSKImage(bmpDest);
    }

    public static async Task<SKBitmap?> ResizeAsync(SKImage? imgSrc,
        double size, ImageResamplingMethod resample = ImageResamplingMethod.Auto,
        CancellationToken token = default)
    {
        if (imgSrc.IsDisposed()) return null;

        var oldSize = new Size(imgSrc.Width, imgSrc.Height);
        var newSize = BHelper.ResizeRatio(oldSize, size);

        using var bmpSrc = SKBitmap.FromImage(imgSrc);
        return await ResizeAsync(bmpSrc, (int)newSize.Width, (int)newSize.Height, resample, token);
    }

    public static async Task<SKBitmap?> ResizeAsync(SKBitmap? bmpSrc,
        int width, int height, ImageResamplingMethod resample = ImageResamplingMethod.Auto,
        CancellationToken token = default)
    {
        if (bmpSrc.IsDisposed()) return null;

        try
        {
            if (token.IsCancellationRequested) return null;

            var filter = (FilterType)resample;

            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                using var imgM = ToMagick(bmpSrc);
                if (imgM is null) return null;

                if (filter != FilterType.Undefined) imgM.FilterType = filter;

                imgM.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
                token.ThrowIfCancellationRequested();

                using var resizedImg = FromMagick(imgM);
                return resizedImg is null ? null : SKBitmap.FromImage(resizedImg);
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch { }

        return null;
    }

    public static SKColorSpace? CreateIccColorSpace(SKData? iccData)
    {
        if (iccData is null || iccData.IsEmpty) return null;

        try
        {
            var profile = SKColorSpaceIccProfile.Create(iccData);
            if (profile is null) return null;

            var colorSpace = SKColorSpace.CreateIcc(profile);
            if (colorSpace is null) profile.Dispose();

            return colorSpace;
        }
        catch { return null; }
    }

    public static SKColorSpace? CreateIccColorSpace(ReadOnlySpan<byte> iccData)
    {
        if (iccData.IsEmpty) return null;

        var data = SKData.CreateCopy(iccData);
        var colorSpace = CreateIccColorSpace(data);
        if (colorSpace is null) data.Dispose();

        return colorSpace;
    }

    public static (SKColorSpace? ColorSpace, bool IsSupported) GetColorProfileByName(string name)
    {
        (SKColorSpace? ColorSpace, bool IsSupported) results = new(null, true);

        static (SKColorSpace?, bool) AsSdrProfile(SKColorSpace? cs)
        {
            if (HdrToneMapper.IsHdrColorSpace(cs))
            {
                cs?.Dispose();
                cs = null;
            }
            else if (cs?.IsSrgb == true)
            {
                cs = SKColorSpace.CreateSrgb();
            }

            return (cs, cs is not null);
        }

        if (name.Equals(nameof(ColorProfileOption.None))) return results;

        if (name.Equals(nameof(ColorProfileOption.CurrentMonitorProfile)))
        {
            if (string.IsNullOrEmpty(Core.ColorProfileProvider?.ProfilePath))
                return results;

            using var data = SKData.Create(Core.ColorProfileProvider.ProfilePath);
            (results.ColorSpace, results.IsSupported) = AsSdrProfile(CreateIccColorSpace(data));

            return results;
        }

        var magickProfile = MagickCodec.GetBuiltinColorProfile(name);
        if (magickProfile is not null)
        {
            (results.ColorSpace, results.IsSupported) = AsSdrProfile(CreateIccColorSpace(magickProfile.ToReadOnlySpan()));

            return results;
        }

        if (Path.Exists(name))
        {
            using var data = SKData.Create(name);
            (results.ColorSpace, results.IsSupported) = AsSdrProfile(CreateIccColorSpace(data));

            return results;
        }

        return results;
    }

    public static unsafe SKImage? FromMagick(MagickImage? imgM, SKColorSpace? srcColorSpace = null, bool isHdr = false)
        => FromMagick(imgM, srcColorSpace, isHdr, out _);

    public static unsafe SKImage? FromMagick(MagickImage? imgM, SKColorSpace? srcColorSpace,
        bool isHdr, out double decodeScale)
    {
        decodeScale = 1;
        if (imgM is null) return null;

        if (!isHdr && HdrToneMapper.IsHdrColorSpace(srcColorSpace))
        {
            srcColorSpace = null;
        }

        var alphaType = imgM.HasAlpha ? SKAlphaType.Unpremul : SKAlphaType.Opaque;
        var colorType = isHdr ? SKColorType.RgbaF32 : SKColorType.Rgba8888;

        var srcWidth = imgM.Width;
        var bpp = new SKImageInfo(1, 1, colorType).BytesPerPixel;
        var fitScale = GetPixelBufferFitScale(srcWidth, imgM.Height, bpp);
        if (fitScale < 1)
        {
            var fitW = Math.Max(1u, (uint)(srcWidth * fitScale));
            var fitH = Math.Max(1u, (uint)(imgM.Height * fitScale));
            imgM.Resize(fitW, fitH);
            decodeScale = (double)imgM.Width / srcWidth;
        }

        var info = new SKImageInfo((int)imgM.Width, (int)imgM.Height, colorType, alphaType);
        if (srcColorSpace is not null)
        {
            info = info.WithColorSpace(srcColorSpace);
        }

        using var pixels = imgM.GetPixelsUnsafe();
        NativeMemoryArray<byte> nativeBuffer;

        if (isHdr)
        {
            var hdrBuffer = MagickCodec.ExportHdrPixels(imgM, pixels);
            if (hdrBuffer is null) return null;

            nativeBuffer = hdrBuffer;
        }
        else
        {
            var bytes = pixels.ToByteArray(PixelMapping.RGBA);
            if (bytes is null) return null;

            nativeBuffer = new NativeMemoryArray<byte>(bytes.Length, skipZeroClear: true, addMemoryPressure: true);
            bytes.CopyTo(nativeBuffer.AsSpan());
        }

        fixed (byte* ptr = nativeBuffer)
        {
            var data = SKData.Create(
                (nint)ptr,
                (int)nativeBuffer.Length,
                (addr, ctx) => ((NativeMemoryArray<byte>)ctx!).Dispose(),
                nativeBuffer
            );

            return SKImage.FromPixels(info, data);
        }
    }

    public static unsafe SKImage? FromPixelBuffer(byte* data, int width, int height, int stride,
        SKColorType colorType, SKAlphaType alphaType, SKColorSpace? srcColorSpace)
    {
        if (data == null || width <= 0 || height <= 0 || stride <= 0) return null;
        if (colorType == SKColorType.Unknown) return null;

        var info = new SKImageInfo(width, height, colorType, alphaType);
        if (srcColorSpace is not null)
        {
            info = info.WithColorSpace(srcColorSpace);
        }

        var rowBytes = info.RowBytes;
        var totalBytes = checked((long)rowBytes * height);
        var nativeBuffer = new NativeMemoryArray<byte>(totalBytes, skipZeroClear: true, addMemoryPressure: true);

        var srcSpan = new ReadOnlySpan<byte>(data, stride * (height - 1) + rowBytes);
        var dstSpan = nativeBuffer.AsSpan();
        if (stride == rowBytes)
        {
            srcSpan.Slice(0, (int)totalBytes).CopyTo(dstSpan);
        }
        else
        {
            for (var y = 0; y < height; y++)
            {
                var src = new ReadOnlySpan<byte>(data + (long)y * stride, rowBytes);
                src.CopyTo(dstSpan.Slice(y * rowBytes, rowBytes));
            }
        }
        fixed (byte* ptr = nativeBuffer)
        {
            var skData = SKData.Create(
                (nint)ptr,
                (int)nativeBuffer.Length,
                (addr, ctx) => ((NativeMemoryArray<byte>)ctx!).Dispose(),
                nativeBuffer
            );

            return SKImage.FromPixels(info, skData);
        }
    }

    public static SKBitmap? FromBitmap(Bitmap? abmp)
    {
        if (abmp is null) return null;

        var width = abmp.PixelSize.Width;
        var height = abmp.PixelSize.Height;

        var srcFormat = abmp.Format;
        var srcAlpha = abmp.AlphaFormat;
        var (colorType, alphaType) = MapAvaloniaFormat(srcFormat, srcAlpha);

        if (colorType != SKColorType.Unknown)
        {
            var info = new SKImageInfo(width, height, colorType, alphaType);
            var skBmp = new SKBitmap(info);

            try
            {
                abmp.CopyPixels(
                    new PixelRect(0, 0, width, height),
                    skBmp.GetPixels(),
                    info.RowBytes * height,
                    info.RowBytes);
            }
            catch
            {
                skBmp.Dispose();
                return null;
            }

            return skBmp;
        }

        var dstInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var dstBmp = new SKBitmap(dstInfo);

        try
        {
            using var wbmp = new WriteableBitmap(
                new PixelSize(width, height),
                abmp.Dpi,
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using (var fb = wbmp.Lock())
            {
                abmp.CopyPixels(fb);
            }

            wbmp.CopyPixels(
                new PixelRect(0, 0, width, height),
                dstBmp.GetPixels(),
                dstInfo.RowBytes * height,
                dstInfo.RowBytes);
        }
        catch
        {
            dstBmp.Dispose();
            return null;
        }

        return dstBmp;
    }

    private static (SKColorType ColorType, SKAlphaType AlphaType) MapAvaloniaFormat(
        PixelFormat? format, AlphaFormat? alpha)
    {
        var alphaType = alpha switch
        {
            AlphaFormat.Premul => SKAlphaType.Premul,
            AlphaFormat.Unpremul => SKAlphaType.Unpremul,
            AlphaFormat.Opaque => SKAlphaType.Opaque,
            _ => SKAlphaType.Premul,
        };

        if (format is null) return (SKColorType.Unknown, alphaType);

        var colorType = format.Value switch
        {
            var f when f == PixelFormat.Bgra8888 => SKColorType.Bgra8888,
            var f when f == PixelFormat.Rgba8888 => SKColorType.Rgba8888,
            var f when f == PixelFormat.Rgb565 => SKColorType.Rgb565,
            _ => SKColorType.Unknown,
        };

        return (colorType, alphaType);
    }

    public static SKImage? ToSKImage(SKBitmap? bmp)
    {
        if (bmp.IsDisposed()) return null;

        var img = SKImage.FromBitmap(bmp);
        return img;
    }

    public static SKImage? CopyImage(SKImage? imgSrc)
    {
        if (imgSrc.IsDisposed()) return null;

        var info = new SKImageInfo(imgSrc.Width, imgSrc.Height, imgSrc.ColorType, imgSrc.AlphaType, imgSrc.ColorSpace);
        using var bmp = new SKBitmap(info);
        if (!imgSrc.ReadPixels(info, bmp.GetPixels(), bmp.RowBytes, 0, 0)) return null;

        return ToSKImageNoCopy(bmp);
    }

    public static SKImage? ToSKImageNoCopy(SKBitmap? bmp)
    {
        if (bmp.IsDisposed()) return null;

        bmp.SetImmutable();
        return SKImage.FromBitmap(bmp);
    }

    public static MagickImage? ToMagick(SKBitmap? skBmp)
    {
        using var skImg = ToSKImage(skBmp);
        return ToMagick(skImg);
    }

    public static unsafe MagickImage? ToMagick(SKImage? skImg)
    {
        if (skImg.IsDisposed()) return null;

        var info = new SKImageInfo(skImg.Width, skImg.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var rowBytes = info.Width * info.BytesPerPixel;
        var pixelBuffer = new byte[rowBytes * info.Height];

        fixed (byte* ptr = pixelBuffer)
        {
            if (!skImg.ReadPixels(info, (nint)ptr, rowBytes, 0, 0)) return null;
        }

        var imgM = new MagickImage();
        var settings = new PixelReadSettings((uint)info.Width, (uint)info.Height,
            StorageType.Char, PixelMapping.BGRA);
        imgM.ReadPixels(pixelBuffer, settings);

        return imgM;
    }

    public static WriteableBitmap? ToWritableBitmap(SKImage? img)
    {
        if (img.IsDisposed()) return null;

        using var pixmap = img.PeekPixels();
        return ToWritableBitmap(pixmap);
    }

    public static WriteableBitmap? ToWritableBitmap(SKBitmap? bmp)
    {
        if (bmp.IsDisposed()) return null;

        using var pixmap = bmp.PeekPixels();
        return ToWritableBitmap(pixmap);
    }

    public static unsafe WriteableBitmap? ToWritableBitmap(SKPixmap? pixmap)
    {
        if (pixmap.IsDisposed()) return null;

        var info = pixmap.Info;
        var wbmp = new WriteableBitmap(
            new PixelSize(info.Width, info.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (ILockedFramebuffer fb = wbmp.Lock())
        {
            bool isFastPath = info.ColorType == SKColorType.Bgra8888 &&
                              (info.AlphaType == SKAlphaType.Premul || info.AlphaType == SKAlphaType.Opaque);

            if (isFastPath)
            {
                byte* srcPtr = (byte*)pixmap.GetPixels();
                byte* dstPtr = (byte*)fb.Address;
                long srcRowBytes = pixmap.RowBytes;
                long dstRowBytes = fb.RowBytes;
                long bytesToCopy = info.Width * 4;

                if (srcRowBytes == dstRowBytes)
                {
                    Unsafe.CopyBlock(dstPtr, srcPtr, (uint)(srcRowBytes * info.Height));
                }
                else
                {
                    for (int y = 0; y < info.Height; y++)
                    {
                        Unsafe.CopyBlock(dstPtr, srcPtr, (uint)bytesToCopy);
                        srcPtr += srcRowBytes;
                        dstPtr += dstRowBytes;
                    }
                }
            }
            else
            {
                var dstInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                pixmap.ReadPixels(dstInfo, fb.Address, fb.RowBytes);
            }
        }

        return wbmp;
    }

    public static MemoryStream? ToBitmapV4Stream(SKImage? imgSrc)
    {
        if (imgSrc.IsDisposed()) return null;

        var bmpSrc = SKBitmap.FromImage(imgSrc);
        return ToBitmapV4Stream(bmpSrc);
    }

    public static MemoryStream? ToBitmapV4Stream(SKBitmap? bmpSrc)
    {
        if (bmpSrc.IsDisposed()) return null;

        SKBitmap? converted = null;
        try
        {
            var bmp = bmpSrc;

            if (bmp.ColorType != SKColorType.Bgra8888)
            {
                var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, bmp.AlphaType);
                converted = new SKBitmap(info);
                if (!bmpSrc.CopyTo(converted, SKColorType.Bgra8888)) return null;
                bmp = converted;
            }

            var w = bmp.Width;
            var h = bmp.Height;
            var bmpRowBytes = w * 4;

            const int headerSize = 122;
            long pixelBytesL = (long)bmpRowBytes * h;

            if (pixelBytesL > int.MaxValue - headerSize) return null;

            var pixelBytes = (int)pixelBytesL;
            var fileSize = headerSize + pixelBytes;
            var ms = new MemoryStream(fileSize);

            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                bw.Write((byte)'B');
                bw.Write((byte)'M');
                bw.Write(fileSize);
                bw.Write(0);
                bw.Write(headerSize);

                bw.Write(108);
                bw.Write(w);
                bw.Write(-h);
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write(3);
                bw.Write(pixelBytes);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0);

                bw.Write(0x00FF0000);
                bw.Write(0x0000FF00);
                bw.Write(0x000000FF);
                bw.Write(unchecked((int)0xFF000000));

                bw.Write(0x73524742);
                for (int i = 0; i < 12; i++) bw.Write(0);
            }

            var pixels = bmp.GetPixelSpan();
            var srcRowBytes = bmp.RowBytes;

            if (srcRowBytes == bmpRowBytes)
            {
                ms.Write(pixels);
            }
            else
            {
                for (int y = 0; y < h; y++)
                {
                    ms.Write(pixels.Slice(y * srcRowBytes, bmpRowBytes));
                }
            }

            ms.Position = 0;
            return ms;
        }
        finally
        {
            converted?.Dispose();
        }
    }
}
