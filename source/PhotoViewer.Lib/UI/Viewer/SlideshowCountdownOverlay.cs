using Avalonia;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using System.Globalization;

namespace PhotoViewer.UI.Viewer;

public class SlideshowCountdownOverlay : PhControl
{

    public override void Render(DrawingContext c)
    {
        base.Render(c);

        if (!Core.Config.EnableSlideshow || !Core.Config.EnableSlideshowCountdown) return;
        if (Core.Slideshow is null || !Core.Slideshow.IsRunning) return;

        var totalSeconds = Core.Slideshow.CountdownSeconds;
        var minutes = (int)(totalSeconds / 60);
        var seconds = (int)(totalSeconds % 60);
        var tenths = (int)(totalSeconds * 10 % 10);

        var text = $"{minutes:D2}:{seconds:D2}.{tenths}";
        if (Core.Slideshow.IsPaused) text = $"▮▮ {text}";

        var typeface = new Typeface(Const.FONT_CODE);
        var fontSize = DpiScale(14d);
        var padding = 20d;

        var countDownShadow = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection,
            typeface,
            fontSize,
            Brushes.Black);

        var x = Bounds.Width - countDownShadow.Width - padding;
        var y = Bounds.Height - countDownShadow.Height - padding;
        c.DrawText(countDownShadow, new Point(x + DpiScale(1d), y + DpiScale(1d)));

        var countDownText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection,
            typeface,
            fontSize,
            Brushes.White);

        c.DrawText(countDownText, new Point(x, y));
    }
}
