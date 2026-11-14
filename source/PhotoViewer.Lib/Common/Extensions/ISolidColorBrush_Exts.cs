using Avalonia.Media;

namespace PhotoViewer.Common.Extensions;

public static class ISolidColorBrush_Exts
{
    extension(ISolidColorBrush b)
    {
        public byte A => b.Color.A;
        public byte R => b.Color.R;
        public byte G => b.Color.G;
        public byte B => b.Color.B;
    }
}
