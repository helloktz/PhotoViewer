using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Actions;

[JsonSerializable(typeof(ToggleAction))]
public partial class ToggleActionJsonContext : JsonSerializerContext { }

public partial class ToggleAction : PhReactive
{

    private static readonly Dictionary<Guid, bool> _manager = [];

    [JsonIgnore]
    public Guid Id { get; init; } = Guid.NewGuid();

    public SingleAction? ToggleOn { get; set; } = null;

    public SingleAction? ToggleOff { get; set; } = null;

    public ToggleAction(SingleAction? toggleOn = null, SingleAction? toggleOff = null)
    {
        ToggleOn = toggleOn;
        ToggleOff = toggleOff;
    }

    public override string ToString()
    {
        return $"{ToggleOn?.ToString() ?? "<empty>"} | {ToggleOff?.ToString() ?? "<empty>"}";
    }

    public static bool IsToggleOff(Guid cmdId)
    {
        if (_manager.TryGetValue(cmdId, out var isToggled))
        {
            return isToggled;
        }

        return false;
    }

    public static void SetToggleValue(Guid cmdId, bool isToggled)
    {
        if (!_manager.TryAdd(cmdId, isToggled))
        {
            _manager[cmdId] = isToggled;
        }
    }

}
