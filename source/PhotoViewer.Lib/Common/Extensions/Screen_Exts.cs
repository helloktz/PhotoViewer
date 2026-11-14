using Avalonia;
using Avalonia.Platform;

namespace PhotoViewer.Common.Extensions;

public static class Screen_Exts
{
    extension(Screen screen)
    {

        public PixelRect GetActualWorkingArea()
        {
            return Core.ShellProvider?.GetScreenWorkingArea(screen) ?? screen.WorkingArea;
        }
    }
}
