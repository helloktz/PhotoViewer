using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using System;

namespace PhotoViewer.Tools;

public partial class ToolHostControl : PhControl
{

    [Content]
    public IToolControl? Tool
    {
        get => GetValue(PluginContentProperty);
        set => SetValue(PluginContentProperty, value);
    }
    public static readonly StyledProperty<IToolControl?> PluginContentProperty =
        AvaloniaProperty.Register<ToolHostControl, IToolControl?>(nameof(Tool));

    public string CloseButtonTooltipText
    {
        get => GetValue(CloseButtonTooltipTextProperty);
        private set => SetValue(CloseButtonTooltipTextProperty, value);
    }
    public static readonly StyledProperty<string> CloseButtonTooltipTextProperty =
        AvaloniaProperty.Register<ToolHostControl, string>(nameof(CloseButtonTooltipText));

    public string SettingsButtonTooltipText
    {
        get => GetValue(SettingsButtonTooltipTextProperty);
        private set => SetValue(SettingsButtonTooltipTextProperty, value);
    }
    public static readonly StyledProperty<string> SettingsButtonTooltipTextProperty =
        AvaloniaProperty.Register<ToolHostControl, string>(nameof(SettingsButtonTooltipText));

    public bool HasSettings
    {
        get => GetValue(HasSettingsProperty);
        private set => SetValue(HasSettingsProperty, value);
    }
    public static readonly StyledProperty<bool> HasSettingsProperty =
        AvaloniaProperty.Register<ToolHostControl, bool>(nameof(HasSettings));

    public string ToolName
    {
        get => GetValue(ToolNameProperty);
        private set => SetValue(ToolNameProperty, value);
    }
    public static readonly StyledProperty<string> ToolNameProperty =
        AvaloniaProperty.Register<ToolHostControl, string>(nameof(ToolName));

    public Geometry? ToolIcon
    {
        get => GetValue(ToolIconProperty);
        private set => SetValue(ToolIconProperty, value);
    }
    public static readonly StyledProperty<Geometry?> ToolIconProperty =
        AvaloniaProperty.Register<ToolHostControl, Geometry?>(nameof(ToolIcon));

    public ToolHostControl()
    {
        InitializeComponent();
        IsContentVisible = false;

        LayoutUpdated += ToolHostControl_LayoutUpdated;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        CloseButtonTooltipText = Core.Lang[LangId._Close];
        SettingsButtonTooltipText = Core.Lang[LangId.Menu_MnuSettings];
        ToolName = Tool?.ToolName ?? string.Empty;
    }

    private void ToolHostControl_LayoutUpdated(object? sender, EventArgs e)
    {
        if (Tool is not Layoutable toolEl) return;

        var sideRoom = (PART_ToolSlot.Bounds.Width - toolEl.DesiredSize.Width) / 2;
        var isCrowded = sideRoom < PART_Watermark.DesiredSize.Width;

        PART_Watermark.Classes.Set("crowded", isCrowded);
    }

    private async void PART_BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        if (Tool is IToolControl tool)
        {
            _ = await Core.API.RunApiAsync(API.IG_CloseTool, tool.ToolId);
        }
    }

    private async void PART_BtnSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (Tool is not IToolControl tool) return;
        if (!tool.HasSettingsUI) return;

        await tool.ShowSettingsWindowAsync();
    }

    public bool OpenTool(IToolControl? newTool)
    {
        if (newTool is null) return false;

        if (Tool is IToolControl tool)
        {
            throw new InvalidOperationException($"IGE: The current tool (ID = {tool.ToolId}) must be closed before opening another tool");
        }

        HasSettings = newTool.HasSettingsUI;
        ToolName = newTool.ToolName;
        ToolIcon = Resx.GetIcon(newTool.ToolIcon);

        Tool = newTool;
        IsContentVisible = true;

        return true;
    }

    public void CloseTool(string toolId)
    {
        if (Tool is not IToolControl tool) return;
        if (tool.ToolId != toolId) return;

        CloseCurrentTool();
    }

    public void CloseCurrentTool()
    {
        if (Tool is not IToolControl) return;

        try
        {
            Tool = null;
            IsContentVisible = false;
        }
        catch { }
    }

}
