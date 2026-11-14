using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotoViewer.Common.Actions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class MouseSettingsView : SettingsPageView
{
    private const double ACTION_CONTROL_WIDTH = 250;

    private readonly Dictionary<MouseWheelEvent, MouseWheelAction> _wheelActions = [];
    private readonly Dictionary<MouseClickEvent, SingleAction> _clickActions = [];

    public MouseSettingsView()
    {
        InitializeComponent();
    }

    public MouseSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        BuildWheelActions();
        BuildClickActions();
    }

    private void BuildWheelActions()
    {
        foreach (var (evt, action) in Config.DefaultMouseWheelActions) _wheelActions[evt] = action;
        foreach (var (evt, action) in VM.GetValue(ConfigId.MouseWheelActions,
            new Dictionary<MouseWheelEvent, MouseWheelAction>()))
        {
            _wheelActions[evt] = action;
        }

        SetLocalizedText(PART_ResetWheel, LangId._ResetToDefault);
        PART_ResetWheel.Click += (_, _) => ResetWheelActions();

        AddLangRefresher(RebuildWheelTable);

        RegisterSearchKey(PART_ResetWheel, LangId.Settings_MouseWheelAction,
            ConfigId.MouseWheelActions, LangId.Settings_MouseWheelAction);

        DisableIfLocked(ConfigId.MouseWheelActions, PART_WheelTable, PART_ResetWheel);
    }

    private void StageWheelActions()
        => VM.SetValue(ConfigId.MouseWheelActions,
            new Dictionary<MouseWheelEvent, MouseWheelAction>(_wheelActions));

    private void ResetWheelActions()
    {
        _wheelActions.Clear();
        foreach (var (evt, action) in Config.DefaultMouseWheelActions) _wheelActions[evt] = action;

        StageWheelActions();
        RebuildWheelTable();
    }

    private void RebuildWheelTable()
    {
        PART_WheelTable.Children.Clear();

        foreach (var evt in Enum.GetValues<MouseWheelEvent>())
        {
            var row = new StackPanel { Spacing = 5 };
            row.Children.Add(new PhTextBlock { Text = EnumLabel(nameof(MouseWheelEvent), evt) });
            row.Children.Add(BuildWheelCombo(evt));

            PART_WheelTable.Children.Add(row);
        }
    }

    private ComboBox BuildWheelCombo(MouseWheelEvent evt)
    {
        var combo = new ComboBox
        {
            Width = ACTION_CONTROL_WIDTH,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var current = _wheelActions.GetValueOrDefault(evt, MouseWheelAction.DoNothing);
        var selectedIndex = 0;

        var actions = Enum.GetValues<MouseWheelAction>();
        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            combo.Items.Add(new ComboBoxItem
            {
                Tag = action,
                Content = EnumLabel(nameof(MouseWheelAction), action),
            });
            if (action == current) selectedIndex = i;
        }
        combo.SelectedIndex = selectedIndex;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: MouseWheelAction action })
            {
                _wheelActions[evt] = action;
                StageWheelActions();
            }
        };

        return combo;
    }

    private void BuildClickActions()
    {
        foreach (var (evt, action) in Config.DefaultMouseClickActions) _clickActions[evt] = action;
        foreach (var (evt, action) in VM.GetValue(ConfigId.MouseClickActions,
            new Dictionary<MouseClickEvent, SingleAction>()))
        {
            _clickActions[evt] = action;
        }

        SetLocalizedText(PART_ResetClick, LangId._ResetToDefault);
        PART_ResetClick.Click += (_, _) => ResetClickActions();

        AddLangRefresher(RebuildClickTable);

        RegisterSearchKey(PART_ResetClick, LangId.Settings_MouseClickAction,
            ConfigId.MouseClickActions, LangId.Settings_MouseClickAction);

        DisableIfLocked(ConfigId.MouseClickActions, PART_ClickTable, PART_ResetClick);

    }

    private void StageClickActions()
        => VM.SetValue(ConfigId.MouseClickActions,
            new Dictionary<MouseClickEvent, SingleAction>(_clickActions));

    private void ResetClickActions()
    {
        _clickActions.Clear();
        foreach (var (evt, action) in Config.DefaultMouseClickActions) _clickActions[evt] = action;

        StageClickActions();
        RebuildClickTable();
    }

    private void RebuildClickTable()
    {
        PART_ClickTable.Children.Clear();

        foreach (var evt in Enum.GetValues<MouseClickEvent>())
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new PhTextBlock
            {
                Text = EnumLabel(nameof(MouseClickEvent), evt),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var row = new StackPanel { Spacing = 5 };
            row.Children.Add(header);
            row.Children.Add(BuildClickButton(evt));

            PART_ClickTable.Children.Add(row);
        }
    }

    private PhButton BuildClickButton(MouseClickEvent evt)
    {
        var exe = _clickActions.GetValueOrDefault(evt)?.Executable?.Trim();
        var hasExe = !string.IsNullOrEmpty(exe);

        var btn = new PhButton
        {
            Width = ACTION_CONTROL_WIDTH,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        var label = new TextBlock
        {
            Text = hasExe ? System.IO.Path.GetFileName(exe!) : Core.Lang[LangId.MouseWheelAction_DoNothing],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label[!TextBlock.ForegroundProperty] = btn[!Button.ForegroundProperty];
        btn.Content = label;

        if (hasExe) ToolTip.SetTip(btn, exe);

        btn.Click += async (_, _) => await EditClickActionAsync(evt);

        return btn;
    }

    private async Task EditClickActionAsync(MouseClickEvent evt)
    {
        var existing = _clickActions.GetValueOrDefault(evt);
        var window = new MouseClickActionEditWindow(EnumLabel(nameof(MouseClickEvent), evt), existing, false);

        if (await window.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (window.ResultAction is not { } result) return;

        if (string.IsNullOrEmpty(result.Executable)) _clickActions.Remove(evt);
        else _clickActions[evt] = result;

        StageClickActions();
        RebuildClickTable();
    }

    private static string EnumLabel<TEnum>(string enumName, TEnum value) where TEnum : struct, Enum
        => Core.Lang[$"{enumName}_{value}"];

}
