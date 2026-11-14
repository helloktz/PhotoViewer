using PhotoViewer.Common.Types;
using System.Text.Json.Serialization;

namespace PhotoViewer.Tools;

[JsonSerializable(typeof(ExternalTool))]
public partial class IgToolJsonContext : JsonSerializerContext { }

public sealed class ExternalTool
{

    public required string ToolId { get; init; }

    public string ToolName { get; init; } = string.Empty;

    public string Executable { get; init; } = string.Empty;

    public string Arguments { get; init; } = string.Empty;

    public bool IsIntegrated { get; init; }

    public Hotkey[] Hotkeys { get; init; } = [];
}
