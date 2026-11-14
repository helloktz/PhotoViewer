using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.Tools;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class ToolsSettingsView : SettingsPageView
{
    private const double NAME_MAX_WIDTH = 220;
    private const double HOTKEY_MAX_WIDTH = 200;

    private readonly List<ExternalTool> _tools = [];

    public ToolsSettingsView()
    {
        InitializeComponent();
    }

    public ToolsSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        _tools.AddRange(VM.GetValue(ConfigId.Tools, new ObservableCollection<ExternalTool>()));

        SetLocalizedText(PART_AddTool, LangId._Add);
        PART_AddTool.Click += async (_, _) => await AddOrEditToolAsync(null);

        AddLangRefresher(RebuildTable);

        RegisterSearchKey(PART_AddTool, LangId.Settings_Nav_Tools, ConfigId.Tools, LangId.Settings_Nav_Tools);

        DisableIfLocked(ConfigId.Tools, PART_AddTool, PART_Table);
    }

    private void StageTools() => VM.SetValue(ConfigId.Tools, new ObservableCollection<ExternalTool>(_tools));

    private async Task AddOrEditToolAsync(ExternalTool? existing)
    {
        var isIdLocked = existing is not null && PredefinedExternalTools.IsPredefined(existing.ToolId);
        var win = new ToolEditWindow(existing, CollectTakenIds(except: existing), isIdLocked);
        if (await win.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (win.ResultTool is not { } tool) return;

        var index = existing is not null ? _tools.IndexOf(existing) : -1;
        if (index >= 0) _tools[index] = tool;
        else _tools.Add(tool);

        StageTools();
        RebuildTable();
    }

    public Task EditToolAsync(string toolId)
    {
        if (string.IsNullOrEmpty(toolId)) return Task.CompletedTask;

        var existing = _tools.FirstOrDefault(t => string.Equals(t.ToolId, toolId, StringComparison.OrdinalIgnoreCase));
        var seed = existing ?? PredefinedExternalTools.Find(toolId) ?? new ExternalTool { ToolId = toolId };

        return AddOrEditToolAsync(seed);
    }

    private void DeleteTool(ExternalTool tool)
    {
        if (PredefinedExternalTools.IsPredefined(tool.ToolId)) return;
        if (!_tools.Remove(tool)) return;

        StageTools();
        RebuildTable();
    }

    private HashSet<string> CollectTakenIds(ExternalTool? except)
    {
        var set = new HashSet<string>(_tools.Select(t => t.ToolId), StringComparer.OrdinalIgnoreCase);
        if (except is not null) set.Remove(except.ToolId);
        return set;
    }

    private void RebuildTable()
    {
        PhTableColumn[] columns =
        [
            new() { Header = Core.Lang[LangId._Name] },
            new() { Header = Core.Lang[LangId._Executable], Star = true },
            new() { Header = string.Empty },
            new() { Header = Core.Lang[LangId._Hotkeys] },
        ];

        var rows = _tools.Select(tool =>
        {
            var actions = new List<PhTableAction>
            {
                new() { Icon = ResxIconId.IconEdit, Tooltip = Core.Lang[LangId._Edit], Click = () => _ = AddOrEditToolAsync(tool) },
            };

            if (!PredefinedExternalTools.IsPredefined(tool.ToolId))
            {
                actions.Add(new() { Icon = ResxIconId.IconClose, Tooltip = Core.Lang[LangId._Delete], Click = () => DeleteTool(tool) });
            }

            return new PhTableRow
            {
                Cells =
                [
                    NameCell(tool),
                    ExecutableCell(tool),
                    IntegratedCell(tool),
                    HotkeysCell(tool),
                ],
                Actions = actions,
            };
        }).ToList();

        PART_Table.EmptyText = Core.Lang[LangId._Empty];
        PART_Table.Build(columns, rows);
    }

    private static Control HotkeysCell(ExternalTool tool)
    {
        if (tool.Hotkeys.Length == 0) return new Panel();

        var panel = new WrapPanel { MaxWidth = HOTKEY_MAX_WIDTH };
        foreach (var hk in tool.Hotkeys)
        {
            panel.Children.Add(new PhHotkeyChip(hk.KeyString)
            {
                Margin = new Thickness(0, 0, 6, 0),
            });
        }

        return PhTableControl.WrapCell(panel);
    }

    private Control NameCell(ExternalTool tool)
    {
        var displayName = tool.DisplayName;

        var btn = new PhButton
        {
            Variant = PhButtonVariant.Link,
            Text = displayName,
            MaxWidth = NAME_MAX_WIDTH,
            HorizontalAlignment = HorizontalAlignment.Left,
            ClipToBounds = true,
        };
        ToolTip.SetTip(btn, displayName);
        btn.Click += (_, _) => _ = AddOrEditToolAsync(tool);

        return PhTableControl.WrapCell(btn);
    }

    private static Control ExecutableCell(ExternalTool tool)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(ExecutableLine(tool.Executable, secondary: false));

        if (!string.IsNullOrWhiteSpace(tool.Arguments))
        {
            stack.Children.Add(ExecutableLine(tool.Arguments, secondary: true));
        }

        return PhTableControl.WrapCell(stack);
    }

    private static SelectableTextBlock ExecutableLine(string text, bool secondary)
    {
        var tb = new SelectableTextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Top,
            IsTabStop = false,
        };
        if (!string.IsNullOrEmpty(text)) ToolTip.SetTip(tb, text);

        if (secondary)
        {
            tb.FontSize = Const.FONT_SIZE_SMALL;
            tb.Opacity = 0.7;
        }

        return tb;
    }

    private static Control IntegratedCell(ExternalTool tool)
    {
        if (!tool.IsIntegrated) return new Panel();

        var glyph = new Path
        {
            Data = Resx.GetIcon(ResxIconId.IconIntegrated),
            Width = Const.FONT_SIZE_BODY,
            Height = Const.FONT_SIZE_BODY,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
        };
        glyph[!Shape.StrokeProperty] = new DynamicResourceExtension("PhAccentFill");

        var hit = new Border { Background = Brushes.Transparent, Child = glyph };
        ToolTip.SetTip(hit, Core.Lang[LangId.Settings_Tools_Integrated]);

        return PhTableControl.WrapCell(hit);
    }

}
