using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.Common.Windows;

public sealed class MenuHotkeyRowModel : PhReactive
{

    public LangId MenuKey { get; }

    public string ActionPath
    {
        get; set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = string.Empty;

    public Hotkey[] DefaultHotkeys { get; }

    public Hotkey[] Hotkeys
    {
        get; set
        {
            field = value ?? [];
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeysText));
            OnPropertyChanged(nameof(HotkeysTooltip));
            OnPropertyChanged(nameof(IsChanged));
            OnPropertyChanged(nameof(IsDefault));
            OnPropertyChanged(nameof(HotkeysShowAccent));
            OnPropertyChanged(nameof(HotkeysShowNormal));
        }
    } = [];

    public string HotkeysText => string.Join(", ", Hotkeys.Select(h => h.KeyString));

    public string DefaultHotkeysText => string.Join(", ", DefaultHotkeys.Select(h => h.KeyString));

    public bool IsChanged => !HotkeysSetEqual(Hotkeys, DefaultHotkeys);

    public bool IsDefault => !IsChanged;

    public bool IsConflict
    {
        get; set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeysTooltip));
            OnPropertyChanged(nameof(HotkeysShowDanger));
            OnPropertyChanged(nameof(HotkeysShowAccent));
            OnPropertyChanged(nameof(HotkeysShowNormal));
        }
    }

    public bool HotkeysShowDanger => IsConflict;
    public bool HotkeysShowAccent => IsChanged && !IsConflict;
    public bool HotkeysShowNormal => !IsChanged && !IsConflict;

    public string EditTooltip => Core.Lang[LangId._Edit];

    public string? HotkeysTooltip => IsConflict
        ? Core.Lang[LangId.Settings_Keyboard_Conflict]
        : HotkeysText.Length > 0 ? HotkeysText : null;

    public MenuHotkeyRowModel(LangId menuKey, string actionPath, Hotkey[] defaultHotkeys, Hotkey[] hotkeys)
    {
        MenuKey = menuKey;
        ActionPath = actionPath;
        DefaultHotkeys = defaultHotkeys ?? [];
        Hotkeys = hotkeys ?? [];
    }

    public static bool HotkeysSetEqual(IReadOnlyList<Hotkey> a, IReadOnlyList<Hotkey> b)
    {
        if (a.Count != b.Count) return false;

        var sa = a.Select(h => h.KeyString).OrderBy(s => s, StringComparer.Ordinal);
        var sb = b.Select(h => h.KeyString).OrderBy(s => s, StringComparer.Ordinal);
        return sa.SequenceEqual(sb);
    }

}
