using Avalonia;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using ImageGlass.SDK.Plugins;
using ImageMagick;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Plugins;

internal sealed unsafe class NativeCodecProxy : PhDisposable, ICodec
{
    private readonly NativePlugin _plugin;
    private readonly IGCodecApi* _codecApi;
    private readonly PluginFailureManager _failureManager;
    private readonly HashSet<string> _supportedExtensionsSet;
    private readonly HashSet<string> _encodingExtensionsSet;

    internal NativePlugin Plugin => _plugin;

    internal IGCodecApi* CodecApi => _codecApi;

    internal PluginFailureManager FailureManager => _failureManager;

    public string CodecId { get; }

    public string CodecName { get; }

    public int MetadataPriority { get; }

    public int DecodePriority { get; }

    public int EncodePriority { get; }

    public IReadOnlyList<string> DecodingExtensions { get; }

    public bool SupportsEncoding => SupportsStaticRasterEncoding || SupportsMultiFrameEncoding;

    public IReadOnlyList<string> EncodingExtensions { get; }

    public IReadOnlyList<string> DeclaredDecodingExtensions { get; }

    public IReadOnlyList<string> DeclaredEncodingExtensions { get; }

    public bool SupportsMetadata { get; }

    public bool SupportsStaticRasterDecoding { get; }

    public bool SupportsColorProfiles { get; }

    public bool SupportsAnimationDecoding { get; }

    public bool SupportsStaticRasterEncoding { get; }

    public bool SupportsMultiFrameEncoding { get; }

    public NativeCodecProxy(
        NativePlugin plugin,
        IGCodecApi* codecApi,
        CodecPluginCapability capability,
        PluginFailureManager quarantine)
    {
        _plugin = plugin;
        _codecApi = codecApi;
        _failureManager = quarantine;

        CodecId = capability.CodecId;
        CodecName = string.IsNullOrEmpty(capability.CodecName)
            ? $"{plugin.PluginId}/{capability.CodecId}" : capability.CodecName;

        MetadataPriority = capability.MetadataPriority;
        DecodePriority = capability.DecodePriority;
        EncodePriority = capability.EncodePriority;

        SupportsMetadata = capability.SupportsMetadata;
        SupportsColorProfiles = capability.SupportsColorProfiles;
        SupportsStaticRasterDecoding = capability.SupportsStaticRasterDecoding;
        SupportsAnimationDecoding = capability.SupportsAnimationDecoding;
        SupportsStaticRasterEncoding = capability.SupportsStaticRasterEncoding;
        SupportsMultiFrameEncoding = capability.SupportsMultiFrameEncoding;

        var (disabledDecode, disabledEncode) = PluginTrustPolicy.GetExtensionExclusions(plugin.PluginId);

        DeclaredDecodingExtensions = Normalize(capability.DecodingExtensions);
        DeclaredEncodingExtensions = Normalize(capability.EncodingExtensions);
        DecodingExtensions = Filter(DeclaredDecodingExtensions, disabledDecode);
        EncodingExtensions = Filter(DeclaredEncodingExtensions, disabledEncode);

        _supportedExtensionsSet = new HashSet<string>(DecodingExtensions, StringComparer.OrdinalIgnoreCase);
        _encodingExtensionsSet = new HashSet<string>(EncodingExtensions, StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> Normalize(string[] extensions)
    {
        var list = new List<string>(extensions.Length);
        foreach (var ext in extensions)
        {
            var normalized = PluginTrustPolicy.NormalizeExtension(ext);
            if (normalized.Length > 1) list.Add(normalized);
        }
        return list;
    }

    private static List<string> Filter(IReadOnlyList<string> declared, IReadOnlySet<string> disabled)
    {
        if (disabled.Count == 0) return [.. declared];

        var list = new List<string>(declared.Count);
        foreach (var ext in declared)
        {
            if (!disabled.Contains(ext)) list.Add(ext);
        }
        return list;
    }

    public bool CanLoadMetadata(string filePath)
    {
        if (!SupportsMetadata) return false;
        if (_failureManager.IsQuarantined(_plugin.PluginId)) return false;
        if (string.IsNullOrEmpty(filePath)) return false;
        var ext = Path.GetExtension(filePath);
        return _supportedExtensionsSet.Contains(ext);
    }

    public bool CanDecode(PhotoMetadata metadata, CodecSelectionContext context)
    {
        if (!SupportsStaticRasterDecoding) return false;
        if (_failureManager.IsQuarantined(_plugin.PluginId)) return false;
        if (metadata is null || string.IsNullOrEmpty(metadata.FilePath)) return false;
        return _supportedExtensionsSet.Contains(metadata.FileExtension);
    }

    public Task<PhotoMetadata> LoadMetadataAsync(string filePath,
        PhotoReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Factory.StartNew(() => LoadMetadataCore(filePath, cancellationToken),
            cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public Task<CodecDecodeResult> DecodeAsync(PhotoMetadata metadata,
        PhotoReadOptions options,
        CodecSelectionContext context,
        CancellationToken cancellationToken = default)
    {
        var frameIndex = Math.Max(0, options?.FrameIndex ?? 0);

        var maxWidth = (int)Math.Min(int.MaxValue, options?.Width ?? 0);
        var maxHeight = (int)Math.Min(int.MaxValue, options?.Height ?? 0);

        var wantsScaledStill = maxWidth > 0 && maxHeight > 0
            && PluginAbi.HasEntryPoint(_codecApi, &_codecApi->DecodeStaticRasterScaled);

        if (SupportsAnimationDecoding && metadata.CanAnimate && !wantsScaledStill)
        {
            return Task.Factory.StartNew(() => DecodeAnimationCore(metadata, cancellationToken),
                cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        return Task.Factory.StartNew(
            () => DecodeCore(metadata, frameIndex, maxWidth, maxHeight, cancellationToken),
            cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private CodecDecodeResult DecodeAnimationCore(PhotoMetadata metadata, CancellationToken token)
    {
        var animator = NativePluginAnimator.Create(this, metadata, token);

        return new CodecDecodeResult
        {
            CodecId = $"plugin:{_plugin.PluginId}:{CodecId}",
            ContentKind = CodecContentKind.Animation,
            Size = new Size(metadata.Width, metadata.Height),
            Animator = animator,
            IsHdr = metadata.IsHdr,
            HasEmbeddedColorProfile = SupportsColorProfiles && metadata.SkiaColorSpace is not null,
        };
    }

    private PhotoMetadata LoadMetadataCore(string filePath, CancellationToken token)
    {
        var meta = new PhotoMetadata(filePath);
        if (!PluginAbi.HasEntryPoint(_codecApi, &_codecApi->LoadMetadata)) return meta;

        if (!_plugin.LiveToken.TryEnter()) return meta;

        var cancelHandle = PluginHostApiTable.RegisterCancellation(token);
        try
        {
            IGImageInfo info = default;
            IGStatus status;
            try
            {
                fixed (char* pPath = filePath)
                {
                    var pathRef = new IGStringRef { Data = pPath, Length = filePath.Length };
                    status = _codecApi->LoadMetadata(pathRef, &info, (void*)cancelHandle);
                }
            }
            catch (Exception ex)
            {
                _failureManager.RecordSoftFailure(_plugin.PluginId,
                    $"managed exception during LoadMetadata: {ex.Message}");
                return meta;
            }

            if (status == IGStatus.Canceled)
            {
                token.ThrowIfCancellationRequested();
            }
            if (status == IGStatus.OK)
            {
                if (info.Width > 0) meta.Width = (uint)info.Width;
                if (info.Height > 0) meta.Height = (uint)info.Height;
                if (info.Width > 0) meta.OriginalWidth = (uint)info.Width;
                if (info.Height > 0) meta.OriginalHeight = (uint)info.Height;
                meta.HasAlpha = info.HasAlpha != 0;
                meta.FrameCount = (uint)Math.Max(1, info.FrameCount);
                meta.HdrTransferFn = MapHdrTransferFn(info.HdrTransferFn);
                meta.BitsPerChannel = MapBitsPerChannel((IGPixelFormat)info.PixelFormat);

                meta.CanAnimate = SupportsAnimationDecoding && info.FrameCount > 1;

                ApplyColorProfile(meta, in info);
            }
        }
        finally
        {
            PluginHostApiTable.ReleaseCancellation(cancelHandle);
            _plugin.LiveToken.Exit();
        }
        return meta;
    }

    private CodecDecodeResult DecodeCore(PhotoMetadata metadata, int frameIndex,
        int maxWidth, int maxHeight, CancellationToken token)
    {
        if (!PluginAbi.HasEntryPoint(_codecApi, &_codecApi->DecodeStaticRaster)
            || !PluginAbi.HasEntryPoint(_codecApi, &_codecApi->FreePixelBuffer))
        {
            throw new NotSupportedException($"Native codec '{CodecId}' does not support static-raster decode.");
        }

        if (!_plugin.LiveToken.TryEnter())
        {
            throw new InvalidDataException($"IGE: Native codec '{CodecId}' was unloaded.");
        }

        var cancelHandle = PluginHostApiTable.RegisterCancellation(token);
        var filePath = metadata.FilePath;
        IGPixelBuffer buffer = default;
        var bufferOwned = false;
        var ownershipTransferred = false;

        try
        {
            var wantsScaled = maxWidth > 0 && maxHeight > 0
                && PluginAbi.HasEntryPoint(_codecApi, &_codecApi->DecodeStaticRasterScaled);
            var isScaled = false;

            IGStatus status;
            try
            {
                fixed (char* pPath = filePath)
                {
                    var pathRef = new IGStringRef { Data = pPath, Length = filePath.Length };

                    if (wantsScaled)
                    {
                        status = _codecApi->DecodeStaticRasterScaled(pathRef, frameIndex,
                            maxWidth, maxHeight, &buffer, (void*)cancelHandle);
                        isScaled = status == IGStatus.OK;
                    }
                    else
                    {
                        status = IGStatus.Unsupported;
                    }

                    if (!isScaled && status is IGStatus.Unsupported or IGStatus.NotImplemented)
                    {
                        status = _codecApi->DecodeStaticRaster(pathRef, frameIndex, &buffer,
                            (void*)cancelHandle);
                    }
                }
            }
            catch (Exception ex)
            {
                _failureManager.RecordSoftFailure(_plugin.PluginId,
                    $"managed exception during DecodeStaticRaster: {ex.Message}");
                throw new InvalidDataException(
                    $"IGE: Native codec '{CodecId}' threw during decode of '{filePath}'.", ex);
            }

            if (status == IGStatus.Canceled)
            {
                token.ThrowIfCancellationRequested();
            }
            if (status != IGStatus.OK)
            {
                throw new InvalidDataException(
                    $"IGE: Native codec '{CodecId}' returned status {status} for '{filePath}'.");
            }
            bufferOwned = true;

            var knownAlpha = metadata.FrameCount > 0 ? metadata.HasAlpha : (bool?)null;
            var image = WrapPluginBufferAsImage(in buffer, metadata.SkiaColorSpace, knownAlpha);
            ownershipTransferred = true;

            if (!isScaled)
            {
                if (metadata.Width == 0) metadata.Width = (uint)buffer.Width;
                if (metadata.Height == 0) metadata.Height = (uint)buffer.Height;
                if (metadata.OriginalWidth == 0) metadata.OriginalWidth = (uint)buffer.Width;
                if (metadata.OriginalHeight == 0) metadata.OriginalHeight = (uint)buffer.Height;
            }

            return new CodecDecodeResult
            {
                CodecId = $"plugin:{_plugin.PluginId}:{CodecId}",
                ContentKind = CodecContentKind.StaticRaster,
                Size = new Size(buffer.Width, buffer.Height),
                SingleFrame = image,
                IsHdr = metadata.IsHdr,
                HasEmbeddedColorProfile = SupportsColorProfiles && metadata.SkiaColorSpace is not null,
            };
        }
        finally
        {
            if (bufferOwned && !ownershipTransferred)
            {
                try
                {
                    IGPixelBuffer localBuf = buffer;
                    _codecApi->FreePixelBuffer(&localBuf);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NativeCodecProxy] FreePixelBuffer threw: {ex.Message}");
                }
            }
            PluginHostApiTable.ReleaseCancellation(cancelHandle);
            _plugin.LiveToken.Exit();
        }
    }

    public bool CanEncode(string destFilePath, CodecEncodeContext context)
    {
        if (!SupportsStaticRasterEncoding) return false;
        if (_failureManager.IsQuarantined(_plugin.PluginId)) return false;
        if (string.IsNullOrEmpty(destFilePath)) return false;
        return _encodingExtensionsSet.Contains(Path.GetExtension(destFilePath));
    }

    public bool CanEncodeMultiFrame(string destFilePath, CodecEncodeContext context)
        => SupportsMultiFrameEncoding && CanEncode(destFilePath, context);

    public Task<CodecEncodeResult> EncodeAsync(CodecEncodeRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.Factory.StartNew(() => EncodeCore(request, cancellationToken),
            cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public Task<CodecEncodeResult> EncodeMultiFrameAsync(CodecMultiFrameEncodeRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.Factory.StartNew(() => EncodeMultiFrameCore(request, cancellationToken),
            cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static void ReadPixelsInto(SKImage image, ref byte* pixels, ref nuint capacity,
        out IGPixelBuffer buffer, bool keepPrecision)
    {
        var (colorType, pixelFormat, bytesPerPixel) = keepPrecision
            ? ResolveEncodeFormat(image.ColorType)
            : (SKColorType.Bgra8888, IGPixelFormat.Bgra8Unorm, 4);

        var info = keepPrecision
            ? new SKImageInfo(image.Width, image.Height, colorType, SKAlphaType.Unpremul, image.ColorSpace)
            : new SKImageInfo(image.Width, image.Height, colorType, SKAlphaType.Unpremul);

        var stride = checked(info.Width * bytesPerPixel);
        var byteCount = (long)stride * info.Height;

        if (byteCount > int.MaxValue) throw new InvalidDataException("IGE: frame too large to encode.");

        if (capacity < (nuint)byteCount)
        {
            if (pixels != null) System.Runtime.InteropServices.NativeMemory.Free(pixels);
            pixels = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)byteCount);
            capacity = (nuint)byteCount;
        }

        if (!image.ReadPixels(info, (nint)pixels, stride, 0, 0))
        {
            throw new InvalidDataException("IGE: could not read the source pixels.");
        }

        buffer = new IGPixelBuffer
        {
            Data = pixels,
            Width = info.Width,
            Height = info.Height,
            Stride = stride,
            PixelFormat = (int)pixelFormat,
            ReleaseContext = HOST_OWNED_BUFFER,
        };
    }

    private static (SKColorType ColorType, IGPixelFormat PixelFormat, int BytesPerPixel)
        ResolveEncodeFormat(SKColorType sourceColorType) => sourceColorType switch
        {
            SKColorType.RgbaF16 or SKColorType.RgbaF16Clamped or SKColorType.RgbaF32
                => (SKColorType.RgbaF16, IGPixelFormat.RgbaFloat16, 8),

            SKColorType.Rgba16161616 or SKColorType.Rgba1010102 or SKColorType.Bgra1010102
                or SKColorType.Rgb101010x or SKColorType.Bgr101010x
                => (SKColorType.Rgba16161616, IGPixelFormat.Rgba16Unorm, 8),

            _ => (SKColorType.Bgra8888, IGPixelFormat.Bgra8Unorm, 4),
        };

    private static bool NeedsHighPrecision(SKImage image)
    {
        return ResolveEncodeFormat(image.ColorType).BytesPerPixel > 4;
    }

    private static readonly void* HOST_OWNED_BUFFER = (void*)1;

    private static IGEncodeOptions BuildOptions(uint quality, SKImage source, string srcPath,
        char* pSrc, byte[]? icc, byte* pIcc)
    {
        return new IGEncodeOptions
        {
            StructSize = sizeof(IGEncodeOptions),
            Quality = (int)Math.Clamp(quality, 1u, 100u),
            Lossless = quality >= 100 ? 1 : 0,
            PreserveAlpha = source.AlphaType != SKAlphaType.Opaque ? 1 : 0,
            SourceFilePath = new IGStringRef { Data = pSrc, Length = srcPath.Length },
            IccProfileData = pIcc,
            IccProfileSize = icc?.Length ?? 0,
        };
    }

    private CodecEncodeResult EncodeCore(CodecEncodeRequest request, CancellationToken token)
    {
        if (!CanEncode(request.DestFilePath, CodecEncodeContext.Default)
            || !PluginAbi.HasEntryPoint(_codecApi, &_codecApi->EncodeStaticRaster))
        {
            return new CodecEncodeResult(false, true);
        }
        if (!_plugin.LiveToken.TryEnter()) return new CodecEncodeResult(false, true);

        var cancelHandle = PluginHostApiTable.RegisterCancellation(token);
        var srcPath = request.SourceFilePath ?? string.Empty;
        var icc = request.SourceIccProfile;
        byte* pixels = null;
        nuint capacity = 0;

        try
        {
            var keepPrecision = NeedsHighPrecision(request.Source);

            while (true)
            {
                ReadPixelsInto(request.Source, ref pixels, ref capacity, out var buffer, keepPrecision);
                token.ThrowIfCancellationRequested();

                IGStatus status;
                fixed (char* pDest = request.DestFilePath)
                fixed (char* pSrc = srcPath)
                fixed (byte* pIcc = icc)
                {
                    var opts = BuildOptions(request.Quality, request.Source, srcPath, pSrc, icc, pIcc);
                    var destRef = new IGStringRef { Data = pDest, Length = request.DestFilePath.Length };

                    try
                    {
                        status = _codecApi->EncodeStaticRaster(destRef, &buffer, &opts, (void*)cancelHandle);
                    }
                    catch (Exception ex)
                    {
                        _failureManager.RecordSoftFailure(_plugin.PluginId,
                            $"managed exception during EncodeStaticRaster: {ex.Message}");
                        return new CodecEncodeResult(false, false, ex.Message);
                    }
                }

                if (keepPrecision && status is IGStatus.Unsupported or IGStatus.NotImplemented)
                {
                    keepPrecision = false;
                    continue;
                }

                return MapEncodeStatus(status, token);
            }
        }
        finally
        {
            if (pixels != null) System.Runtime.InteropServices.NativeMemory.Free(pixels);
            PluginHostApiTable.ReleaseCancellation(cancelHandle);
            _plugin.LiveToken.Exit();
        }
    }

    private CodecEncodeResult EncodeMultiFrameCore(CodecMultiFrameEncodeRequest request, CancellationToken token)
    {
        if (!CanEncodeMultiFrame(request.DestFilePath, CodecEncodeContext.Default)
            || !PluginAbi.HasEntryPoint(_codecApi, &_codecApi->BeginEncodeMultiFrame)
            || !PluginAbi.HasEntryPoint(_codecApi, &_codecApi->EncodeFrame)
            || !PluginAbi.HasEntryPoint(_codecApi, &_codecApi->EndEncodeMultiFrame))
        {
            return new CodecEncodeResult(false, true);
        }
        if (!_plugin.LiveToken.TryEnter()) return new CodecEncodeResult(false, true);

        var cancelHandle = PluginHostApiTable.RegisterCancellation(token);
        var srcPath = request.SourceFilePath ?? string.Empty;
        var icc = request.SourceIccProfile;
        void* session = null;
        var committed = false;
        byte* pixels = null;
        nuint capacity = 0;

        try
        {
            IGStatus status;
            using (var firstFrame = FetchFrame(request, 0, token))
            {
                if (firstFrame is null) return new CodecEncodeResult(false, false, "frame 0 unavailable");

                fixed (char* pDest = request.DestFilePath)
                fixed (char* pSrc = srcPath)
                fixed (byte* pIcc = icc)
                {
                    var info = new IGMultiFrameEncodeInfo
                    {
                        StructSize = sizeof(IGMultiFrameEncodeInfo),
                        FrameCount = request.FrameCount,
                        IsAnimated = request.IsAnimated ? 1 : 0,
                        LoopCount = request.LoopCount,
                        CanvasWidth = 0,
                        CanvasHeight = 0,
                    };
                    var opts = BuildOptions(request.Quality, firstFrame.Frame.Image, srcPath, pSrc, icc, pIcc);
                    var destRef = new IGStringRef { Data = pDest, Length = request.DestFilePath.Length };

                    status = _codecApi->BeginEncodeMultiFrame(destRef, &info, &opts, &session, (void*)cancelHandle);
                }

                if (status is IGStatus.Unsupported or IGStatus.NotImplemented)
                {
                    session = null;
                    return new CodecEncodeResult(false, true);
                }
                if (status != IGStatus.OK || session == null)
                {
                    session = null;
                    return new CodecEncodeResult(false, false, $"BeginEncodeMultiFrame returned {status}");
                }

                status = WriteFrame(firstFrame.Frame, 0, ref pixels, ref capacity, session, cancelHandle);
            }

            if (status != IGStatus.OK) return MapEncodeStatus(status, token);

            for (var i = 1; i < request.FrameCount; i++)
            {
                token.ThrowIfCancellationRequested();

                using var frame = FetchFrame(request, i, token);
                if (frame is null) return new CodecEncodeResult(false, false, $"frame {i} unavailable");

                status = WriteFrame(frame.Frame, i, ref pixels, ref capacity, session, cancelHandle);
                if (status != IGStatus.OK) return MapEncodeStatus(status, token);
            }

            status = _codecApi->EndEncodeMultiFrame(session, 1, (void*)cancelHandle);
            committed = true;
            return MapEncodeStatus(status, token);
        }
        finally
        {
            if (session != null && !committed)
            {
                try { _codecApi->EndEncodeMultiFrame(session, 0, null); }
                catch (Exception ex) { Debug.WriteLine($"[NativeCodecProxy] abort threw: {ex.Message}"); }
            }
            if (pixels != null) System.Runtime.InteropServices.NativeMemory.Free(pixels);
            PluginHostApiTable.ReleaseCancellation(cancelHandle);
            _plugin.LiveToken.Exit();
        }
    }

    private IGStatus WriteFrame(CodecEncodeFrame frame, int index, ref byte* pixels, ref nuint capacity,
        void* session, nint cancelHandle)
    {
        ReadPixelsInto(frame.Image, ref pixels, ref capacity, out var buffer, keepPrecision: false);

        var frameInfo = new IGEncodeFrameInfo
        {
            StructSize = sizeof(IGEncodeFrameInfo),
            FrameIndex = index,
            DurationMs = frame.DurationMs,
            HasAlpha = frame.Image.AlphaType != SKAlphaType.Opaque ? 1 : 0,
        };

        try
        {
            return _codecApi->EncodeFrame(session, &buffer, &frameInfo, (void*)cancelHandle);
        }
        catch (Exception ex)
        {
            _failureManager.RecordSoftFailure(_plugin.PluginId,
                $"managed exception during EncodeFrame: {ex.Message}");
            return IGStatus.Internal;
        }
    }

    private static OwnedFrame? FetchFrame(CodecMultiFrameEncodeRequest request, int index, CancellationToken token)
    {
        var frame = request.GetFrameAsync(index, token).GetAwaiter().GetResult();
        return frame is null || frame.Image.IsDisposed() ? null : new OwnedFrame(frame);
    }

    private static CodecEncodeResult MapEncodeStatus(IGStatus status, CancellationToken token)
    {
        if (status == IGStatus.Canceled) token.ThrowIfCancellationRequested();
        if (status is IGStatus.Unsupported or IGStatus.NotImplemented) return new CodecEncodeResult(false, true);
        if (status != IGStatus.OK) return new CodecEncodeResult(false, false, $"codec returned {status}");
        return new CodecEncodeResult(true, false);
    }

    private sealed class OwnedFrame(CodecEncodeFrame frame) : IDisposable
    {
        public CodecEncodeFrame Frame { get; } = frame;

        public void Dispose()
        {
            if (Frame.OwnsImage) Frame.Image.Dispose();
        }
    }

    internal SKImage WrapPluginBufferAsImage(in IGPixelBuffer buffer, SKColorSpace? srcColorSpace,
        bool? hasAlpha = null)
    {
        if (buffer.Data == null || buffer.Width <= 0 || buffer.Height <= 0)
        {
            throw new InvalidDataException(
                $"IGE: Native codec '{CodecId}' returned an invalid pixel buffer.");
        }

        var (colorType, alphaType) = MapPixelFormat((IGPixelFormat)buffer.PixelFormat, hasAlpha);
        if (colorType == SKColorType.Unknown)
        {
            throw new InvalidDataException(
                $"IGE: Native codec '{CodecId}' returned an unsupported pixel format ({buffer.PixelFormat}).");
        }

        var info = new SKImageInfo(buffer.Width, buffer.Height, colorType, alphaType);
        if (srcColorSpace is not null)
        {
            info = info.WithColorSpace(srcColorSpace);
        }

        var carrier = new PluginPixelBufferRelease
        {
            CodecApiPtr = (nint)_codecApi,
            Buffer = buffer,
            PluginId = _plugin.PluginId,
            LiveToken = _plugin.LiveToken,
        };

        var byteCount = checked(buffer.Stride * buffer.Height);
        carrier.TrackNativeMemory(byteCount);

        SKData? data = null;
        SKImage? image;
        try
        {
            data = SKData.Create((nint)buffer.Data, byteCount,
                PluginPixelBufferRelease.ReleaseData, carrier);

            image = SKImage.FromPixels(info, data, buffer.Stride);
        }
        catch
        {
            data?.Dispose();
            carrier.ReleaseFromHost();
            throw;
        }

        if (image is null)
        {
            data?.Dispose();
            carrier.ReleaseFromHost();
            throw new InvalidDataException(
                $"IGE: Native codec '{CodecId}' returned an unsupported pixel buffer layout.");
        }

        return image;
    }

    internal static int MapBitsPerChannel(IGPixelFormat format) => format switch
    {
        IGPixelFormat.Rgba16Unorm or IGPixelFormat.RgbaFloat16 => 16,
        _ => 8,
    };

    internal static (SKColorType ColorType, SKAlphaType AlphaType) MapPixelFormat(IGPixelFormat format,
        bool? hasAlpha = null)
    {
        var alphaType = hasAlpha == false ? SKAlphaType.Opaque : SKAlphaType.Unpremul;

        return format switch
        {
            IGPixelFormat.Bgra8Unorm => (SKColorType.Bgra8888, alphaType),
            IGPixelFormat.Rgba8Unorm => (SKColorType.Rgba8888, alphaType),
            IGPixelFormat.Rgba16Unorm => (SKColorType.Rgba16161616, alphaType),
            IGPixelFormat.RgbaFloat16 => (SKColorType.RgbaF16, alphaType),
            _ => (SKColorType.Unknown, SKAlphaType.Unknown),
        };
    }

    private static HdrTransferFunction MapHdrTransferFn(int value)
    {
        return (IGHdrTransferFn)value switch
        {
            IGHdrTransferFn.PQ => HdrTransferFunction.PQ,
            IGHdrTransferFn.HLG => HdrTransferFunction.HLG,
            IGHdrTransferFn.GainMap => HdrTransferFunction.GainMap,
            IGHdrTransferFn.Linear => HdrTransferFunction.Linear,
            IGHdrTransferFn.ScRgb => HdrTransferFunction.ScRgb,
            _ => HdrTransferFunction.None,
        };
    }

    private static void ApplyColorProfile(PhotoMetadata meta, in IGImageInfo info)
    {
        SKColorSpace? skiaCs = null;
        if (info.IccProfileData != null && info.IccProfileSize > 0)
        {
            try
            {
                var iccSpan = new ReadOnlySpan<byte>(info.IccProfileData, info.IccProfileSize);
                skiaCs = SkiaCodec.CreateIccColorSpace(iccSpan);

                var bytes = iccSpan.ToArray();
                var photoProfile = new PhotoColorProfile(bytes);
                meta.ColorProfileName = photoProfile.GetIccDescription();
                if (photoProfile.ProfileData is not null)
                {
                    meta.MagickColorProfile = new ColorProfile(photoProfile.ProfileData);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NativeCodecProxy] ICC profile parse failed: {ex.Message}");
            }
        }

        skiaCs ??= MapColorSpace((IGColorSpace)info.ColorSpace);

        if (skiaCs is not null)
        {
            meta.SkiaColorSpace?.Dispose();
            meta.SkiaColorSpace = skiaCs;
        }

        meta.ColorSpace = skiaCs is not null && skiaCs.IsSrgb
            ? ImageMagick.ColorSpace.sRGB
            : ImageMagick.ColorSpace.Undefined;
    }

    private static SKColorSpace? MapColorSpace(IGColorSpace cs)
    {
        return cs switch
        {
            IGColorSpace.Srgb => SKColorSpace.CreateSrgb(),
            IGColorSpace.LinearSrgb => SKColorSpace.CreateSrgbLinear(),
            IGColorSpace.DisplayP3 => SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3),
            IGColorSpace.AdobeRgb => SKColorSpace.CreateRgb(SKColorSpaceTransferFn.TwoDotTwo, SKColorSpaceXyz.AdobeRgb),
            IGColorSpace.Rec2020 => SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Rec2020, SKColorSpaceXyz.Rec2020),
            IGColorSpace.Rec2020Linear => SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Linear, SKColorSpaceXyz.Rec2020),
            _ => null,
        };
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
    }
}
