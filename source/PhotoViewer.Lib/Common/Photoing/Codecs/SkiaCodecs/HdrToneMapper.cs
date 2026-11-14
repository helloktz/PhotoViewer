using PhotoViewer.Common.Extensions;
using SkiaSharp;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public static class HdrToneMapper
{

    private const float FallbackToneCurveWhiteLevel = 6f;

    private const float MinToneCurveWhiteLevel = 1f;
    private const float MaxToneCurveWhiteLevel = 24f;

    private const float Bt2408ReferenceWhiteNits = 203f;

    private const float ScRgbWhiteNits = 80f;

    private const float DiffuseWhiteThreshold = 1.001f;

    private const int PeakBucketsPerUnit = 32;
    private const int PeakBuckets = 32 * PeakBucketsPerUnit;

    private const double PeakPercentile = 0.9999d;

    private const float Lum2020R = 0.2627f;
    private const float Lum2020G = 0.6780f;
    private const float Lum2020B = 0.0593f;

    private const float M00 = 1.6605f, M01 = -0.5876f, M02 = -0.0729f;
    private const float M10 = -0.1245f, M11 = 1.1329f, M12 = -0.0083f;
    private const float M20 = -0.0182f, M21 = -0.1006f, M22 = 1.1188f;

    private const float Lum709R = 0.2126f;
    private const float Lum709G = 0.7152f;
    private const float Lum709B = 0.0722f;

    public static SKImage? ToneMapToSdr(SKImage? source,
        HdrTransferFunction transferFn, HdrToneMappingOptions options, double contentPeakNits = 0d)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (source.IsDisposed()) return null;
        if (options.Mode == HdrToneMappingMode.None) return null;

        if (transferFn == HdrTransferFunction.GainMap) return null;

        SKImage? retaggedSource = null;
        try
        {
            var effectiveSource = source;

            if (!IsHdrColorSpace(source.ColorSpace))
            {
                if (transferFn is HdrTransferFunction.None or HdrTransferFunction.Linear
                    or HdrTransferFunction.ScRgb)
                {
                    var linearCs = SKColorSpace.CreateSrgbLinear();

                    retaggedSource = ReinterpretColorSpace(source, linearCs);
                    if (retaggedSource is null) return null;

                    effectiveSource = retaggedSource;
                }
                else
                {
                    var hdrCs = BuildHdrColorSpace(transferFn, source.ColorSpace);
                    if (hdrCs is null) return null;

                    retaggedSource = ReinterpretColorSpace(source, hdrCs);
                    if (retaggedSource is null) return null;

                    effectiveSource = retaggedSource;
                }
            }

            return ToneMapManual(effectiveSource, transferFn, options, contentPeakNits);
        }
        finally
        {
            retaggedSource?.Dispose();
        }
    }

    public static bool IsHdrColorSpace(SKColorSpace? cs)
    {
        if (cs is null || cs.IsSrgb || cs.GammaIsLinear) return false;
        if (cs.GammaIsCloseToSrgb) return false;
        if (cs.GetNumericalTransferFunction(out _)) return false;
        return true;
    }

    private static unsafe SKImage? ToneMapManual(SKImage source,
        HdrTransferFunction transferFn, HdrToneMappingOptions options, double contentPeakNits)
    {
        var isLinearSrgb = transferFn is HdrTransferFunction.None or HdrTransferFunction.Linear
            or HdrTransferFunction.ScRgb;

        using var linearBmp = LinearizeToFloat(source, isLinearSrgb);
        if (linearBmp is null) return null;

        var (maxSample, measuredPeak) = MeasureLinearPeak(linearBmp);

        if (isLinearSrgb && maxSample <= DiffuseWhiteThreshold) return null;

        var compression = Math.Clamp((float)options.HighlightCompression, 0f, 1f);
        var saturation = Math.Clamp((float)options.Saturation, 0f, 2f);
        var whiteLevel = ComputeToneCurveWhiteLevel(transferFn, options,
            contentPeakNits, measuredPeak, compression);

        var toneCurve = ToneCurve.For(options.Mode, whiteLevel, compression);
        if (toneCurve is null) return null;

        var width = linearBmp.Width;
        var height = linearBmp.Height;

        var outputInfo = new SKImageInfo(width, height,
            SKColorType.RgbaF32, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());
        using var outputBmp = new SKBitmap(outputInfo);

        var srcPtr = (byte*)linearBmp.GetPixels();
        var dstPtr = (byte*)outputBmp.GetPixels();
        var srcRowBytes = linearBmp.RowBytes;
        var dstRowBytes = outputBmp.RowBytes;

        var normScale = ComputeNormScale(transferFn, options);

        if (isLinearSrgb)
        {
            ToneMapPerChannel(srcPtr, srcRowBytes, dstPtr, dstRowBytes,
                width, height, normScale, saturation, toneCurve.Value);
        }
        else
        {
            ToneMapLuminanceBased(srcPtr, srcRowBytes, dstPtr, dstRowBytes,
                width, height, normScale, saturation, toneCurve.Value);
        }

        return ConvertToFinalSrgb(outputBmp);
    }

    private static SKBitmap? LinearizeToFloat(SKImage source, bool isLinearSrgb)
    {
        var targetCs = isLinearSrgb
            ? SKColorSpace.CreateSrgbLinear()
            : SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Linear, SKColorSpaceXyz.Rec2020);

        var linearInfo = new SKImageInfo(source.Width, source.Height,
            SKColorType.RgbaF32, SKAlphaType.Unpremul, targetCs);

        var bitmap = new SKBitmap();
        var isAllocated = bitmap.TryAllocPixels(linearInfo);
        if (!isAllocated)
        {
            bitmap.Dispose();
            return null;
        }

        var isDrawn = SkiaCodec.TryDrawInBands(source, bitmap);
        if (!isDrawn)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    private sealed class PeakScan
    {
        public readonly int[] Histogram = new int[PeakBuckets + 1];
        public float Max;
    }

    private static unsafe (float Max, float Peak) MeasureLinearPeak(SKBitmap linearBmp)
    {
        var basePtr = (nint)linearBmp.GetPixels();
        var rowBytes = linearBmp.RowBytes;
        var width = linearBmp.Width;

        var merged = new int[PeakBuckets + 1];
        var maxSample = 0f;
        var mergeLock = new Lock();

        Parallel.For(0, linearBmp.Height, () => new PeakScan(), (y, _, scan) =>
        {
            var row = (float*)((byte*)basePtr + (long)y * rowBytes);

            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                var v = MathF.Max(row[i], MathF.Max(row[i + 1], row[i + 2]));
                if (v > scan.Max) scan.Max = v;

                var bucket = v <= 0f ? 0 : (int)(v * PeakBucketsPerUnit);
                scan.Histogram[Math.Min(bucket, PeakBuckets)]++;
            }

            return scan;
        },
        scan =>
        {
            lock (mergeLock)
            {
                if (scan.Max > maxSample) maxSample = scan.Max;
                for (var b = 0; b <= PeakBuckets; b++) merged[b] += scan.Histogram[b];
            }
        });

        long total = 0;
        for (var b = 0; b <= PeakBuckets; b++) total += merged[b];

        var allowedAbove = (long)(total * (1d - PeakPercentile));
        long above = 0;
        for (var b = PeakBuckets; b > 0; b--)
        {
            above += merged[b];
            if (above <= allowedAbove) continue;

            var peak = (b + 1) / (float)PeakBucketsPerUnit;
            return (maxSample, MathF.Min(peak, maxSample));
        }

        return (maxSample, maxSample);
    }

    private static float ComputeNormScale(HdrTransferFunction transferFn, HdrToneMappingOptions options)
    {
        var whiteNits = ClampReferenceWhiteNits(options);
        var normScale = EncodingWhiteNits(transferFn) / whiteNits;

        var exposure = (float)options.Exposure;
        if (exposure != 0f)
        {
            normScale *= MathF.Pow(2f, exposure);
        }

        return normScale;
    }

    private static unsafe void ToneMapPerChannel(
        byte* srcPtr, int srcRowBytes, byte* dstPtr, int dstRowBytes,
        int width, int height, float normScale, float saturation,
        ToneCurve toneCurve)
    {
        var applySaturation = MathF.Abs(saturation - 1f) > 1e-4f;

        var srcBase = (nint)srcPtr;
        var dstBase = (nint)dstPtr;
        Parallel.For(0, height, y =>
        {
            var srcRow = (float*)((byte*)srcBase + (long)y * srcRowBytes);
            var dstRow = (float*)((byte*)dstBase + (long)y * dstRowBytes);

            var x = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                for (; x <= width - 4; x += 4)
                {
                    ToneMapPerChannel4(srcRow + x * 4, dstRow + x * 4,
                        normScale, saturation, applySaturation, toneCurve);
                }
            }

            for (; x < width; x++)
            {
                var i = x * 4;
                var r = srcRow[i] * normScale;
                var g = srcRow[i + 1] * normScale;
                var b = srcRow[i + 2] * normScale;
                var a = srcRow[i + 3];

                if (r > 0f || g > 0f || b > 0f)
                {
                    r = toneCurve.Map(r);
                    g = toneCurve.Map(g);
                    b = toneCurve.Map(b);
                }

                if (applySaturation)
                {
                    var lum = Lum709R * r + Lum709G * g + Lum709B * b;
                    r = lum + saturation * (r - lum);
                    g = lum + saturation * (g - lum);
                    b = lum + saturation * (b - lum);
                }

                dstRow[i] = Math.Clamp(r, 0f, 1f);
                dstRow[i + 1] = Math.Clamp(g, 0f, 1f);
                dstRow[i + 2] = Math.Clamp(b, 0f, 1f);
                dstRow[i + 3] = Math.Clamp(a, 0f, 1f);
            }
        });
    }

    private static unsafe void ToneMapLuminanceBased(
        byte* srcPtr, int srcRowBytes, byte* dstPtr, int dstRowBytes,
        int width, int height, float normScale, float saturation,
        ToneCurve toneCurve)
    {
        var applySaturation = MathF.Abs(saturation - 1f) > 1e-4f;

        var srcBase = (nint)srcPtr;
        var dstBase = (nint)dstPtr;
        Parallel.For(0, height, y =>
        {
            var srcRow = (float*)((byte*)srcBase + (long)y * srcRowBytes);
            var dstRow = (float*)((byte*)dstBase + (long)y * dstRowBytes);

            var x = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                for (; x <= width - 4; x += 4)
                {
                    ToneMapLuminanceBased4(srcRow + x * 4, dstRow + x * 4,
                        normScale, saturation, applySaturation, toneCurve);
                }
            }

            for (; x < width; x++)
            {
                var i = x * 4;
                var r = srcRow[i] * normScale;
                var g = srcRow[i + 1] * normScale;
                var b = srcRow[i + 2] * normScale;
                var a = srcRow[i + 3];

                var lum = Lum2020R * r + Lum2020G * g + Lum2020B * b;

                if (lum > 0f)
                {
                    var scale = toneCurve.Map(lum) / lum;
                    r *= scale;
                    g *= scale;
                    b *= scale;
                }
                else
                {
                    r = 0f; g = 0f; b = 0f;
                }

                var sr = M00 * r + M01 * g + M02 * b;
                var sg = M10 * r + M11 * g + M12 * b;
                var sb = M20 * r + M21 * g + M22 * b;

                if (applySaturation)
                {
                    var srgbLum = Lum709R * sr + Lum709G * sg + Lum709B * sb;
                    sr = srgbLum + saturation * (sr - srgbLum);
                    sg = srgbLum + saturation * (sg - srgbLum);
                    sb = srgbLum + saturation * (sb - srgbLum);
                }

                dstRow[i] = Math.Clamp(sr, 0f, 1f);
                dstRow[i + 1] = Math.Clamp(sg, 0f, 1f);
                dstRow[i + 2] = Math.Clamp(sb, 0f, 1f);
                dstRow[i + 3] = Math.Clamp(a, 0f, 1f);
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToneMapPerChannel4(float* src, float* dst,
        float normScale, float saturation, bool applySaturation, in ToneCurve toneCurve)
    {
        LoadPlanes(src, out var r, out var g, out var b, out var a);
        r *= normScale;
        g *= normScale;
        b *= normScale;

        var isRedLit = Vector128.GreaterThan(r, Vector128<float>.Zero);
        var isGreenLit = Vector128.GreaterThan(g, Vector128<float>.Zero);
        var isBlueLit = Vector128.GreaterThan(b, Vector128<float>.Zero);
        var isLit = isRedLit | isGreenLit | isBlueLit;

        var mappedR = toneCurve.Map(r);
        var mappedG = toneCurve.Map(g);
        var mappedB = toneCurve.Map(b);
        r = Vector128.ConditionalSelect(isLit, mappedR, r);
        g = Vector128.ConditionalSelect(isLit, mappedG, g);
        b = Vector128.ConditionalSelect(isLit, mappedB, b);

        if (applySaturation)
        {
            var lum = Lum709R * r + Lum709G * g + Lum709B * b;
            r = lum + saturation * (r - lum);
            g = lum + saturation * (g - lum);
            b = lum + saturation * (b - lum);
        }

        var clampedR = Clamp01(r);
        var clampedG = Clamp01(g);
        var clampedB = Clamp01(b);
        var clampedA = Clamp01(a);
        StorePlanes(dst, clampedR, clampedG, clampedB, clampedA);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ToneMapLuminanceBased4(float* src, float* dst,
        float normScale, float saturation, bool applySaturation, in ToneCurve toneCurve)
    {
        LoadPlanes(src, out var r, out var g, out var b, out var a);
        r *= normScale;
        g *= normScale;
        b *= normScale;

        var lum = Lum2020R * r + Lum2020G * g + Lum2020B * b;

        var isLit = Vector128.GreaterThan(lum, Vector128<float>.Zero);
        var mapped = toneCurve.Map(lum);
        var scale = mapped / lum;
        r = Vector128.ConditionalSelect(isLit, r * scale, Vector128<float>.Zero);
        g = Vector128.ConditionalSelect(isLit, g * scale, Vector128<float>.Zero);
        b = Vector128.ConditionalSelect(isLit, b * scale, Vector128<float>.Zero);

        var sr = M00 * r + M01 * g + M02 * b;
        var sg = M10 * r + M11 * g + M12 * b;
        var sb = M20 * r + M21 * g + M22 * b;

        if (applySaturation)
        {
            var srgbLum = Lum709R * sr + Lum709G * sg + Lum709B * sb;
            sr = srgbLum + saturation * (sr - srgbLum);
            sg = srgbLum + saturation * (sg - srgbLum);
            sb = srgbLum + saturation * (sb - srgbLum);
        }

        var clampedR = Clamp01(sr);
        var clampedG = Clamp01(sg);
        var clampedB = Clamp01(sb);
        var clampedA = Clamp01(a);
        StorePlanes(dst, clampedR, clampedG, clampedB, clampedA);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void LoadPlanes(float* src, out Vector128<float> r, out Vector128<float> g,
        out Vector128<float> b, out Vector128<float> a)
    {
        var p0 = Vector128.Load(src);
        var p1 = Vector128.Load(src + 4);
        var p2 = Vector128.Load(src + 8);
        var p3 = Vector128.Load(src + 12);

        var rb01 = Vector128.UnzipEven(p0, p1);
        var ga01 = Vector128.UnzipOdd(p0, p1);
        var rb23 = Vector128.UnzipEven(p2, p3);
        var ga23 = Vector128.UnzipOdd(p2, p3);

        r = Vector128.UnzipEven(rb01, rb23);
        b = Vector128.UnzipOdd(rb01, rb23);
        g = Vector128.UnzipEven(ga01, ga23);
        a = Vector128.UnzipOdd(ga01, ga23);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void StorePlanes(float* dst, Vector128<float> r, Vector128<float> g,
        Vector128<float> b, Vector128<float> a)
    {
        var rb01 = Vector128.ZipLower(r, b);
        var rb23 = Vector128.ZipUpper(r, b);
        var ga01 = Vector128.ZipLower(g, a);
        var ga23 = Vector128.ZipUpper(g, a);

        var p0 = Vector128.ZipLower(rb01, ga01);
        var p1 = Vector128.ZipUpper(rb01, ga01);
        var p2 = Vector128.ZipLower(rb23, ga23);
        var p3 = Vector128.ZipUpper(rb23, ga23);

        p0.Store(dst);
        p1.Store(dst + 4);
        p2.Store(dst + 8);
        p3.Store(dst + 12);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> Clamp01(Vector128<float> v)
    {
        var isBelow = Vector128.LessThan(v, Vector128<float>.Zero);
        var isAbove = Vector128.GreaterThan(v, Vector128<float>.One);
        var raised = Vector128.ConditionalSelect(isBelow, Vector128<float>.Zero, v);

        return Vector128.ConditionalSelect(isAbove, Vector128<float>.One, raised);
    }

    private static SKImage? ConvertToFinalSrgb(SKBitmap linearBmp)
    {
        using var linearImg = SkiaCodec.ToSKImageNoCopy(linearBmp);
        if (linearImg is null) return null;

        var finalInfo = new SKImageInfo(linearBmp.Width, linearBmp.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var finalBmp = new SKBitmap();
        var isAllocated = finalBmp.TryAllocPixels(finalInfo);
        if (!isAllocated) return null;

        var isDrawn = SkiaCodec.TryDrawInBands(linearImg, finalBmp);
        if (!isDrawn) return null;

        return SkiaCodec.ToSKImageNoCopy(finalBmp);
    }

    private static SKImage? ReinterpretColorSpace(SKImage source, SKColorSpace newColorSpace)
    {
        using var pixmap = source.PeekPixels();
        if (pixmap is null) return null;

        var newInfo = pixmap.Info.WithColorSpace(newColorSpace);
        using var reinterpreted = new SKPixmap(newInfo, pixmap.GetPixels(), pixmap.RowBytes);

        return SKImage.FromPixels(reinterpreted);
    }

    private static SKColorSpace? BuildHdrColorSpace(HdrTransferFunction transferFn, SKColorSpace? sourceCs)
    {
        var gamut = SKColorSpaceXyz.Rec2020;
        if (sourceCs?.ToColorSpaceXyz(out var sourceXyz) == true)
        {
            gamut = sourceXyz;
        }

        return transferFn switch
        {
            HdrTransferFunction.PQ => SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Pq, gamut),
            HdrTransferFunction.HLG => SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Hlg, gamut),
            _ => null,
        };
    }

    private static float ClampReferenceWhiteNits(HdrToneMappingOptions options)
        => Math.Clamp((float)options.ReferenceWhiteNits, 50f, 1000f);

    private static float EncodingWhiteNits(HdrTransferFunction transferFn) => transferFn switch
    {
        HdrTransferFunction.ScRgb => ScRgbWhiteNits,
        _ => Bt2408ReferenceWhiteNits,
    };

    private static float ComputeToneCurveWhiteLevel(HdrTransferFunction transferFn,
        HdrToneMappingOptions options, double contentPeakNits, float measuredPeak, float compression)
    {
        var whiteNits = ClampReferenceWhiteNits(options);

        var peakNits = contentPeakNits > 0d
            ? contentPeakNits
            : measuredPeak * EncodingWhiteNits(transferFn);

        var baseLevel = peakNits > 0d
            ? Math.Clamp((float)(peakNits / whiteNits),
                MinToneCurveWhiteLevel, MaxToneCurveWhiteLevel)
            : FallbackToneCurveWhiteLevel;

        return baseLevel * (1f + 3f * compression);
    }

    private static float ReferenceWhiteToneMap(float v, float whiteLevel)
    {
        if (v <= 0f) return 0f;

        var mapped = v * (1f + v / (whiteLevel * whiteLevel)) / (1f + v);

        return MathF.Min(1f, mapped);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> ReferenceWhiteToneMap(Vector128<float> v, float whiteLevel)
    {
        var one = Vector128<float>.One;
        var mapped = v * (one + v / Vector128.Create(whiteLevel * whiteLevel)) / (one + v);
        var clipped = Vector128.Min(one, mapped);
        var isNotPositive = Vector128.LessThanOrEqual(v, Vector128<float>.Zero);

        return Vector128.ConditionalSelect(isNotPositive, Vector128<float>.Zero, clipped);
    }

    private static float ExtendedReinhardToneMap(float v, float compression)
    {
        if (v <= 0f) return 0f;

        var kneeStart = 0.7f - 0.4f * compression;
        const float maxOut = 1.0f;
        var range = maxOut - kneeStart;

        if (v <= kneeStart) return v;

        float excess = v - kneeStart;
        return kneeStart + range * excess / (excess + range);
    }

    private static float AcesToneMap(float v, float compression)
    {
        if (v <= 0f) return 0f;

        var kneeStart = 0.5f - 0.4f * compression;
        const float maxOut = 1.0f;
        var range = maxOut - kneeStart;

        if (v <= kneeStart) return v;

        float excess = v - kneeStart;
        return kneeStart + range * MathF.Tanh(excess / range);
    }

    private readonly struct ToneCurve
    {
        private readonly HdrToneMappingMode _mode;
        private readonly float _whiteLevel;
        private readonly float _compression;

        private ToneCurve(HdrToneMappingMode mode, float whiteLevel, float compression)
        {
            _mode = mode;
            _whiteLevel = whiteLevel;
            _compression = compression;
        }

        public static ToneCurve? For(HdrToneMappingMode mode, float whiteLevel, float compression) => mode switch
        {
            HdrToneMappingMode.BT2408 or HdrToneMappingMode.Reinhard or HdrToneMappingMode.ACES
                => new ToneCurve(mode, whiteLevel, compression),
            _ => null,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Map(float v) => _mode switch
        {
            HdrToneMappingMode.BT2408 => ReferenceWhiteToneMap(v, _whiteLevel),
            HdrToneMappingMode.Reinhard => ExtendedReinhardToneMap(v, _compression),
            _ => AcesToneMap(v, _compression),
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<float> Map(Vector128<float> v)
        {
            if (_mode == HdrToneMappingMode.BT2408) return ReferenceWhiteToneMap(v, _whiteLevel);

            var lane0 = Map(v.GetElement(0));
            var lane1 = Map(v.GetElement(1));
            var lane2 = Map(v.GetElement(2));
            var lane3 = Map(v.GetElement(3));

            return Vector128.Create(lane0, lane1, lane2, lane3);
        }
    }

}
