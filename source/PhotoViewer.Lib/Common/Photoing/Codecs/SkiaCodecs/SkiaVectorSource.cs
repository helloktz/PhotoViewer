using Avalonia;
using PhotoViewer.Common.Types;
using SkiaSharp;
using Svg.Skia;

namespace PhotoViewer.Common.Photoing;

public sealed class SkiaVectorSource : PhDisposable
{

    public SKSvg? SvgDocument { get; set; }

    public SKPicture? VectorPicture => SvgDocument?.Picture;

    public SKImage? RasterizedFallback { get; set; }

    public Size IntrinsicSize { get; }

    public bool HasAnimations => SvgDocument?.HasAnimations ?? false;

    public SkiaVectorSource(SKSvg svgDocument, Size intrinsicSize, SKImage? rasterizedFallback)
    {
        SvgDocument = svgDocument;
        IntrinsicSize = intrinsicSize;
        RasterizedFallback = rasterizedFallback;
    }

    protected override void OnDisposing()
    {
        RasterizedFallback?.Dispose();
        RasterizedFallback = null;

        SvgDocument?.Dispose();
        SvgDocument = null;
    }
}
