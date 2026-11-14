using Avalonia;
using PhotoViewer.Common.Loggers;
using ImageMagick;
using ImageMagick.Formats;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.RegularExpressions;

namespace PhotoViewer.Common.Photoing;

public static partial class MagickCodec
{
    [GeneratedRegex(@"(^data\:(?<type>image\/[a-z\+\-]*);base64,)?(?<data>[a-zA-Z0-9\+\/\=]+)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled, "en-US")]
    private static partial Regex CreateBase64DataUriRegex__();

    private static MagickReadSettings GetPreviewReadSettings__(PhotoReadOptions options,
        MagickReadSettings settings)
    {
        if (options.Width == 0 || options.Height == 0) return settings;

        settings.SetDefines(new JpegReadDefines()
        {
            Size = new MagickGeometry(options.Width, options.Height),
        });

        return settings;
    }

    private static void ReadRawThumbnailSize__(PhotoMetadata meta)
    {
        meta.PreviewWidth = meta.PreviewHeight = 0;
        if (meta.RawThumbnail is null) return;

        try
        {
            using var probeM = new MagickImage();
            probeM.Ping(meta.RawThumbnail.ToReadOnlySpan());

            meta.PreviewWidth = probeM.Width;
            meta.PreviewHeight = probeM.Height;
        }
        catch (Exception ex)
        {
            PhotoTrace.Mark("decode:raw-preview-unreadable", meta.FilePath, ex.Message);
        }
    }

    private static MagickImage? ProcessMagickImage__(MagickImage refImgM,
        PhotoReadOptions options, PhotoMetadata meta, bool requestThumbnail, out MagickColorConversion conversion)
    {
        IMagickImage? thumbM = null;
        conversion = MagickColorConversion.None;

        if (!meta.IsHdr) ApplyIccHdrInfo__(meta, refImgM);

        if (requestThumbnail && meta.ExifProfile != null && options.OnlyLoadNonRawPreview)
        {
            thumbM = meta.ExifProfile.CreateThumbnail();
            if (thumbM != null
                && thumbM.Width >= options.PreviewMinWidth
                && thumbM.Height >= options.PreviewMinHeight)
            {
                if (options.CorrectRotation) thumbM.AutoOrient();

                ApplySizeSettings__(thumbM, options);
            }
            else
            {
                thumbM?.Dispose();
                thumbM = null;
            }
        }

        if (!requestThumbnail || thumbM == null)
        {
            ApplySizeSettings__(refImgM, options);

            if (options.CorrectRotation) refImgM.AutoOrient();

            if (!meta.IsHdr && options.SkipDisplayProfile)
            {
                if (meta.MagickColorProfile is not null)
                {
                    refImgM.TransformColorSpace(meta.MagickColorProfile, ColorProfiles.SRGB);
                    conversion = MagickColorConversion.ToSrgb;
                }
            }
            else if (!meta.IsHdr
                && (Core.Config.EnableAlwaysApplyColorProfile || meta.MagickColorProfile is not null))
            {
                if (GetColorProfileByName(Core.Config.ColorProfile) is { } destIccProfile)
                {
                    refImgM.TransformColorSpace(
                        meta.MagickColorProfile ?? ColorProfiles.SRGB,
                        destIccProfile);
                    conversion = MagickColorConversion.ToDisplayProfile;
                }
            }

            if (refImgM.ColorSpace == ColorSpace.CMYK)
            {
                refImgM.ColorSpace = ColorSpace.sRGB;
            }
        }

        return (MagickImage?)thumbM;
    }

    private static void ApplySizeSettings__(IMagickImage imgM, PhotoReadOptions options)
    {
        if (options.Width > 0 && options.Height > 0)
        {
            if (imgM.BaseWidth > options.Width || imgM.BaseHeight > options.Height)
            {
                imgM.Thumbnail(options.Width, options.Height);
            }
        }
    }

    private static void TransformImage__(IMagickImage imgM, PhotoTransform? transform = null)
    {
        if (transform == null) return;

        if (transform.Flips.HasFlag(FlipOptions.Horizontal))
        {
            imgM.Flop();
        }
        if (transform.Flips.HasFlag(FlipOptions.Vertical))
        {
            imgM.Flip();
        }

        if (transform.Rotation != 0)
        {
            imgM.Rotate(transform.Rotation);
        }

        if (transform.IsColorInverted)
        {
            imgM.Negate(Channels.RGB);
        }
    }

    private static Size GetMaxImageRenderSize__(uint srcWidth, uint srcHeight, uint maxSize)
    {
        var widthScale = 1f;
        var heightScale = 1f;

        if (srcWidth > maxSize)
        {
            widthScale = 1f * maxSize / srcWidth;
        }

        if (srcHeight > maxSize)
        {
            heightScale = 1f * maxSize / srcHeight;
        }

        var scale = Math.Min(widthScale, heightScale);
        var newW = srcWidth * scale;
        var newH = srcHeight * scale;

        return new Size(newW, newH);
    }

    private static int GetDefaultIconFrameIndex__(MagickImageCollection imgC)
    {
        var bestIndex = 0;
        var bestArea = 0UL;
        var bestColorRank = 0;

        for (var i = 0; i < imgC.Count; i++)
        {
            var frame = imgC[i];
            var area = (ulong)frame.Width * frame.Height;

            var colorRank = frame.ColormapSize > 0 ? frame.ColormapSize : int.MaxValue;

            var isLarger = area > bestArea;
            var isDeeper = area == bestArea && colorRank > bestColorRank;
            if (!isLarger && !isDeeper) continue;

            bestIndex = i;
            bestArea = area;
            bestColorRank = colorRank;
        }

        return bestIndex;
    }

    private static MagickFormat ConvertMimeTypeToMagickFormat__(string? mimeType)
    {
        return mimeType switch
        {
            "image/avif" => MagickFormat.Avif,
            "image/bmp" => MagickFormat.Bmp,
            "image/gif" => MagickFormat.Gif,
            "image/tiff" => MagickFormat.Tiff,
            "image/jpeg" => MagickFormat.Jpeg,
            "image/svg+xml" => MagickFormat.Rsvg,
            "image/x-icon" => MagickFormat.Ico,
            "image/x-portable-anymap" => MagickFormat.Pnm,
            "image/x-portable-bitmap" => MagickFormat.Pbm,
            "image/x-portable-graymap" => MagickFormat.Pgm,
            "image/x-portable-pixmap" => MagickFormat.Ppm,
            "image/x-xbitmap" => MagickFormat.Xbm,
            "image/x-xpixmap" => MagickFormat.Xpm,
            "image/x-cmu-raster" => MagickFormat.Ras,
            _ => MagickFormat.Png,
        };
    }

    private static T? GetExifValue__<T>(IExifProfile? profile, ExifTag<T> tag, T? defaultValue = default)
    {
        if (profile == null) return default;

        var exifValue = profile.GetValue(tag);
        if (exifValue == null) return defaultValue;

        return exifValue.Value;
    }

    private static (int Primaries, int TransferCharacteristics)? ParseJxlColorEncoding(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        const int HEADER_SIZE = 64 * 1024;

        try
        {
            using var fs = File.OpenRead(filePath);
            var bufSize = (int)Math.Min(fs.Length, HEADER_SIZE);
            var buf = new byte[bufSize];
            fs.ReadExactly(buf);
            if (buf.Length < 4) return null;

            int csOffset;

            if (buf[0] == 0xFF && buf[1] == 0x0A)
            {
                csOffset = 0;
            }
            else if (buf.AsSpan().IndexOf("ftypjxl "u8) >= 0
                || buf.AsSpan().IndexOf("ftypJXL "u8) >= 0)
            {
                csOffset = -1;
                ReadOnlySpan<byte> jxlcSig = "jxlc"u8;
                ReadOnlySpan<byte> jxlpSig = "jxlp"u8;
                for (int i = 0; i <= buf.Length - 8; i++)
                {
                    if (buf.AsSpan(i, 4).SequenceEqual(jxlcSig))
                    {
                        csOffset = i + 4;
                        break;
                    }
                    if (buf.AsSpan(i, 4).SequenceEqual(jxlpSig))
                    {
                        csOffset = i + 8;
                        break;
                    }
                }
                if (csOffset < 0) return null;
            }
            else
            {
                return null;
            }

            if (csOffset + 2 > buf.Length) return null;
            if (buf[csOffset] != 0xFF || buf[csOffset + 1] != 0x0A) return null;

            int bitPos = (csOffset + 2) * 8;

            uint ReadBits(int n)
            {
                uint result = 0;
                for (int i = 0; i < n; i++)
                {
                    int byteIdx = bitPos >> 3;
                    int bitIdx = bitPos & 7;
                    if (byteIdx >= buf.Length) return result;
                    result |= (uint)((buf[byteIdx] >> bitIdx) & 1) << i;
                    bitPos++;
                }
                return result;
            }

            bool ReadBool() => ReadBits(1) == 1;

            uint ReadU32(int b0, int n0, int b1, int n1, int b2, int n2, int b3, int n3)
            {
                return (int)ReadBits(2) switch
                {
                    0 => (uint)b0 + ReadBits(n0),
                    1 => (uint)b1 + ReadBits(n1),
                    2 => (uint)b2 + ReadBits(n2),
                    _ => (uint)b3 + ReadBits(n3),
                };
            }

            uint ReadEnum() => ReadU32(0, 0, 1, 0, 2, 4, 18, 6);

            void SkipSizeHeader()
            {
                if (ReadBool())
                {
                    ReadBits(5);
                    if (ReadBits(3) == 0) ReadBits(5);
                }
                else
                {
                    ReadU32(1, 9, 1, 13, 1, 18, 1, 30);
                    if (ReadBits(3) == 0) ReadU32(1, 9, 1, 13, 1, 18, 1, 30);
                }
            }

            void SkipCustomXy()
            {
                ReadU32(0, 19, 524288, 19, 1048576, 20, 2097152, 21);
                ReadU32(0, 19, 524288, 19, 1048576, 20, 2097152, 21);
            }

            void SkipPreviewHeader()
            {
                var div8 = ReadBool();
                if (div8)
                {
                    ReadU32(16, 0, 32, 0, 1, 5, 33, 9);
                    var ratio = ReadBits(3);
                    if (ratio == 0) ReadU32(16, 0, 32, 0, 1, 5, 33, 9);
                }
                else
                {
                    ReadU32(1, 6, 65, 8, 321, 10, 1345, 12);
                    var ratio = ReadBits(3);
                    if (ratio == 0) ReadU32(1, 6, 65, 8, 321, 10, 1345, 12);
                }
            }

            void SkipBitDepth()
            {
                if (ReadBool())
                {
                    ReadU32(32, 0, 16, 0, 24, 0, 1, 6);
                    ReadBits(4);
                }
                else
                {
                    ReadU32(8, 0, 10, 0, 12, 0, 1, 6);
                }
            }

            void SkipNameString()
            {
                var nameLen = ReadU32(0, 0, 0, 4, 16, 5, 48, 10);
                ReadBits((int)(nameLen * 8));
            }

            void SkipExtraChannelInfo()
            {
                var type = ReadEnum();
                SkipBitDepth();
                ReadU32(0, 0, 3, 0, 4, 0, 1, 3);
                SkipNameString();
                if (type == 0) ReadBool();
                if (type == 2) ReadBits(16 * 4);
                if (type == 5) ReadU32(1, 0, 0, 2, 3, 4, 19, 8);
            }

            SkipSizeHeader();

            if (ReadBool()) return null;

            var extraFields = ReadBool();
            if (extraFields)
            {
                ReadBits(3);
                if (ReadBool()) SkipSizeHeader();
                if (ReadBool()) SkipPreviewHeader();
                if (ReadBool())
                {
                    ReadU32(100, 0, 1000, 0, 1, 10, 1, 30);
                    ReadU32(1, 0, 1001, 0, 1, 8, 1, 10);
                    ReadU32(0, 0, 0, 3, 0, 16, 0, 32);
                    ReadBool();
                }
            }

            SkipBitDepth();

            ReadBool();

            var numExtraChannels = ReadU32(0, 0, 1, 0, 2, 4, 1, 12);
            if (numExtraChannels != 0)
            {
                for (uint i = 0; i < numExtraChannels; i++)
                {
                    if (ReadBool()) continue;
                    SkipExtraChannelInfo();
                }
            }

            ReadBool();

            if (ReadBool()) return null;
            if (ReadBool()) return null;

            var colorSpace = (int)ReadEnum();

            if (colorSpace == 2) return null;

            bool hasPrimaries = colorSpace is 0 or 3;

            int primaries = -1;
            var whitePoint = (int)ReadEnum();
            if (whitePoint == 2) SkipCustomXy();

            if (hasPrimaries)
            {
                primaries = (int)ReadEnum();
                if (primaries == 2)
                {
                    SkipCustomXy();
                    SkipCustomXy();
                    SkipCustomXy();
                }
            }

            if (ReadBool()) return null;
            var transferFunction = (int)ReadEnum();

            return (primaries, transferFunction);
        }
        catch { }

        return null;
    }

    private static bool DetectGainMap(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;

        const int SCAN_SIZE = 256 * 1024;

        try
        {
            using var fs = File.OpenRead(filePath);
            var bufSize = (int)Math.Min(fs.Length, SCAN_SIZE);
            var buf = new byte[bufSize];
            fs.ReadExactly(buf);

            var span = buf.AsSpan();

            if (span.IndexOf("hdrgm:Version"u8) >= 0) return true;

            if (span.IndexOf("HDRGainMap"u8) >= 0) return true;

            return false;
        }
        catch { }

        return false;
    }

    private static void ApplyIccHdrInfo__(PhotoMetadata meta, IMagickImage img)
    {
        if (img.GetProfile("icc") is not { } profile) return;
        if (ParseCicpFromIcc(profile.ToReadOnlySpan()) is not (var primaries, var transfer)) return;

        meta.HdrTransferFn = transfer switch
        {
            16 => HdrTransferFunction.PQ,
            18 => HdrTransferFunction.HLG,
            _ => meta.HdrTransferFn,
        };

        if (!meta.IsWideGamut && primaries is 9 or 11 or 12)
        {
            meta.IsWideGamut = true;
        }
    }

    private static (int ColorPrimaries, int TransferCharacteristics)? ParseCicpFromIcc(ReadOnlySpan<byte> icc)
    {
        const int TAG_COUNT_OFFSET = 128;
        const int TAG_TABLE_OFFSET = 132;
        const int TAG_ENTRY_SIZE = 12;
        const int CICP_TAG_SIZE = 12;

        if (icc.Length < TAG_TABLE_OFFSET) return null;

        var tagCount = BinaryPrimitives.ReadUInt32BigEndian(icc[TAG_COUNT_OFFSET..]);

        if (tagCount == 0 || tagCount > 256) return null;

        for (var i = 0; i < (int)tagCount; i++)
        {
            var entry = TAG_TABLE_OFFSET + (i * TAG_ENTRY_SIZE);
            if (entry + TAG_ENTRY_SIZE > icc.Length) return null;

            if (!icc.Slice(entry, 4).SequenceEqual("cicp"u8)) continue;

            var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(icc[(entry + 4)..]);
            var dataSize = BinaryPrimitives.ReadUInt32BigEndian(icc[(entry + 8)..]);
            if (dataSize < CICP_TAG_SIZE || dataOffset + CICP_TAG_SIZE > (uint)icc.Length) return null;

            var data = icc.Slice((int)dataOffset, CICP_TAG_SIZE);

            if (!data[..4].SequenceEqual("cicp"u8)) return null;

            return (data[8], data[9]);
        }

        return null;
    }

    private static (int ColorPrimaries, int TransferCharacteristics)? ParseCicpFromIsobmff(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        const int SEARCH_SIZE = 256 * 1024;
        ReadOnlySpan<byte> signature = "colrnclx"u8;

        try
        {
            using var fs = File.OpenRead(filePath);
            var bufSize = (int)Math.Min(fs.Length, SEARCH_SIZE);
            var buf = new byte[bufSize];
            fs.ReadExactly(buf);

            var span = buf.AsSpan();
            for (int i = 0; i <= span.Length - 12; i++)
            {
                if (span.Slice(i, 8).SequenceEqual(signature))
                {
                    var primaries = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(i + 8));
                    var transfer = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(i + 10));
                    return (primaries, transfer);
                }
            }
        }
        catch { }

        return null;
    }

    private static (int ColorPrimaries, int TransferCharacteristics)? ParseCicpFromPng(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        const int SEARCH_SIZE = 64 * 1024;
        ReadOnlySpan<byte> chunkType = "cICP"u8;

        try
        {
            using var fs = File.OpenRead(filePath);

            Span<byte> sig = stackalloc byte[8];
            if (fs.Read(sig) < 8) return null;
            ReadOnlySpan<byte> pngSig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            if (!sig.SequenceEqual(pngSig)) return null;

            var bufSize = (int)Math.Min(fs.Length, SEARCH_SIZE);
            var buf = new byte[bufSize];
            fs.Position = 0;
            fs.ReadExactly(buf);

            var span = buf.AsSpan();
            int pos = 8;
            while (pos + 12 <= span.Length)
            {
                int chunkLen = BinaryPrimitives.ReadInt32BigEndian(span.Slice(pos));
                if (chunkLen < 0) break;

                if (span.Slice(pos + 4, 4).SequenceEqual(chunkType))
                {
                    if (pos + 10 <= span.Length)
                    {
                        return ((int)span[pos + 8], (int)span[pos + 9]);
                    }
                }

                pos += 12 + chunkLen;
            }
        }
        catch { }

        return null;
    }

    private static bool IsPlausiblePeakNits(double nits) => nits is >= 100d and < 10_000d;

    private static double? ParseContentPeakFromIsobmff(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        const int SEARCH_SIZE = 256 * 1024;

        try
        {
            using var fs = File.OpenRead(filePath);
            var bufSize = (int)Math.Min(fs.Length, SEARCH_SIZE);
            var buf = new byte[bufSize];
            fs.ReadExactly(buf);

            var span = buf.AsSpan();

            var clliPos = FindSizedBox(span, "clli"u8, 4);
            if (clliPos >= 0)
            {
                var maxCll = (double)BinaryPrimitives.ReadUInt16BigEndian(span.Slice(clliPos));
                if (IsPlausiblePeakNits(maxCll)) return maxCll;
            }

            var mdcvPos = FindSizedBox(span, "mdcv"u8, 24);
            if (mdcvPos >= 0)
            {
                var maxMastering = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(mdcvPos + 16)) / 10_000d;
                if (IsPlausiblePeakNits(maxMastering)) return maxMastering;
            }
        }
        catch { }

        return null;
    }

    private static int FindSizedBox(ReadOnlySpan<byte> span, ReadOnlySpan<byte> boxType, int bodySize)
    {
        var expectedSize = 8 + bodySize;
        var searchFrom = 0;

        while (searchFrom <= span.Length - 4)
        {
            var found = span.Slice(searchFrom).IndexOf(boxType);
            if (found < 0) return -1;

            var typePos = searchFrom + found;
            var bodyPos = typePos + 4;

            if (typePos >= 4 && bodyPos + bodySize <= span.Length
                && BinaryPrimitives.ReadUInt32BigEndian(span.Slice(typePos - 4)) == expectedSize)
            {
                return bodyPos;
            }

            searchFrom = typePos + 1;
        }

        return -1;
    }

    private static double? ParseContentPeakFromPng(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        const int SEARCH_SIZE = 64 * 1024;

        try
        {
            using var fs = File.OpenRead(filePath);

            Span<byte> sig = stackalloc byte[8];
            if (fs.Read(sig) < 8) return null;
            ReadOnlySpan<byte> pngSig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            if (!sig.SequenceEqual(pngSig)) return null;

            var bufSize = (int)Math.Min(fs.Length, SEARCH_SIZE);
            var buf = new byte[bufSize];
            fs.Position = 0;
            fs.ReadExactly(buf);

            var span = buf.AsSpan();
            double? masteringPeak = null;

            var pos = 8;
            while (pos + 12 <= span.Length)
            {
                var chunkLen = BinaryPrimitives.ReadInt32BigEndian(span.Slice(pos));
                if (chunkLen < 0) break;

                var dataPos = pos + 8;
                var chunkType = span.Slice(pos + 4, 4);

                if ((chunkType.SequenceEqual("cLLi"u8) || chunkType.SequenceEqual("cLLI"u8))
                    && dataPos + 4 <= span.Length)
                {
                    var maxCll = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(dataPos)) / 10_000d;
                    if (IsPlausiblePeakNits(maxCll)) return maxCll;
                }
                else if (chunkType.SequenceEqual("mDCV"u8) && dataPos + 20 <= span.Length)
                {
                    var maxMastering = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(dataPos + 16)) / 10_000d;
                    if (IsPlausiblePeakNits(maxMastering)) masteringPeak = maxMastering;
                }

                pos += 12 + chunkLen;
            }

            return masteringPeak;
        }
        catch { }

        return null;
    }

}
