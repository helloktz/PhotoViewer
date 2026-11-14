using Avalonia;
using Avalonia.Media;
using PhotoViewer.Common.Extensions;

namespace PhotoViewer.UI.Viewer.Checkerboard;

internal class CheckerboardInfo
{

    public CheckerboardType Mode { get; set; } = CheckerboardType.None;

    public Size Size { get; set; } = new Size(10, 10);

    public Color Color1 { get; set; } = Colors.Black.WithAlpha(25);

    public Color Color2 { get; set; } = Colors.White.WithAlpha(25);

}
