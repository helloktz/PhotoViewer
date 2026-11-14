using PhotoViewer.UI.Viewer;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

public interface ITool
{

    string ToolId { get; }

    bool IsHosted { get; }

    object? Settings { get; }

    ViewerControl Viewer { get; set; }

    void LoadSettings(JsonElement? jsonEl) { }

    JsonElement? SaveSettings() => null;

    Task ExecuteAsync(ToolExecutionContext context) => Task.CompletedTask;
}
