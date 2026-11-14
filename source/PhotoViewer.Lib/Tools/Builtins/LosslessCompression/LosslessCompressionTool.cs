using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.UI.Viewer;
using PhotoViewer.UI.Windowing;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

public sealed class LosslessCompressionTool : ITool
{
    public static string TOOL_ID => "Tool_LosslessCompression";

    public string ToolId => TOOL_ID;
    public bool IsHosted => false;
    public object? Settings { get; private set; }
    public ViewerControl Viewer { get; set; } = null!;

    public async Task ExecuteAsync(ToolExecutionContext context)
    {
        if (Core.IsBusy || Core.Photos.Count == 0 || Core.ClipboardImage != null) return;

        var filePath = Core.Photos.CurrentFilePath;

        if (!MagickCodec.IsLosslessCompressSupported(filePath))
        {
            _ = await ModalWindow.ShowInfoAsync(context.Window, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Menu_MnuLosslessCompression],
                Heading = Core.Lang[LangId._NotSupported],
                Description = filePath,
                Thumbnail = Core.Photos.Current?.GalleryThumbnail,
            });

            return;
        }

        Core.IsBusy = true;

        try
        {
            var compressionWindow = new LosslessCompressionWindow(filePath);
            _ = await compressionWindow.ShowAsync(context.Window);
        }
        finally
        {
            Core.IsBusy = false;
        }
    }
}
