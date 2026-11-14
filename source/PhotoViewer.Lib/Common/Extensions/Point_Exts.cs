using Avalonia;

namespace PhotoViewer.Common.Extensions;

public static class Point_Exts
{
    extension(Point p)
    {

        public PixelPoint ToPixelPoint()
        {
            return new PixelPoint((int)p.X, (int)p.Y);
        }

    }
}
