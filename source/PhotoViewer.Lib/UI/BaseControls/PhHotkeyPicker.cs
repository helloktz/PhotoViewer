using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhotoViewer.Common.Types;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.UI;

public class PhHotkeyPicker : PhControl
{
    private readonly List<Hotkey> _hotkeys = [];
    private readonly WrapPanel _chips;
    private readonly PhTextBox _recorder;

    public IReadOnlyList<Hotkey> Hotkeys
    {
        get => [.. _hotkeys];
        set
        {
            _hotkeys.Clear();
            if (value is not null) _hotkeys.AddRange(value);
            RenderChips();
        }
    }

    public string? PlaceholderText
    {
        get => _recorder.PlaceholderText;
        set => _recorder.PlaceholderText = value;
    }

    public PhHotkeyPicker()
    {
        _recorder = new PhTextBox
        {
            IsReadOnly = true,
            ValidateByPressingEnter = false,
        };

        _recorder.AddHandler(KeyDownEvent, OnRecorderKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);

        _chips = new WrapPanel
        {
            IsVisible = false,
            Margin = new Thickness(0, 8, 0, 0),
        };

        Content = new StackPanel
        {
            Children = { _recorder, _chips },
        };
    }

    private void OnRecorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Escape or Key.Enter) return;

        if (IsModifierKey(e.Key)) { e.Handled = true; return; }

        AddHotkey(new Hotkey(e.KeyModifiers, e.Key));
        e.Handled = true;
    }

    private static bool IsModifierKey(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin;

    private void AddHotkey(Hotkey hk)
    {
        if (hk.Key == Key.None) return;
        if (_hotkeys.Any(h => h.Key == hk.Key && h.Modifiers == hk.Modifiers)) return;

        _hotkeys.Add(hk);
        RenderChips();
    }

    private void RenderChips()
    {
        _chips.Children.Clear();
        foreach (var hk in _hotkeys)
        {
            _chips.Children.Add(BuildChip(hk));
        }
        _chips.IsVisible = _hotkeys.Count > 0;
    }

    private PhHotkeyChip BuildChip(Hotkey hk)
    {
        var chip = new PhHotkeyChip(hk.KeyString, showDelete: true)
        {
            Margin = new Thickness(0, 0, 6, 6),
        };
        chip.Deleted += (_, _) =>
        {
            _hotkeys.Remove(hk);
            RenderChips();
        };
        return chip;
    }

}
