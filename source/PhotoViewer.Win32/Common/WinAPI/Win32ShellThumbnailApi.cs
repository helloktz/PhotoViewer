using PhotoViewer.Common.Photoing;
using SkiaSharp;
using System.IO;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace PhotoViewer.Win32.Common;

public static class Win32ShellThumbnailApi
{

    public static SKImage? GetThumbnail(string filePath, int width, int height, bool useCacheOnly)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var flags = SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_BIGGERSIZEOK;

            if (useCacheOnly)
            {
                using var hBitmapCached = GetThumbnailHBitmap(filePath, width, height,
                    flags | SIIGBF.SIIGBF_INCACHEONLY);

                if (hBitmapCached is not null)
                {
                    return ConvertHBitmapToSKBitmap(hBitmapCached);
                }
            }

            else
            {
                using var hBitmap = GetThumbnailHBitmap(filePath, width, height, flags);

                if (hBitmap is not null)
                {
                    return ConvertHBitmapToSKBitmap(hBitmap);
                }
            }

        }
        catch { }

        return null;
    }

    private static DeleteObjectSafeHandle? GetThumbnailHBitmap(string filePath,
        int width, int height, SIIGBF options)
    {
        PInvoke.SHCreateItemFromParsingName<IShellItemImageFactory>(filePath, null, out var shItemImageFac)
            .ThrowOnFailure();

        if (shItemImageFac is null) return null;

        shItemImageFac.GetImage(new SIZE(width, height), options, out var hBitmap);

        return hBitmap;
    }

    private static unsafe SKImage? ConvertHBitmapToSKBitmap(DeleteObjectSafeHandle hBitmap)
    {
        HDC hdc = default;
        HGDIOBJ oldBitmap = default;
        SKImage? imgOutput = null;

        try
        {
            hdc = PInvoke.CreateCompatibleDC(default);
            oldBitmap = PInvoke.SelectObject(hdc, (HGDIOBJ)hBitmap.DangerousGetHandle());

            BITMAP bm;
            if (PInvoke.GetObject((HGDIOBJ)hBitmap.DangerousGetHandle(), sizeof(BITMAP), &bm) == 0)
                return null;

            var info = new SKImageInfo(bm.bmWidth, bm.bmHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bmpOutput = new SKBitmap(info);

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = bm.bmWidth,
                    biHeight = -bm.bmHeight,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                }
            };

            if (PInvoke.GetDIBits(hdc, (HBITMAP)hBitmap.DangerousGetHandle(),
                0, (uint)bm.bmHeight, (void*)bmpOutput.GetPixels(), &bmi, DIB_USAGE.DIB_RGB_COLORS) == 0)
            {
                return null;
            }

            if (bm.bmBitsPixel < 32)
            {
                var pixels = (byte*)bmpOutput.GetPixels();
                var totalPixels = bm.bmWidth * bm.bmHeight;
                for (var i = 0; i < totalPixels; i++)
                {
                    pixels[i * 4 + 3] = 0xFF;
                }
            }

            imgOutput = SkiaCodec.ToSKImage(bmpOutput);
            return imgOutput;
        }
        catch
        {
            imgOutput?.Dispose();
            return null;
        }
        finally
        {
            if (oldBitmap != default)
                PInvoke.SelectObject(hdc, oldBitmap);

            if (hdc != default)
                PInvoke.DeleteDC(hdc);
        }
    }

}
