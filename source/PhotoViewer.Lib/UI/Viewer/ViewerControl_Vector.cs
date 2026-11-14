using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using SkiaSharp;
using Svg.Skia;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{

    internal SKPicture? _svgPicture;

    internal SKSvg? _svgDocument;

    private void DisposeVectorResources()
    {
        _svgPicture = null;
        _svgDocument?.Dispose();
        _svgDocument = null;
    }

    private bool HandleVectorPhotoLoaded(SkiaVectorSource vectorSource)
    {
        if (vectorSource.VectorPicture is null) return false;

        DisposeVectorResources();

        _svgDocument = vectorSource.SvgDocument;
        _svgPicture = vectorSource.VectorPicture;

        vectorSource.SvgDocument = null;

        SourceKind = PhotoSource.VectorRenderer;

        BitmapSize = vectorSource.IntrinsicSize;

        SKImageRef.Set(ref _imgSource, vectorSource.RasterizedFallback);
        vectorSource.RasterizedFallback = null;

        _isFirstDraw.SetTrue();

        if (_svgDocument!.HasAnimations)
        {
            _animator?.FrameChanged -= Animator_FrameChanged;
            _animator?.Dispose();

            _animator = new SvgAnimator(
                _svgDocument,
                _lock,
                picture => _svgPicture = picture,
                InvalidateVectorFrame);
            _animator.FrameChanged += Animator_FrameChanged;
            StartAnimator();
        }

        return true;
    }

    private void InvalidateVectorFrame()
    {
        InvalidateVisual();
        OnRenderStateChanged();
    }

    private bool IsVectorSource() => SourceKind == PhotoSource.VectorRenderer;

    internal static SKMatrix ComputeVectorTransform(SKRect srcRect, SKRect destRect)
    {
        var scaleX = destRect.Width / srcRect.Width;
        var scaleY = destRect.Height / srcRect.Height;
        var tx = destRect.Left - srcRect.Left * scaleX;
        var ty = destRect.Top - srcRect.Top * scaleY;

        return SKMatrix.CreateScaleTranslation(scaleX, scaleY, tx, ty);
    }
}
