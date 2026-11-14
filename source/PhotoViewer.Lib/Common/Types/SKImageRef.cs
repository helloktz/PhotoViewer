using PhotoViewer.Common.Extensions;
using SkiaSharp;
using System;
using System.Threading;

namespace PhotoViewer.Common.Types;

public sealed class SKImageRef
{
    private SKImage? _image;
    private int _refCount;
    private int _disposeRequested;

    public SKImage? Image => _image;

    public SKImageRef(SKImage? image)
    {
        _image = image;
        _refCount = image is null ? 0 : 1;
    }

    public void KeepAlive()
    {
        if (_image is null) return;
        Interlocked.Increment(ref _refCount);
    }

    public ImageLease? Acquire()
    {
        var image = _image;

        if (image is null || image.IsDisposed()) return null;

        Interlocked.Increment(ref _refCount);
        return new ImageLease(this, image);
    }

    public void RequestDispose()
    {
        if (_image is null) return;

        Interlocked.Exchange(ref _disposeRequested, 1);
        Release();
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _refCount) == 0 && _disposeRequested == 1)
        {
            var image = Interlocked.Exchange(ref _image, null);
            image?.Dispose();
        }
    }

    public sealed class ImageLease : IDisposable
    {
        private SKImageRef? _owner;
        public SKImage Image { get; }

        public ImageLease(SKImageRef owner, SKImage image)
        {
            _owner = owner;
            Image = image;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }

    public static void Set(ref SKImageRef? srcImg, SKImage? newImg, SKImageRef? shareFrom = null)
    {
        if (ReferenceEquals(srcImg?.Image, newImg)) return;

        srcImg?.RequestDispose();

        if (newImg is null)
        {
            srcImg = null;
            return;
        }

        if (shareFrom is not null && ReferenceEquals(shareFrom.Image, newImg))
        {
            shareFrom.KeepAlive();
            srcImg = shareFrom;
            return;
        }

        srcImg = new SKImageRef(newImg);
    }

}
