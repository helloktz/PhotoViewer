using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using System;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Actions;

[JsonSerializable(typeof(SingleAction))]
public partial class SingleActionJsonContext : JsonSerializerContext { }

public partial class SingleAction : PhReactive
{

    public string Executable { get; set; } = "";

    public string Argument { get; set; } = "";

    public SingleAction? NextAction { get; set; }

    [JsonIgnore]
    public string? LangKey { get; set; }

    public SingleAction() { }

    public SingleAction(string executable = "", string argument = "", SingleAction? nextAction = null)
    {
        Executable = executable;
        Argument = argument;
        NextAction = nextAction;
    }

    public SingleAction(API api, string? argument = null, string langKey = "")
    {
        Executable = Enum.GetName(api) ?? "";
        Argument = argument ?? "";
        LangKey = langKey;
    }

    public SingleAction(SingleAction? action = null)
    {
        Executable = action?.Executable ?? "";
        Argument = action?.Argument ?? "";
        NextAction = action?.NextAction;
        LangKey = action?.LangKey;
    }

    public override string ToString()
    {
        return Executable;
    }

}
