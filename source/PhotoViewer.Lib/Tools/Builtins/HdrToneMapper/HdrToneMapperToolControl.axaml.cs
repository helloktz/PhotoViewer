using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Viewer;
using System;
using System.Globalization;
using System.Text.Json;

namespace PhotoViewer.Tools;

public partial class HdrToneMapperToolControl : PhControl, IToolControl
{
    private bool _isUpdatingUI;

    private static readonly HdrToneMappingMode[] _modes = Enum.GetValues<HdrToneMappingMode>();

    private static readonly (int Columns, Orientation RowOrientation)[] _layoutSteps =
    [
        (4, Orientation.Horizontal),
        (2, Orientation.Horizontal),
        (2, Orientation.Vertical),
        (1, Orientation.Vertical),
    ];

    public static string TOOL_ID => "Tool_HdrToneMapper";
    public string ToolId => TOOL_ID;
    public string ToolName => Core.Lang[LangId.Menu_MnuHdrToneMapper];
    public ResxIconId? ToolIcon => ResxIconId.IconToolHdrToneMapper;
    public bool HasSettingsUI => false;
    public object? Settings => Core.HdrToneMappingConfig;
    public ViewerControl Viewer { get; set; } = null!;

    public HdrToneMapperToolControl()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        PopulateModeItems();
        LoadConfigToUI();

        Viewer?.BeginLiveHdrToneMapping();

        PART_CmbMode.SelectionChanged += Mode_SelectionChanged;
        PART_SldExposure.ValueChanged += Slider_ValueChanged;
        PART_SldReferenceWhite.ValueChanged += Slider_ValueChanged;
        PART_SldHighlightCompression.ValueChanged += Slider_ValueChanged;
        PART_SldSaturation.ValueChanged += Slider_ValueChanged;
        PART_BtnReset.Click += PART_BtnReset_Click;

        if (Viewer is not null) Viewer.PhotoLoading += Viewer_PhotoLoading;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        PART_CmbMode.SelectionChanged -= Mode_SelectionChanged;
        PART_SldExposure.ValueChanged -= Slider_ValueChanged;
        PART_SldReferenceWhite.ValueChanged -= Slider_ValueChanged;
        PART_SldHighlightCompression.ValueChanged -= Slider_ValueChanged;
        PART_SldSaturation.ValueChanged -= Slider_ValueChanged;
        PART_BtnReset.Click -= PART_BtnReset_Click;

        if (Viewer is not null) Viewer.PhotoLoading -= Viewer_PhotoLoading;

        Viewer?.EndLiveHdrToneMapping();

        base.OnUnloaded(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(new Size(double.PositiveInfinity, availableSize.Height));
        ApplyResponsiveLayout(availableSize.Width);

        return base.MeasureOverride(availableSize);
    }

    private void Viewer_PhotoLoading(ViewerControl sender, PhotoLoadingEventArgs e)
    {
        UpdateControlsState();
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();
        PART_BtnReset.Text = Core.Lang[LangId.Tool_Hdr_BtnReset];
        UpdateValueLabels();
    }

    private void Mode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUI) return;

        SaveUIToConfig();
    }

    private void Slider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingUI) return;

        SaveUIToConfig();
    }

    private void PART_BtnReset_Click(object? sender, RoutedEventArgs e)
    {
        var mode = Core.HdrToneMappingConfig.Mode;
        Core.HdrToneMappingConfig = new HdrToneMappingOptions { Mode = mode };

        LoadConfigToUI();
        ToolRegistry.SaveToolSettings(this);
        Viewer?.ReapplyHdrToneMapping();
    }

    public void LoadSettings(JsonElement? jsonEl)
    {
        var opts = jsonEl?.Deserialize(HdrToneMappingOptionsJsonContext.Default.HdrToneMappingOptions);
        if (opts is not null) Core.HdrToneMappingConfig = opts;
    }

    public JsonElement? SaveSettings()
    {
        return JsonSerializer.SerializeToElement(Core.HdrToneMappingConfig,
            HdrToneMappingOptionsJsonContext.Default.HdrToneMappingOptions);
    }

    private void PopulateModeItems()
    {
        if (PART_CmbMode.ItemCount > 0) return;

        foreach (var mode in _modes)
        {
            PART_CmbMode.Items.Add(new ComboBoxItem { Content = Enum.GetName(mode) });
        }
    }

    private void ApplyResponsiveLayout(double availableWidth)
    {
        var columnWidth = 0d;
        foreach (var column in PART_SlidersGrid.Children)
        {
            columnWidth = Math.Max(columnWidth, column.DesiredSize.Width);
        }

        if (columnWidth <= 0) return;

        var comboWidth = PART_CmbMode.DesiredSize.Width;
        var resetWidth = PART_BtnReset.DesiredSize.Width;
        var modeLabelWidth = PART_LblMode.DesiredSize.Width;
        var groupRowWidth = comboWidth + PART_ModeResetRow.Spacing + resetWidth;

        var groupHorizontalWidth = Math.Max(modeLabelWidth, groupRowWidth);
        var groupVerticalWidth = Math.Max(modeLabelWidth, Math.Max(comboWidth, resetWidth));

        var contentWidth = availableWidth - Padding.Left - Padding.Right;
        var columns = _layoutSteps[^1].Columns;
        var rowOrientation = _layoutSteps[^1].RowOrientation;

        foreach (var step in _layoutSteps)
        {
            var groupWidth = step.RowOrientation == Orientation.Horizontal
                ? groupHorizontalWidth
                : groupVerticalWidth;
            var stepWidth = (step.Columns * columnWidth) + PART_RootPanel.Spacing + groupWidth;
            if (stepWidth > contentWidth) continue;

            columns = step.Columns;
            rowOrientation = step.RowOrientation;
            break;
        }

        if (PART_SlidersGrid.Columns != columns) PART_SlidersGrid.Columns = columns;
        if (PART_ModeResetRow.Orientation != rowOrientation) PART_ModeResetRow.Orientation = rowOrientation;
    }

    private void LoadConfigToUI()
    {
        _isUpdatingUI = true;
        try
        {
            var cfg = Core.HdrToneMappingConfig;

            PART_CmbMode.SelectedIndex = Array.IndexOf(_modes, cfg.Mode);
            PART_SldExposure.Value = cfg.Exposure;
            PART_SldReferenceWhite.Value = cfg.ReferenceWhiteNits;
            PART_SldHighlightCompression.Value = cfg.HighlightCompression;
            PART_SldSaturation.Value = cfg.Saturation;

            UpdateValueLabels();
            UpdateControlsState();
        }
        finally
        {
            _isUpdatingUI = false;
        }
    }

    private void UpdateControlsState()
    {

        PART_RootPanel.IsEnabled = Viewer?.CanReapplyHdrToneMapping() ?? false;

        var idx = PART_CmbMode.SelectedIndex;
        var mode = idx >= 0 ? _modes[idx] : HdrToneMappingMode.BT2408;
        var enabled = mode != HdrToneMappingMode.None;

        PART_SldExposure.IsEnabled = enabled;
        PART_SldReferenceWhite.IsEnabled = enabled;
        PART_SldHighlightCompression.IsEnabled = enabled;
        PART_SldSaturation.IsEnabled = enabled;
    }

    private void SaveUIToConfig()
    {
        var cfg = Core.HdrToneMappingConfig;
        cfg.Mode = _modes[Math.Max(0, PART_CmbMode.SelectedIndex)];
        cfg.Exposure = PART_SldExposure.Value;
        cfg.ReferenceWhiteNits = PART_SldReferenceWhite.Value;
        cfg.HighlightCompression = PART_SldHighlightCompression.Value;
        cfg.Saturation = PART_SldSaturation.Value;

        Core.Config.EnableHdrToneMapping = cfg.Mode != HdrToneMappingMode.None;

        UpdateValueLabels();
        UpdateControlsState();
        ToolRegistry.SaveToolSettings(this);

        Viewer?.ReapplyHdrToneMapping();
    }

    private void UpdateValueLabels()
    {
        PART_LblExposure.LangParams = PART_SldExposure.Value.ToString("0.0#", CultureInfo.InvariantCulture);
        PART_LblReferenceWhite.LangParams = PART_SldReferenceWhite.Value.ToString("0", CultureInfo.InvariantCulture);
        PART_LblHighlightCompression.LangParams = PART_SldHighlightCompression.Value.ToString("0.00", CultureInfo.InvariantCulture);
        PART_LblSaturation.LangParams = PART_SldSaturation.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

}
