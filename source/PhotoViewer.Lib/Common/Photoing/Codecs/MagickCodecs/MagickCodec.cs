using Cysharp.Collections;
using PhotoViewer.Common.Loggers;
using ImageMagick;
using ImageMagick.Formats;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public static partial class MagickCodec
{

    public static bool IsInitialized { get; private set; } = false;

    public static void Initialize()
    {
        if (IsInitialized) return;

#if DEBUG
        MagickNET.SetLogEvents(LogEventTypes.Exception);
#endif

        try
        {
            if (!ImageMagick.OpenCL.IsEnabled)
            {
                ImageMagick.OpenCL.IsEnabled = true;
            }
        }
        catch { }

        IsInitialized = true;
    }

    public static MagickReadSettings ParseSettings(PhotoReadOptions? options,
        bool writePurpose, string filePath = "")
    {
        options ??= new();
        var ext = Path.GetExtension(filePath).ToUpperInvariant();

        var settings = new MagickReadSettings
        {
            SyncImageWithExifProfile = true,
            SyncImageWithTiffProperties = true,
        };

        if (options.FrameIndex >= 0)
        {
            settings.FrameIndex = (uint)options.FrameIndex;
            settings.FrameCount = 1;
        }

        if (ext.Equals(".SVG", StringComparison.OrdinalIgnoreCase))
        {
            settings.Format = MagickFormat.Rsvg;
            settings.BackgroundColor = MagickColors.Transparent;
        }
        else if (ext.Equals(".SVGZ", StringComparison.OrdinalIgnoreCase))
        {
            settings.Format = MagickFormat.Svgz;
            settings.BackgroundColor = MagickColors.Transparent;
        }
        else if (ext.Equals(".HEIC", StringComparison.OrdinalIgnoreCase))
        {
            settings.SetDefines(new HeicReadDefines()
            {
                MaxChildrenPerBox = 500,
                MaxItems = 2000,
            });
        }
        else if (ext.Equals(".JP2", StringComparison.OrdinalIgnoreCase))
        {
            settings.SetDefines(new Jp2ReadDefines
            {
                QualityLayers = 100,
            });
        }
        else if (ext.Equals(".TIF", StringComparison.Ordinal)
            || ext.Equals(".TIFF", StringComparison.Ordinal))
        {
            settings.SetDefines(new TiffReadDefines
            {
                IgnoreTags = [
                    "34022",
                    "34025",
                    "34026",

                    "32928",

                    "32932",
                    "34031",
                ],
            });
        }
        else if (ext.Equals(".APNG", StringComparison.Ordinal))
        {
            settings.Format = MagickFormat.APng;
        }

        if (options.Width > 0 && options.Height > 0)
        {
            settings.Width = options.Width;
            settings.Height = options.Height;

            if (ext == ".JPG" || ext == ".JPEG" || ext == ".JPE" || ext == ".JFIF")
            {
                settings.SetDefines(new JpegReadDefines()
                {
                    Size = new MagickGeometry(options.Width, options.Height),
                });
            }
        }

        settings.SetDefines(new BmpReadDefines
        {
            IgnoreFileSize = true,
        });

        settings.SetDefines(new DngReadDefines()
        {
            UseCameraWhiteBalance = true,
            OutputColor = DngOutputColor.SRGB,
            ReadThumbnail = true,
        });

        if (writePurpose)
        {
            if (ext == ".TIF" || ext == ".TIFF")
            {
                settings.SetDefines(new TiffWriteDefines
                {
                    WriteLayers = true,
                    PreserveCompression = true,
                });
            }
            else if (ext == ".WEBP")
            {
                settings.SetDefines(new WebPWriteDefines
                {
                    Lossless = true,
                    ThreadLevel = true,
                    AlphaQuality = 100,
                });
            }
        }

        return settings;
    }

    public static async Task<PhotoMetadata> LoadMetadataAsync(string? filePath,
        PhotoReadOptions? options = null,
        MagickReadSettings? readSettings = null,
        CancellationToken token = default)
    {
        filePath ??= string.Empty;
        var meta = new PhotoMetadata(filePath);

        if (string.IsNullOrWhiteSpace(filePath)) return meta;

        if (SvgCodec.IsSvgFile(filePath))
        {
            return await SvgCodec.LoadMetadataAsync(filePath, token);
        }

        var settings = readSettings ?? ParseSettings(options, false, filePath);

        settings.FrameIndex = null;
        settings.FrameCount = null;
        using var imgC = new MagickImageCollection();

        try
        {
            imgC.Ping(filePath, settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(LoadMetadataAsync)}: {ex.Message}");

            meta = await SkiaCodec.LoadMetadataAsync(filePath, options, token);
            return meta;
        }

        meta = await LoadMetadataAsync(meta, imgC, options, token);

        return meta;
    }

    public static async Task<PhotoMetadata> LoadMetadataAsync(byte[]? bytes,
        PhotoReadOptions? options = null,
        MagickReadSettings? readSettings = null,
        CancellationToken token = default)
    {
        var meta = new PhotoMetadata();
        if (bytes is null || bytes.Length == 0) return meta;

        var settings = readSettings ?? ParseSettings(options, false);

        settings.FrameIndex = null;
        settings.FrameCount = null;
        using var imgC = new MagickImageCollection();

        try
        {
            imgC.Ping(bytes, settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(LoadMetadataAsync)}: {ex.Message}");
        }

        meta = await LoadMetadataAsync(meta, imgC, options, token);

        return meta;
    }

    public static async Task<PhotoMetadata> LoadMetadataAsync(PhotoMetadata meta,
        MagickImageCollection imgC,
        PhotoReadOptions? options = null,
        CancellationToken token = default)
    {
        if (imgC.Count == 0) return meta;

        var isIcon = meta.FileExtension is ".ico" or ".cur";
        meta.DefaultFrameIndex = isIcon ? GetDefaultIconFrameIndex__(imgC) : 0;

        var requestedFrameIndex = options?.FrameIndex ?? 0;
        var frameIndex = meta.DefaultFrameIndex > 0 ? meta.DefaultFrameIndex : requestedFrameIndex;

        if (frameIndex >= imgC.Count) frameIndex = 0;
        else if (frameIndex < 0) frameIndex = imgC.Count - 1;

        meta.FrameCount = (uint)imgC.Count;

        var readingTask = Task.Run(() =>
        {
            try
            {
                var skiaFramesInfo = SkiaCodec.GetFramesMetadata(meta.FilePath);
                meta.CanAnimate = skiaFramesInfo?.Count > 0;
                meta.AnimationLoop = imgC[0].AnimationIterations;

                meta.Frames = imgC.Select((frame, frameIndex) => new FrameMetadata()
                {
                    BackgroundColor = (MagickColor?)frame.BackgroundColor ?? MagickColors.Transparent,
                    Width = frame.Width,
                    Height = frame.Height,
                    X = frame.Page.X,
                    Y = frame.Page.Y,

                    Animation = skiaFramesInfo?.ElementAtOrDefault(frameIndex),
                }).ToImmutableList();
            }
            catch { }

            try
            {
                meta.OriginalWidth = meta.Width = imgC[frameIndex].Page.Width;
                meta.OriginalHeight = meta.Height = imgC[frameIndex].Page.Height;
                meta.Orientation = ToSkiaOrientation(imgC[frameIndex].Orientation);

                var swapDims = meta.Orientation is SKEncodedOrigin.LeftTop
                    or SKEncodedOrigin.RightTop
                    or SKEncodedOrigin.RightBottom
                    or SKEncodedOrigin.LeftBottom;
                if (swapDims)
                {
                    meta.Width = meta.OriginalHeight;
                    meta.Height = meta.OriginalWidth;
                }

                var density = imgC[frameIndex].Density.ChangeUnits(DensityUnit.PixelsPerInch);
                meta.DpiX = density.X;
                meta.DpiY = density.Y;

                meta.HasAlpha = imgC.Any(i => i.HasAlpha);
                meta.ColorSpace = imgC[frameIndex].ColorSpace;

                meta.RawThumbnail = imgC[frameIndex].GetProfile("dng:thumbnail");
                ReadRawThumbnailSize__(meta);
            }
            catch { }

            if (token.IsCancellationRequested) return;

            try
            {
                if (imgC[frameIndex].GetExifProfile() is IExifProfile exifProfile)
                {
                    meta.ExifProfile = exifProfile;

                    meta.ExifRatingPercent = GetExifValue__(exifProfile, ExifTag.RatingPercent);

                    var dt = GetExifValue__(exifProfile, ExifTag.DateTimeOriginal);
                    meta.ExifDateTimeOriginal = BHelper.ConvertDateTime(dt);

                    dt = GetExifValue__(exifProfile, ExifTag.DateTime);
                    meta.ExifDateTime = BHelper.ConvertDateTime(dt);

                    meta.ExifArtist = GetExifValue__(exifProfile, ExifTag.Artist);
                    meta.ExifCopyright = GetExifValue__(exifProfile, ExifTag.Copyright);
                    meta.ExifSoftware = GetExifValue__(exifProfile, ExifTag.Software);
                    meta.ExifImageDescription = GetExifValue__(exifProfile, ExifTag.ImageDescription);
                    meta.ExifModel = GetExifValue__(exifProfile, ExifTag.Model);
                    meta.ExifISOSpeed = (int?)GetExifValue__(exifProfile, ExifTag.ISOSpeed);

                    var rational = GetExifValue__(exifProfile, ExifTag.ExposureTime);
                    meta.ExifExposureTime = rational.Denominator == 0
                        ? null
                        : rational.Numerator / rational.Denominator;

                    rational = GetExifValue__(exifProfile, ExifTag.FNumber);
                    meta.ExifFNumber = rational.Denominator == 0
                        ? null
                        : rational.Numerator / rational.Denominator;

                    rational = GetExifValue__(exifProfile, ExifTag.FocalLength);
                    meta.ExifFocalLength = rational.Denominator == 0
                        ? null
                        : rational.Numerator / rational.Denominator;
                }
            }
            catch { }

            if (token.IsCancellationRequested) return;

            try
            {
                if (imgC[frameIndex].GetColorProfile() is IColorProfile colorProfile)
                {
                    meta.ColorSpace = colorProfile.ColorSpace;
                    if (string.IsNullOrEmpty(colorProfile.Description))
                    {
                        meta.ColorProfileName = colorProfile.ColorSpace.ToString();
                    }
                    else
                    {
                        meta.ColorProfileName = $"{colorProfile.Description} ({colorProfile.ColorSpace})";
                    }

                    meta.MagickColorProfile = colorProfile;
                    meta.SkiaColorSpace = SkiaCodec.CreateIccColorSpace(colorProfile.ToReadOnlySpan());
                }
            }
            catch { }

            DetectHdrInfo(meta, imgC[frameIndex]);

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

    public static async Task<MagickDecoderOutput> DecodeImageAsync(
        PhotoMetadata meta,
        PhotoReadOptions options, MagickReadSettings? settings,
        PhotoTransform? transform, CancellationToken cancelToken)
    {
        var result = new MagickDecoderOutput();

        settings ??= ParseSettings(options, false, meta.FilePath);
        if (options.FrameIndex >= 0)
        {
            settings.FrameIndex = (uint)options.FrameIndex;
            settings.FrameCount = 1;
        }

        if (options.FrameIndex < 0)
        {
            var imgColl = new MagickImageCollection();
            try
            {
                cancelToken.ThrowIfCancellationRequested();
                await imgColl.ReadAsync(meta.FilePath, settings, cancelToken).ConfigureAwait(false);
                cancelToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                imgColl.Dispose();
                cancelToken.ThrowIfCancellationRequested();
                if (!CanRetryContentRead__(ex, settings, meta.FilePath)) throw;

                try
                {
                    imgColl = new MagickImageCollection();
                    using var stream = File.OpenRead(meta.FilePath);
                    await imgColl.ReadAsync(stream, settings, cancelToken).ConfigureAwait(false);
                    cancelToken.ThrowIfCancellationRequested();
                }
                catch (Exception)
                {
                    imgColl.Dispose();
                    cancelToken.ThrowIfCancellationRequested();
                    ExceptionDispatchInfo.Capture(ex).Throw();
                }
            }

            var i = 0;
            foreach (var imgFrameM in imgColl)
            {
                ProcessMagickImage__((MagickImage)imgFrameM, options, meta, false, out var frameConversion);
                if (i == 0) result.ColorConversion = frameConversion;

                if (i == transform?.FrameIndex || transform?.FrameIndex == -1)
                {
                    TransformImage__(imgFrameM, transform);
                }

                i++;
            }

            result.MultiFrames = imgColl;
            return result;
        }

        var imgM = new MagickImage();
        var hasRequestedThumbnail = false;

        if (options.OnlyLoadRawPreview is true)
        {
            try
            {
                if (meta.RawThumbnail != null
                    && meta.IsEmbeddedPreviewLargeEnough(options.PreviewMinWidth, options.PreviewMinHeight))
                {
                    imgM.Read(meta.RawThumbnail.ToReadOnlySpan(), GetPreviewReadSettings__(options, settings));
                    hasRequestedThumbnail = true;
                }
            }
            catch (Exception ex)
            {
                PhotoTrace.Mark("decode:raw-preview-failed", meta.FilePath, ex.Message);
            }

            if (!hasRequestedThumbnail)
            {
                PhotoTrace.Mark("decode:raw-preview-rejected", meta.FilePath,
                    $"preview={meta.PreviewWidth}x{meta.PreviewHeight}, "
                    + $"min={options.PreviewMinWidth}x{options.PreviewMinHeight} -> full decode");
            }
        }

        if (!hasRequestedThumbnail)
        {
            imgM.Dispose();
            try
            {
                cancelToken.ThrowIfCancellationRequested();
                await imgM.ReadAsync(meta.FilePath, settings, cancelToken).ConfigureAwait(false);
                cancelToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                imgM.Dispose();
                cancelToken.ThrowIfCancellationRequested();
                if (!CanRetryContentRead__(ex, settings, meta.FilePath)) throw;

                try
                {
                    imgM = new MagickImage();
                    using var stream = File.OpenRead(meta.FilePath);
                    await imgM.ReadAsync(stream, settings, cancelToken).ConfigureAwait(false);
                    cancelToken.ThrowIfCancellationRequested();
                }
                catch (Exception)
                {
                    imgM.Dispose();
                    cancelToken.ThrowIfCancellationRequested();
                    ExceptionDispatchInfo.Capture(ex).Throw();
                }
            }
        }

        var thumbM = ProcessMagickImage__(imgM, options, meta, true, out var conversion);
        if (thumbM != null)
        {
            imgM.Dispose();
            imgM = thumbM;
        }
        result.ColorConversion = conversion;

        TransformImage__(imgM, transform);
        result.SingleFrame = imgM;

        return result;
    }

    public static async Task<byte[]?> QuickDecodeAsync(string filePath, MagickFormat outputFormat,
        double desiredWidth, double desiredHeight,
        double minSize = 0, double maxSize = double.PositiveInfinity,
        CancellationToken token = default)
    {
        using var imgM = await QuickDecodeAsync(filePath, desiredWidth, desiredHeight, minSize, maxSize, token);
        if (imgM is null) return null;

        return imgM.ToByteArray(outputFormat);
    }

    public static async Task<MagickImage?> QuickDecodeAsync(string filePath,
        double desiredWidth, double desiredHeight,
        double minSize = 0, double maxSize = double.PositiveInfinity,
        CancellationToken token = default)
    {
        var options = new PhotoReadOptions()
        {
            Width = (uint)desiredWidth,
            Height = (uint)desiredHeight,
        };
        var settings = ParseSettings(options, false, filePath);

        try
        {
            using var probeM = new MagickImage();
            probeM.Ping(filePath, settings);

            if (probeM.Width < minSize
                || probeM.Height < minSize
                || probeM.Width > maxSize
                || probeM.Height > maxSize) return null;
        }
        catch { return null; }

        if (token.IsCancellationRequested) return null;

        var imgM = new MagickImage();
        try
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await imgM.ReadAsync(filePath, settings, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                imgM.Dispose();
                token.ThrowIfCancellationRequested();
                if (!CanRetryContentRead__(ex, settings, filePath)) throw;

                imgM = new MagickImage();
                using var stream = File.OpenRead(filePath);
                await imgM.ReadAsync(stream, settings, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();

            return imgM;
        }
        catch
        {
            imgM.Dispose();
            return null;
        }
    }

    private static bool CanRetryContentRead__(Exception error, MagickReadSettings settings, string filePath)
    {
        if (settings.Format != MagickFormat.Unknown
            || error is not (MagickCorruptImageErrorException
                or MagickCoderErrorException or MagickImageErrorException)) return false;

        var extFormatInfo = MagickFormatInfo.Create(filePath);
        if (extFormatInfo is null || extFormatInfo.ModuleFormat == MagickFormat.Dng)
        {
            return false;
        }

        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(error);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            if (current is not (MagickCorruptImageErrorException or MagickCoderErrorException
                or MagickImageErrorException or MagickWarningException)) return false;

            if (current is MagickException magickError)
            {
                foreach (var related in magickError.RelatedExceptions) pending.Push(related);
            }
            if (current.InnerException is { } inner) pending.Push(inner);
        }

        return true;
    }

    public static async Task<Photo?> DecodeBase64Async(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;

        var (MimeType, ByteData) = ConvertBase64ToBytes(base64);
        if (string.IsNullOrEmpty(MimeType)) return null;

        var format = ConvertMimeTypeToMagickFormat__(MimeType);

        SKImage? bmpSrc = null;
        var readSettings = new MagickReadSettings { Format = format };
        if (readSettings.Format == MagickFormat.Rsvg)
        {
            readSettings.BackgroundColor = MagickColors.Transparent;
        }

        using (var imgM = new MagickImage(ByteData, readSettings))
        {
            bmpSrc = SkiaCodec.FromMagick(imgM);
        }

        var meta = await LoadMetadataAsync(ByteData, null, readSettings);
        var photo = new Photo(bmpSrc, meta);

        return photo;
    }

    public static (string MimeType, byte[] ByteData) ConvertBase64ToBytes(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentNullException(nameof(content));
        }

        var base64DataUriRegex = CreateBase64DataUriRegex__();
        var match = base64DataUriRegex.Match(content);

        if (!match.Success)
        {
            throw new FormatException("The base64 content is invalid.");
        }

        var base64Data = match.Groups["data"].Value;
        var byteData = Convert.FromBase64String(base64Data);
        var mimeType = match.Groups["type"].Value.ToLowerInvariant();

        if (mimeType.Length == 0)
        {
            mimeType = "image/png";
        }

        return (mimeType, byteData);
    }

    public static nint GetPixelsPointer(MagickImage? imgM, MagickFormat format = MagickFormat.Bgra,
        int x = 0, int y = 0, int? width = null, int? height = null)
    {
        if (imgM is null) return IntPtr.Zero;

        imgM.Format = format;
        imgM.Depth = 8;

        using var pixels = imgM.GetPixelsUnsafe();
        var w = width ?? (int)imgM.Width;
        var h = height ?? (int)imgM.Height;
        var imgMPtr = pixels.GetAreaPointer(x, y, (uint)w, (uint)h);

        return imgMPtr;
    }

    public static bool CanRead(string srcFilePath)
    {
        return MagickFormatInfo.Create(srcFilePath)?.SupportsReading ?? false;
    }

    public static bool CanWrite(string destFilePath)
    {
        return MagickFormatInfo.Create(destFilePath)?.SupportsWriting ?? false;
    }

    public static async Task SaveAsync(PhotoMetadata meta, string destFilePath, PhotoReadOptions options,
        PhotoTransform? transform = null, uint quality = 100, CancellationToken token = default)
    {
        var destExt = Path.GetExtension(destFilePath);

        try
        {
            if (!CanWrite(destFilePath))
            {
                throw new FormatException("IGE: Unsupported image format.");
            }

            var settings = ParseSettings(options, true, meta.FilePath);
            using var result = await DecodeImageAsync(meta, options with
            {
                CorrectRotation = false,
            }, settings, transform, token);

            if (result.MultiFrames is not null)
            {
                if (result.MultiFrames.Count > 0
                    && result.MultiFrames[0].Format == MagickFormat.Gif
                    && !destExt.Equals(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    result.MultiFrames.Coalesce();
                }

                if (result.MultiFrames.Count > 0
                    && MagickFormatInfo.Create(result.MultiFrames[0].Format)?.ModuleFormat != MagickFormat.Tiff)
                {
                    foreach (var imgM in result.MultiFrames)
                    {
                        imgM.Settings.RemoveDefine(MagickFormat.Tiff, "write-layers");
                    }
                }

                await result.MultiFrames.WriteAsync(destFilePath, token);
            }
            else if (result.SingleFrame is not null)
            {
                result.SingleFrame.Quality = ResolveHdrSafeQuality__(meta, destExt, quality);
                SetPngCompressionLevel(result.SingleFrame, destFilePath, quality);

                if (destExt.Equals(".ICO", StringComparison.OrdinalIgnoreCase))
                {
                    var imgW = result.SingleFrame.Width;
                    var imgH = result.SingleFrame.Height;
                    const int MAX_ICON_SIZE = 256;

                    if (imgW > MAX_ICON_SIZE || imgH > MAX_ICON_SIZE)
                    {
                        var iconSize = GetMaxImageRenderSize__(imgW, imgH, MAX_ICON_SIZE);
                        result.SingleFrame.Scale((uint)iconSize.Width, (uint)iconSize.Height);
                    }
                }

                await result.SingleFrame.WriteAsync(destFilePath, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static uint ResolveHdrSafeQuality__(PhotoMetadata meta, string destExt, uint quality)
    {
        if (!meta.IsHdr || quality >= 100) return quality;

        return destExt.Equals(".JXL", StringComparison.OrdinalIgnoreCase) ? 100 : quality;
    }

    public static void SetPngCompressionLevel(MagickImage imgM, string destFilePath, uint quality)
    {
        if (MagickFormatInfo.Create(destFilePath)?.ModuleFormat != MagickFormat.Png) return;

        const uint MAX_LEVEL = 6;
        var level = Math.Clamp(quality / 10, 1, MAX_LEVEL);

        imgM.Settings.SetDefines(new PngWriteDefines { CompressionLevel = level });
    }

    public static async IAsyncEnumerable<(int FrameNumber, int FrameCount, string FileName)> SaveFramesAsync(string srcFilePath, string destFolder, [EnumeratorCancellation] CancellationToken token = default)
    {
        Directory.CreateDirectory(destFolder);

        var settings = ParseSettings(new PhotoReadOptions()
        {
            FrameIndex = -1,
        }, false, srcFilePath);

        using var imgColl = new MagickImageCollection(srcFilePath, settings);
        var frameCount = imgColl.Count;
        var index = 0;

        foreach (var imgM in imgColl)
        {
            index++;
            imgM.Quality = 100;
            var newFilename = string.Empty;

            try
            {
                newFilename = Path.GetFileNameWithoutExtension(srcFilePath)
                    + " - " + index.ToString($"D{imgColl.Count.ToString().Length}")
                    + ".png";
                var destFilePath = Path.Combine(destFolder, newFilename);

                SetPngCompressionLevel((MagickImage)imgM, destFilePath, imgM.Quality);
                await imgM.WriteAsync(destFilePath, MagickFormat.Png, token);
            }
            catch (OperationCanceledException) { break; }
            catch { }

            yield return (index, frameCount, newFilename);
        }
    }

    public static void DetectHdrInfo(PhotoMetadata meta, IMagickImage? img = null)
    {
        if (img is not null)
        {
            meta.BitsPerChannel = (int)img.Depth;
        }

        {
            var hasGainMap = false;

            if (img is not null)
            {
                if (img.GetProfile("apple_hdrgainmap") is not null)
                {
                    hasGainMap = true;
                }
                else if (img.GetProfile("xmp") is { } xmpProfile)
                {
                    ReadOnlySpan<byte> xmp = xmpProfile.ToReadOnlySpan();
                    if (xmp.IndexOf("hdrgm:"u8) >= 0
                        || xmp.IndexOf("HDRGainMap"u8) >= 0
                        || xmp.IndexOf("GContainer:"u8) >= 0)
                    {
                        hasGainMap = true;
                    }
                }
            }

            if (!hasGainMap && !string.IsNullOrEmpty(meta.FilePath))
            {
                hasGainMap = DetectGainMap(meta.FilePath);
            }

            if (hasGainMap)
            {
                meta.HdrTransferFn = HdrTransferFunction.GainMap;
                return;
            }
        }

        if (meta.SkiaColorSpace is not null)
        {
            if (meta.SkiaColorSpace.GetNumericalTransferFunction(out var transferFn))
            {
                if (transferFn.Equals(SKColorSpaceTransferFn.Pq))
                {
                    meta.HdrTransferFn = HdrTransferFunction.PQ;
                }
                else if (transferFn.Equals(SKColorSpaceTransferFn.Hlg))
                {
                    meta.HdrTransferFn = HdrTransferFunction.HLG;
                }
            }

            if (meta.SkiaColorSpace.ToColorSpaceXyz(out var gamut))
            {
                meta.IsWideGamut = !gamut.Equals(SKColorSpaceXyz.Srgb);
            }
        }

        if (!meta.IsHdr)
        {
            ReadOnlySpan<byte> icc = default;
            if (img is not null && img.GetProfile("icc") is { } iccProfile)
            {
                icc = iccProfile.ToReadOnlySpan();
            }
            else if (meta.MagickColorProfile is { } metaIcc)
            {
                icc = metaIcc.ToReadOnlySpan();
            }

            if (icc.Length > 0 && ParseCicpFromIcc(icc) is (var iccPrimaries, var iccTransfer))
            {
                if (iccTransfer == 16)
                {
                    meta.HdrTransferFn = HdrTransferFunction.PQ;
                }
                else if (iccTransfer == 18)
                {
                    meta.HdrTransferFn = HdrTransferFunction.HLG;
                }

                if (!meta.IsWideGamut && iccPrimaries is 9 or 11 or 12)
                {
                    meta.IsWideGamut = true;
                }
            }

            if (icc.Length > 0)
            {
                if (icc.IndexOf("ST 2084"u8) >= 0
                    || icc.IndexOf("ST2084"u8) >= 0
                    || icc.IndexOf("Perceptual Quantizer"u8) >= 0)
                {
                    meta.HdrTransferFn = HdrTransferFunction.PQ;
                }
                else if (icc.IndexOf("Hybrid Log"u8) >= 0
                    || icc.IndexOf("Hybrid Log-Gamma"u8) >= 0)
                {
                    meta.HdrTransferFn = HdrTransferFunction.HLG;
                }

                if (!meta.IsWideGamut)
                {
                    if (icc.IndexOf("BT.2020"u8) >= 0
                        || icc.IndexOf("Rec. 2020"u8) >= 0
                        || icc.IndexOf("Rec.2020"u8) >= 0
                        || icc.IndexOf("Display P3"u8) >= 0)
                    {
                        meta.IsWideGamut = true;
                    }
                }
            }
        }

        if (!meta.IsHdr
            && meta.FileExtension is ".avif" or ".heif" or ".heic" or ".hif")
        {
            if (ParseCicpFromIsobmff(meta.FilePath) is (var primaries, var transfer))
            {
                if (transfer == 16)
                {
                    meta.HdrTransferFn = HdrTransferFunction.PQ;
                }
                else if (transfer == 18)
                {
                    meta.HdrTransferFn = HdrTransferFunction.HLG;
                }

                if (!meta.IsWideGamut && primaries is 9 or 12)
                {
                    meta.IsWideGamut = true;
                }
            }
        }

        if (!meta.IsHdr && meta.FileExtension is ".png")
        {
            if (ParseCicpFromPng(meta.FilePath) is (var pngPrimaries, var pngTransfer))
            {
                if (pngTransfer == 16)
                {
                    meta.HdrTransferFn = HdrTransferFunction.PQ;
                }
                else if (pngTransfer == 18)
                {
                    meta.HdrTransferFn = HdrTransferFunction.HLG;
                }

                if (!meta.IsWideGamut && pngPrimaries is 9 or 11 or 12)
                {
                    meta.IsWideGamut = true;
                }
            }
        }

        if (!meta.IsHdr && meta.FileExtension is ".jxl")
        {
            if (ParseJxlColorEncoding(meta.FilePath) is (var jxlPrimaries, var jxlTransfer))
            {
                if (jxlTransfer == 16)
                {
                    meta.HdrTransferFn = HdrTransferFunction.PQ;
                }
                else if (jxlTransfer == 18)
                {
                    meta.HdrTransferFn = HdrTransferFunction.HLG;
                }
                else if (jxlTransfer == 8)
                {
                    meta.HdrTransferFn = HdrTransferFunction.Linear;
                }

                if (!meta.IsWideGamut && jxlPrimaries is 9 or 11)
                {
                    meta.IsWideGamut = true;
                }
            }
            else
            {
                if (!meta.IsHdr
                    && meta.BitsPerChannel >= 10
                    && (meta.IsWideGamut
                        || img?.ColorSpace is ImageMagick.ColorSpace.DisplayP3))
                {
                    meta.HdrTransferFn = HdrTransferFunction.Linear;
                }
            }
        }

        if (!meta.IsHdr
            && meta.FileExtension is ".exr" or ".hdr" or ".jxr" or ".wdp")
        {
            meta.HdrTransferFn = HdrTransferFunction.Linear;
            meta.IsWideGamut = true;
        }

        if (!meta.IsHdr && img is not null)
        {
            var cll = img.GetAttribute("exif:ContentLightLevel");
            var mdcv = img.GetAttribute("exif:MasteringDisplayColorVolume");
            if (cll is not null || mdcv is not null)
            {
                meta.HdrTransferFn = HdrTransferFunction.Linear;
            }
        }

        if (!meta.IsWideGamut && img is not null
            && img.ColorSpace is ImageMagick.ColorSpace.DisplayP3)
        {
            meta.IsWideGamut = true;
        }

        if (meta.IsHdr && meta.HdrTransferFn != HdrTransferFunction.GainMap)
        {
            meta.ContentPeakNits = DetectContentPeakNits(meta);
        }
    }

    private static double DetectContentPeakNits(PhotoMetadata meta)
    {
        var peak = meta.FileExtension switch
        {
            ".avif" or ".heif" or ".heic" or ".hif" => ParseContentPeakFromIsobmff(meta.FilePath),
            ".png" => ParseContentPeakFromPng(meta.FilePath),
            _ => null,
        };

        return peak ?? 0d;
    }

    public static SKEncodedOrigin ToSkiaOrientation(Orientation orientation)
    {
        return orientation switch
        {
            Orientation.TopLeft => SKEncodedOrigin.TopLeft,
            Orientation.TopRight => SKEncodedOrigin.TopRight,
            Orientation.BottomRight => SKEncodedOrigin.BottomRight,
            Orientation.BottomLeft => SKEncodedOrigin.BottomLeft,
            Orientation.LeftTop => SKEncodedOrigin.LeftTop,
            Orientation.RightTop => SKEncodedOrigin.RightTop,
            Orientation.RightBottom => SKEncodedOrigin.RightBottom,
            Orientation.LeftBottom => SKEncodedOrigin.LeftBottom,
            _ => SKEncodedOrigin.Default,
        };
    }

    public static NativeMemoryArray<byte>? ExportHdrPixels(MagickImage imgM, IUnsafePixelCollection<float> pixels)
    {
        var hasAlpha = imgM.HasAlpha;
        var area = pixels.GetArea(0, 0, imgM.Width, imgM.Height);
        if (area is null || area.Length == 0) return null;

        var w = (int)imgM.Width;
        var h = (int)imgM.Height;
        var pixelCount = (long)w * h;
        var byteCount = pixelCount * 4 * sizeof(float);

        if (pixelCount <= 0 || byteCount > int.MaxValue) return null;

        var srcChannels = (int)(area.Length / pixelCount);

        var chR = (int)(pixels.GetChannelIndex(PixelChannel.Red) ?? 0);
        var chG = (int)(pixels.GetChannelIndex(PixelChannel.Green) ?? 1);
        var chB = (int)(pixels.GetChannelIndex(PixelChannel.Blue) ?? 2);
        var chA = hasAlpha ? (int)(pixels.GetChannelIndex(PixelChannel.Alpha) ?? 0u) : -1;

        var nativeBuffer = new NativeMemoryArray<byte>(byteCount, skipZeroClear: true, addMemoryPressure: true);
        var dst = MemoryMarshal.Cast<byte, float>(nativeBuffer.AsSpan());

        const float quantumScale = 1f / 65535f;
        var srcIdx = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var dstIdx = i * 4;

            if (srcChannels >= 3)
            {
                dst[dstIdx] = area[srcIdx + chR] * quantumScale;
                dst[dstIdx + 1] = area[srcIdx + chG] * quantumScale;
                dst[dstIdx + 2] = area[srcIdx + chB] * quantumScale;
                dst[dstIdx + 3] = chA >= 0 && srcIdx + chA < area.Length
                    ? area[srcIdx + chA] * quantumScale
                    : 1f;
            }
            else
            {
                var gray = area[srcIdx] * quantumScale;
                dst[dstIdx] = gray;
                dst[dstIdx + 1] = gray;
                dst[dstIdx + 2] = gray;
                dst[dstIdx + 3] = chA >= 0 && srcChannels >= 2
                    ? area[srcIdx + chA] * quantumScale
                    : 1f;
            }

            srcIdx += srcChannels;
        }

        return nativeBuffer;
    }

    public static SKColorSpace? GetDecodedColorSpace(MagickDecoderOutput output, PhotoMetadata meta)
    {
        return output.ColorConversion switch
        {
            MagickColorConversion.ToDisplayProfile => Core.IsDestColorProfileSupported ? Core.DestColorProfile : null,
            MagickColorConversion.ToSrgb => null,
            _ => meta.SkiaColorSpace,
        };
    }

    public static bool HasBakedDisplayProfile(MagickDecoderOutput output, PhotoMetadata meta)
    {
        return output.ColorConversion == MagickColorConversion.ToDisplayProfile
            && GetDecodedColorSpace(output, meta) is null;
    }

    public static ColorProfile? GetColorProfileByName(string name)
    {
        if (name.Equals(nameof(ColorProfileOption.None))) return null;

        var magickProfile = MagickCodec.GetBuiltinColorProfile(name);
        if (magickProfile is not null)
        {
            return magickProfile;
        }

        var profilePath = name;

        if (name.Equals(nameof(ColorProfileOption.CurrentMonitorProfile)))
        {
            profilePath = Core.ColorProfileProvider?.ProfilePath;
        }

        if (Path.Exists(profilePath))
        {
            return new ColorProfile(profilePath);
        }

        return null;
    }

    public static ColorProfile? GetBuiltinColorProfile(string magickProfileName)
    {
        var profile = magickProfileName switch
        {
            nameof(ColorProfiles.AdobeRGB1998) => ColorProfiles.AdobeRGB1998,
            nameof(ColorProfiles.AppleRGB) => ColorProfiles.AppleRGB,
            nameof(ColorProfiles.CoatedFOGRA39) => ColorProfiles.CoatedFOGRA39,
            nameof(ColorProfiles.ColorMatchRGB) => ColorProfiles.ColorMatchRGB,
            nameof(ColorProfiles.SRGB) or nameof(ColorProfileOption.sRGB) => ColorProfiles.SRGB,
            nameof(ColorProfiles.USWebCoatedSWOP) => ColorProfiles.USWebCoatedSWOP,
            _ => null,
        };

        return profile;
    }

    public static bool IsLosslessCompressSupported(string? filePath)
    {
        var opt = new ImageOptimizer()
        {
            OptimalCompression = true,
        };

        return opt.IsSupported(filePath ?? string.Empty);
    }

    public static bool LosslessCompress(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        var fi = new FileInfo(filePath);
        var opt = new ImageOptimizer()
        {
            OptimalCompression = true,
        };

        if (!opt.IsSupported(fi)) throw new NotSupportedException("IGE_002: Unsupported image format.");

        return opt.LosslessCompress(fi);
    }

}
