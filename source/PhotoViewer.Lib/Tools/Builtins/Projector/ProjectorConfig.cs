using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoViewer.Tools;

[JsonSerializable(typeof(ProjectorConfig))]
public partial class ProjectorConfigJsonContext : JsonSerializerContext { }

public sealed class ProjectorConfig
{

    public bool EnableViewSync { get; set; } = true;

    public bool ShowPointer { get; set; }

    public List<string> BackgroundColors { get; set; } = [];
}
