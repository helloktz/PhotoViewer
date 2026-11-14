using Avalonia;

namespace PhotoViewer.Common.Extensions;

public static class Size_Exts
{

    extension(Size size)
    {

        public bool IsEmpty => size.Width <= 0
            || size.Height <= 0
            || !double.IsFinite(size.Width)
            || !double.IsFinite(size.Height);

    }

}
