using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class MenuHotkeysEditorControl : PhControl
{
    private const double MinTableHeight = 220;
    private const double BottomGap = 40;
    private ScrollViewer? _pageScroll;

    private Dictionary<LangId, Hotkey[]> _working = [];

    private readonly List<MenuHotkeyRowModel> _allRows = [];

    public event EventHandler? HotkeysChanged;

    public MenuHotkeysEditorControl()
    {
        InitializeComponent();

        PART_Table.MinHeight = MinTableHeight;

        PART_Search.TextChanged += (_, _) => RebuildTable();
        PART_Reset.Click += (_, _) => ResetAll();
    }

    public void LoadHotkeys(IReadOnlyDictionary<LangId, Hotkey[]> current)
    {
        _working = new Dictionary<LangId, Hotkey[]>(current);
        BuildRows();
        RebuildTable();
    }

    public Dictionary<LangId, Hotkey[]> CurrentHotkeys => new(_working);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _pageScroll = this.FindAncestorOfType<ScrollViewer>();
        if (_pageScroll is not null) _pageScroll.PropertyChanged += PageScroll_PropertyChanged;

        Dispatcher.UIThread.Post(UpdateTableMaxHeight, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_pageScroll is not null) _pageScroll.PropertyChanged -= PageScroll_PropertyChanged;
        _pageScroll = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void PageScroll_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty) UpdateTableMaxHeight();
    }

    private void UpdateTableMaxHeight()
    {
        if (_pageScroll is null) return;

        var viewport = _pageScroll.Bounds.Height;
        if (viewport <= 0) return;

        if (PART_Table.TranslatePoint(new Point(0, 0), _pageScroll) is not { } pt) return;

        var tableTop = pt.Y + _pageScroll.Offset.Y;
        PART_Table.MaxHeight = Math.Max(MinTableHeight, viewport - tableTop - BottomGap);
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        PART_Reset.Text = Core.Lang[LangId._ResetToDefault];
        PART_Search.PlaceholderText = Core.Lang[LangId._TypeToFilter];

        RelocalizeRows();
        RebuildTable();
    }

    private void BuildRows()
    {
        _allRows.Clear();

        var (paths, allKeys) = MenuMap();

        foreach (var def in AppAPIProvider.DefaultMenuList)
        {
            var key = Lang.GetKey(def.LangKey);
            if (key is null) continue;

            string path;
            if (paths.TryGetValue(key.Value, out var p)) path = p;
            else if (allKeys.Contains(key.Value)) continue;
            else path = Core.Lang[key.Value];

            var effective = _working.TryGetValue(key.Value, out var v) ? v : def.Hotkeys;
            _allRows.Add(new MenuHotkeyRowModel(key.Value, path, def.Hotkeys, effective));
        }

        UpdateConflicts();
    }

    private void RelocalizeRows()
    {
        if (_allRows.Count == 0) return;

        var (paths, _) = MenuMap();
        foreach (var row in _allRows)
        {
            row.ActionPath = paths.TryGetValue(row.MenuKey, out var p) ? p : Core.Lang[row.MenuKey];
        }
    }

    private static (Dictionary<LangId, string> Paths, HashSet<LangId> AllKeys) MenuMap()
        => App.MainWindow?.PART_MainView?.PART_Toolbar?.GetMenuActionMap()
            ?? ([], []);

    private void UpdateConflicts()
    {
        var counts = new Dictionary<string, int>();
        foreach (var row in _allRows)
        {
            foreach (var hk in row.Hotkeys)
                counts[hk.KeyString] = counts.GetValueOrDefault(hk.KeyString) + 1;
        }

        foreach (var row in _allRows)
        {
            row.IsConflict = row.Hotkeys.Any(hk => counts.GetValueOrDefault(hk.KeyString) > 1);
        }
    }

    private void RebuildTable()
    {
        PhTableColumn[] columns =
        [
            new() { Header = Core.Lang[LangId.Settings_Keyboard_Action], MinWidth = 200 },
            new() { Header = Core.Lang[LangId._Hotkeys], Star = true, MinWidth = 140 },
        ];

        var query = PART_Search.Text?.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allRows
            : _allRows.Where(r =>
                r.ActionPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.HotkeysText.Contains(query, StringComparison.OrdinalIgnoreCase));

        var rows = filtered.Select(row =>
        {
            var model = row;
            return new PhTableRow
            {
                Key = row.MenuKey.ToString(),
                Cells =
                [
                    PhTableControl.TextCell(row.ActionPath),
                    BuildHotkeysCell(row),
                ],
                Actions =
                [
                    new()
                    {
                        Icon = ResxIconId.IconEdit,
                        Tooltip = row.EditTooltip,
                        Click = () => _ = EditRowAsync(model),
                    },
                ],
            };
        }).ToList();

        PART_Table.EmptyText = Core.Lang[LangId.Settings_Keyboard_NoResults];
        PART_Table.Build(columns, rows);
    }

    private static Border BuildHotkeysCell(MenuHotkeyRowModel row)
    {
        var tone = row.HotkeysShowDanger ? PhHotkeyChipTone.Danger
            : row.HotkeysShowAccent ? PhHotkeyChipTone.Accent
            : PhHotkeyChipTone.Normal;

        var panel = new WrapPanel();
        foreach (var hk in row.Hotkeys)
        {
            panel.Children.Add(new PhHotkeyChip(hk.KeyString)
            {
                Tone = tone,
                Margin = new Thickness(0, 0, 6, 0),
            });
        }

        var cell = PhTableControl.WrapCell(panel);
        if (row.HotkeysTooltip is { } tip) ToolTip.SetTip(cell, tip);
        return cell;
    }

    private async Task EditRowAsync(MenuHotkeyRowModel row)
    {
        var win = new MenuHotkeyEditWindow(row.ActionPath, row.Hotkeys, row.DefaultHotkeys);
        if (await win.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (win.ResultHotkeys is not { } result) return;

        if (MenuHotkeyRowModel.HotkeysSetEqual(result, row.DefaultHotkeys))
        {
            _working.Remove(row.MenuKey);
        }
        else
        {
            _working[row.MenuKey] = result;
        }

        row.Hotkeys = result;
        UpdateConflicts();
        RebuildTable();
        PART_Table.FlashRow(row.MenuKey.ToString());
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetAll()
    {
        _working.Clear();
        foreach (var row in _allRows) row.Hotkeys = row.DefaultHotkeys;
        UpdateConflicts();
        RebuildTable();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

}
