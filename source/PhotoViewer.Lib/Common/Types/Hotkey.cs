using Avalonia.Input;
using PhotoViewer.Common.Types.JsonTypeConverters;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoViewer.Common.Types;

[JsonConverter(typeof(JsonStringToHotkeyConverter))]
public class Hotkey
{

    public static readonly KeyModifiers Ctrl = BHelper.OS == OSType.Mac ? KeyModifiers.Meta : KeyModifiers.Control;

    public static readonly Key Delete = BHelper.OS == OSType.Mac ? Key.Back : Key.Delete;

    public Key Key { get; set; } = Key.None;

    public KeyModifiers Modifiers { get; set; } = KeyModifiers.None;
    public bool Control => Modifiers.HasFlag(KeyModifiers.Control);
    public bool Shift => Modifiers.HasFlag(KeyModifiers.Shift);
    public bool Alt => Modifiers.HasFlag(KeyModifiers.Alt);

    public string KeyString => ToString(this);

    public string InvariantKeyString => ToInvariantString(this);

    public Hotkey() { }

    public Hotkey(Key key)
    {
        Key = key;
    }

    public Hotkey(KeyModifiers modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public Hotkey(KeyGesture kg)
    {
        Key = kg.Key;
        Modifiers = kg.KeyModifiers;
    }

    public Hotkey(KeyEventArgs e)
    {
        Key = e.Key;
        Modifiers = e.KeyModifiers;
    }

    private static readonly Dictionary<Key, string> _invariantKeyNames = new()
    {
        [Key.D0] = "0",
        [Key.D1] = "1",
        [Key.D2] = "2",
        [Key.D3] = "3",
        [Key.D4] = "4",
        [Key.D5] = "5",
        [Key.D6] = "6",
        [Key.D7] = "7",
        [Key.D8] = "8",
        [Key.D9] = "9",
        [Key.Back] = "Backspace",
        [Key.OemPlus] = "+",
        [Key.OemMinus] = "-",
        [Key.OemComma] = ",",
        [Key.OemPeriod] = ".",
        [Key.OemQuestion] = "/",
        [Key.OemSemicolon] = ";",
        [Key.OemQuotes] = "'",
        [Key.OemOpenBrackets] = "[",
        [Key.OemCloseBrackets] = "]",
        [Key.OemPipe] = "|",
        [Key.OemBackslash] = "\\",
        [Key.OemTilde] = "`",
    };

    private static readonly Dictionary<string, Key> _keyAliases = BuildKeyAliases();

    private static readonly Dictionary<string, KeyModifiers> _modifierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = KeyModifiers.Control,
        ["Control"] = KeyModifiers.Control,
        ["⌃"] = KeyModifiers.Control,
        ["Shift"] = KeyModifiers.Shift,
        ["⇧"] = KeyModifiers.Shift,
        ["Alt"] = KeyModifiers.Alt,
        ["Option"] = KeyModifiers.Alt,
        ["⌥"] = KeyModifiers.Alt,
        ["Cmd"] = KeyModifiers.Meta,
        ["Command"] = KeyModifiers.Meta,
        ["Meta"] = KeyModifiers.Meta,
        ["Win"] = KeyModifiers.Meta,
        ["Super"] = KeyModifiers.Meta,
        ["⌘"] = KeyModifiers.Meta,
    };

    private static Dictionary<string, Key> BuildKeyAliases()
    {
        var aliases = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["\""] = Key.OemQuotes,
            ["*"] = Key.Multiply,
            ["Esc"] = Key.Escape,
            ["Del"] = Key.Delete,
            ["Ins"] = Key.Insert,
            ["PgUp"] = Key.PageUp,
            ["PgDn"] = Key.PageDown,
            ["Up Arrow"] = Key.Up,
            ["Down Arrow"] = Key.Down,
            ["Left Arrow"] = Key.Left,
            ["Right Arrow"] = Key.Right,

            ["←"] = Key.Left,
            ["↑"] = Key.Up,
            ["→"] = Key.Right,
            ["↓"] = Key.Down,
            ["↖"] = Key.Home,
            ["↘"] = Key.End,
            ["↩"] = Key.Return,
            ["⇞"] = Key.PageUp,
            ["⇟"] = Key.PageDown,
            ["⇥"] = Key.Tab,
            ["⌫"] = Key.Back,
            ["⎋"] = Key.Escape,
            ["␣"] = Key.Space,
        };

        foreach (var (key, name) in _invariantKeyNames)
        {
            aliases[name] = key;
        }

        return aliases;
    }

    public override string ToString() => KeyString;

    public bool IsSame(Hotkey? hk)
    {
        if (hk is null) return false;

        return IsSame(hk.Key, hk.Modifiers);
    }

    public bool IsSame(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (Key != key) return false;
        if (Control && !modifiers.HasFlag(KeyModifiers.Control)) return false;
        if (Shift && !modifiers.HasFlag(KeyModifiers.Shift)) return false;
        if (Alt && !modifiers.HasFlag(KeyModifiers.Alt)) return false;

        return true;
    }

    public KeyGesture ToGesture()
    {
        return new KeyGesture(Key, Modifiers);
    }

    public static Hotkey? ParseFrom(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        var modifiers = KeyModifiers.None;
        var key = Key.None;
        var partStart = 0;

        for (var i = 0; i <= s.Length; i++)
        {
            var isLastPart = i == s.Length;
            if (!isLastPart && (s[i] != '+' || i == partStart)) continue;

            var part = s[partStart..i].Trim();
            partStart = i + 1;

            if (isLastPart)
            {
                if (!TryParseKey(part, out key)) return null;
            }
            else
            {
                if (!_modifierAliases.TryGetValue(part, out var modifier)) return null;
                modifiers |= modifier;
            }
        }

        return new Hotkey(modifiers, key);
    }

    public static string ToString(Hotkey hotkey)
    {
        var kg = new KeyGesture(hotkey.Key, hotkey.Modifiers);
        return kg.ToString("p", null);
    }

    public static string ToInvariantString(Hotkey hotkey)
    {
        var parts = new List<string>(5);
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (hotkey.Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Cmd");

        parts.Add(_invariantKeyNames.GetValueOrDefault(hotkey.Key) ?? hotkey.Key.ToString());

        return string.Join('+', parts);
    }

    private static bool TryParseKey(string name, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrEmpty(name)) return false;

        if (_keyAliases.TryGetValue(name, out key)) return true;

        if (!char.IsAsciiLetter(name[0])) return false;

        return Enum.TryParse(name, true, out key) && key != Key.None;
    }

}
