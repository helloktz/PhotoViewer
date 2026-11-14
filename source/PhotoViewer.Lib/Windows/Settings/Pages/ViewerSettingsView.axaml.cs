using PhotoViewer.Common.Localization;
using PhotoViewer.UI.Viewer;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PhotoViewer.Common.Windows;

public partial class ViewerSettingsView : SettingsPageView
{
    private const string ZOOM_SEPARATOR = "; ";

    private const string DEFAULT_ZOOM_LEVELS = "5; 10; 15; 20; 30; 40; 50; 60; 70; 80; 90; 100; "
        + "125; 150; 175; 200; 250; 300; 350; 400; 500; 600; 700; 800; 1000; 1200; 1500; 1800; "
        + "2100; 2500; 3000; 3500; 4500; 6000; 8000; 10000";

    public ViewerSettingsView()
    {
        InitializeComponent();
    }

    public ViewerSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        BindToggle(PART_NavButtons, ConfigId.EnableNavigationButtons,
            LangId.Settings_EnableNavigationButtons, LangId.Settings_Appearance, true);
        BindToggle(PART_VectorRenderer, ConfigId.EnableVectorRenderer,
            LangId.Settings_EnableVectorRenderer, LangId.Settings_Appearance, true);
        BindEnumDropdown(PART_ScrollbarMode, ConfigId.ViewerScrollbarMode, ScrollbarMode.Never,
            LangId.Settings_ViewerScrollbarMode, LangId.Settings_Appearance);
        BindEnumDropdown(PART_CheckerboardMode, ConfigId.CheckerboardMode, CheckerboardType.None,
            LangId.Settings_CheckerboardMode, LangId.Settings_Appearance);

        BindToggle(PART_FreePan, ConfigId.EnableFreePan,
            LangId.Settings_EnableFreePan, LangId.Settings_Panning);
        BindDoubleInput(PART_PanMargin, ConfigId.PanMargin,
            LangId.Settings_PanMargin, LangId.Settings_Panning);
        BindSlider(PART_PanSpeed, ConfigId.PanSpeed,
            LangId.Settings_PanSpeed, LangId.Settings_Panning, 20d, PART_PanSpeedLabel);

        BindEnumDropdown(PART_InterpolationScaleDown, ConfigId.ImageInterpolationScaleDown,
            ImageInterpolation.LinearMipmapNearest,
            LangId.Settings_ImageInterpolation_ScaleDown, LangId.Settings_Zooming);
        BindEnumDropdown(PART_InterpolationScaleUp, ConfigId.ImageInterpolationScaleUp,
            ImageInterpolation.Nearest,
            LangId.Settings_ImageInterpolation_ScaleUp, LangId.Settings_Zooming);
        BindSlider(PART_ZoomSpeed, ConfigId.ZoomSpeed,
            LangId.Settings_ZoomSpeed, LangId.Settings_Zooming, 0d, PART_ZoomSpeedLabel);

        BuildZoomLevels();
    }

    private void BuildZoomLevels()
    {
        var levels = VM.GetValue(ConfigId.ZoomLevels, Array.Empty<double>());

        PART_UseSmoothZooming.IsChecked = levels.Length == 0;
        PART_ZoomLevels.Text = FormatZoomLevels(levels);

        PART_UseSmoothZooming.IsCheckedChanged += (_, _) =>
        {
            UpdateZoomLevelsEnabled();
            StageZoomLevels(reformat: false);
        };

        PART_ZoomLevels.TextChanged += (_, _) => StageZoomLevels(reformat: false);
        PART_ZoomLevels.LostFocus += (_, _) => StageZoomLevels(reformat: true);

        SetLocalizedText(PART_LoadDefaultZoomLevels, LangId.Settings_LoadDefaultZoomLevels);
        PART_LoadDefaultZoomLevels.Click += (_, _) => PART_ZoomLevels.Text = DEFAULT_ZOOM_LEVELS;

        UpdateZoomLevelsEnabled();

        RegisterSearchKey(PART_ZoomLevels, LangId.Settings_ZoomLevels,
            ConfigId.ZoomLevels, LangId.Settings_Zooming);
    }

    private void UpdateZoomLevelsEnabled()
    {
        if (Config.IsConfigLocked(ConfigId.ZoomLevels))
        {
            PART_UseSmoothZooming.IsEnabled = false;
            PART_ZoomLevels.IsEnabled = false;
            PART_LoadDefaultZoomLevels.IsEnabled = false;
            return;
        }

        var smooth = PART_UseSmoothZooming.IsChecked ?? false;
        PART_ZoomLevels.IsEnabled = !smooth;
        PART_LoadDefaultZoomLevels.IsEnabled = !smooth;
    }

    private void StageZoomLevels(bool reformat)
    {
        if (PART_UseSmoothZooming.IsChecked ?? false)
        {
            VM.SetValue(ConfigId.ZoomLevels, Array.Empty<double>());
            return;
        }

        var tokens = (PART_ZoomLevels.Text ?? string.Empty)
            .Split([';', ',', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var seen = new HashSet<double>();
        var factors = new List<double>(tokens.Length);
        foreach (var token in tokens)
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
                && double.IsFinite(percent) && percent > 0)
            {
                var factor = percent / 100d;
                if (seen.Add(factor)) factors.Add(factor);
            }
        }
        factors.Sort();

        VM.SetValue(ConfigId.ZoomLevels, factors.ToArray());

        if (reformat)
        {
            var normalized = FormatZoomLevels(factors);
            if (!string.Equals(PART_ZoomLevels.Text, normalized, StringComparison.Ordinal))
                PART_ZoomLevels.Text = normalized;
        }
    }

    private static string FormatZoomLevels(IEnumerable<double> factors)
        => string.Join(ZOOM_SEPARATOR,
            factors.Select(f => (f * 100d).ToString("0.##", CultureInfo.InvariantCulture)));

}
