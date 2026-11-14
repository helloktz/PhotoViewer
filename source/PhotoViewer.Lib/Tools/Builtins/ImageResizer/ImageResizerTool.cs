using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using PhotoViewer.UI.Viewer;
using PhotoViewer.UI.Windowing;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

public sealed class ImageResizerTool : ITool
{
    public static string TOOL_ID => "Tool_ImageResizer";

    public string ToolId => TOOL_ID;
    public bool IsHosted => false;
    public object? Settings { get; private set; }
    public ViewerControl Viewer { get; set; } = null!;

    public async Task ExecuteAsync(ToolExecutionContext context)
    {
        if (Core.IsBusy) return;

        Core.IsBusy = true;

        try
        {
            using var shownState = Viewer.CaptureImageState();
            using var srcBmp = shownState?.CopyPixels();
            if (srcBmp.IsDisposed()) return;

            var resizerWindow = new ImageResizerWindow(srcBmp);
            var result = await resizerWindow.ShowAsync(context.Window);

            if (result == DialogExitCode.OK && !resizerWindow.OutputBitmap.IsDisposed())
            {
                var photo = new Photo(resizerWindow.OutputBitmap);
                await Core.LoadClipboardPhotoAsync(photo);
            }
        }
        finally
        {
            Core.IsBusy = false;
        }
    }
}
