using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

public delegate IToolControl ToolControlFactory(ViewerControl viewer);

public sealed class ToolRegistry : PhDisposable
{
    private readonly Dictionary<string, ITool> _tools = new();

    public ToolProcessManager ExternalTools { get; } = new();

    public void Register(string toolId, ITool tool)
    {
        _tools[toolId] = tool;
    }

    public void RemoveExternalTools()
    {
        var ids = _tools.Where(kv => kv.Value is ExternalToolProxy).Select(kv => kv.Key).ToList();
        foreach (var id in ids) _tools.Remove(id);
    }

    public ITool? Get(string toolId)
    {
        _tools.TryGetValue(toolId, out var tool);
        return tool;
    }

    public bool Contains(string toolId) => _tools.ContainsKey(toolId);

    public IEnumerable<ExternalTool> GetAllExternalToolManifests()
    {
        foreach (var tool in _tools.Values)
        {
            if (tool is ExternalToolProxy proxy)
                yield return proxy.Tool;
        }
    }

    public IEnumerable<string> GetAllToolIds() => _tools.Keys;

    public static void LoadToolSettings(ITool tool)
    {
        JsonElement? jsonEl = Core.Config.ToolSettings.TryGetValue(tool.ToolId, out var el)
            ? el
            : null;

        tool.LoadSettings(jsonEl);
    }

    public static void SaveToolSettings(ITool tool)
    {
        var jsonEl = tool.SaveSettings();

        SetToolSettings(tool.ToolId, jsonEl);
    }

    public static void SetToolSettings(string toolId, JsonElement? jsonEl)
    {
        var toolSettings = Core.Config.ToolSettings;

        if (jsonEl is null)
        {
            toolSettings.Remove(toolId);
        }
        else
        {
            toolSettings[toolId] = jsonEl.Value;
        }

        Core.Config.ToolSettings = toolSettings;
    }

    public static async Task ExecuteNonHostedToolAsync(ITool tool, ToolExecutionContext context)
    {
        try
        {
            await tool.ExecuteAsync(context);
        }
        finally
        {
            SaveToolSettings(tool);
        }
    }

    protected override void OnDisposing()
    {
        ExternalTools.Dispose();
        base.OnDisposing();
    }

}
