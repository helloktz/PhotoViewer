using Avalonia.Input;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Actions;

[JsonSerializable(typeof(HotkeySingleAction))]
public partial class HotkeySingleActionJsonContext : JsonSerializerContext { }

public partial class HotkeySingleAction : SingleAction
{

    public Hotkey[] Hotkeys { get; set; } = [];

    public HotkeySingleAction() : base(null) { }

    public HotkeySingleAction(LangId langKey, API api, KeyModifiers modifiers, Key key) : base(api)
    {
        LangKey = Lang.KeysMap[langKey];
        Hotkeys = [new Hotkey(modifiers, key)];
    }

    public HotkeySingleAction(LangId langKey, API api, Key key) : base(api)
    {
        LangKey = Lang.KeysMap[langKey];
        Hotkeys = [new Hotkey(key)];
    }

    public HotkeySingleAction(LangId langKey, API api, Hotkey[]? hotkeys = null) : base(api)
    {
        LangKey = Lang.KeysMap[langKey];
        if (hotkeys is not null) Hotkeys = hotkeys;
    }

    public HotkeySingleAction(LangId langKey, API api, string? argument, Hotkey[]? hotkeys = null)
        : base(api, argument)
    {
        LangKey = Lang.KeysMap[langKey];
        if (hotkeys is not null) Hotkeys = hotkeys;
    }

}
