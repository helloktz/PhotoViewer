using PhotoViewer.UI.Viewer;

namespace PhotoViewer.Tools;

public sealed class ToolControlAdapter : ITool
{
    private readonly ToolControlFactory _factory;

    public string ToolId { get; }
    public bool IsHosted => true;

    public object? Settings => null;
    public ViewerControl Viewer { get; set; } = null!;

    public ToolControlAdapter(string toolId, ToolControlFactory factory)
    {
        ToolId = toolId;
        _factory = factory;
    }

    public IToolControl CreateToolControl(ViewerControl viewer) => _factory(viewer);
}
