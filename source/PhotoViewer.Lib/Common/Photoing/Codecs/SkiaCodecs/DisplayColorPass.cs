using PhotoViewer.Common.Extensions;
using SkiaSharp;

namespace PhotoViewer.Common.Photoing;

public sealed record DisplayColorPass(bool IsHdr, HdrTransferFunction TransferFn, double ContentPeakNits,
    HdrToneMappingOptions? Options, SKColorSpace? DestProfile)
{

    public bool IsNoOp => !IsHdr && DestProfile is null;

    public static DisplayColorPass For(PhotoMetadata? meta, bool enableHdr)
    {
        var canApplyProfile = CanApplyDestProfile(meta);
        var destProfile = canApplyProfile ? Core.DestColorProfile : null;

        var isHdr = enableHdr && meta?.IsHdr == true;
        if (!isHdr) return new DisplayColorPass(false, HdrTransferFunction.None, 0, null, destProfile);

        var options = Core.HdrToneMappingConfig with { };

        return new DisplayColorPass(true, meta!.HdrTransferFn, meta.ContentPeakNits, options, destProfile);
    }

    public static bool CanApplyDestProfile(PhotoMetadata? meta)
    {
        if (!Core.IsDestColorProfileSupported) return false;
        if (Core.Config.EnableAlwaysApplyColorProfile) return true;

        return meta?.SkiaColorSpace is not null;
    }

    public SKImage? Apply(SKImage srcImage)
    {
        if (IsHdr)
        {
            var toneMapped = HdrToneMapper.ToneMapToSdr(srcImage, TransferFn, Options!, ContentPeakNits);

            if (!toneMapped.IsDisposed())
            {
                if (DestProfile is null) return toneMapped;

                var isProfiled = SkiaCodec.TryApplyColorSpace(toneMapped, DestProfile, out var profiled);
                if (!isProfiled) return toneMapped;

                toneMapped.Dispose();
                return profiled;
            }
        }

        if (DestProfile is null) return null;

        var isInDestSpace = srcImage.ColorSpace is { } srcSpace && SKColorSpace.Equal(srcSpace, DestProfile);
        if (isInDestSpace) return null;

        var isColored = SkiaCodec.TryApplyColorSpace(srcImage, DestProfile, out var colored);
        return isColored ? colored : null;
    }
}
