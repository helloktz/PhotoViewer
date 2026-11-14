using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using System;
using System.Globalization;

namespace PhotoViewer.Common.Windows;

public partial class SlideshowSettingsView : SettingsPageView
{

    private const double MIN_INTERVAL = SlideshowProvider.MIN_INTERVAL_MS / 1000.0;

    public SlideshowSettingsView()
    {
        InitializeComponent();
    }

    public SlideshowSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        BindToggle(PART_FullscreenSlideshow, ConfigId.EnableFullscreenSlideshow,
            LangId.Settings_EnableFullscreenSlideshow, LangId.Settings_Slideshow_Appearance, true);
        BindToggle(PART_SlideshowCountdown, ConfigId.EnableSlideshowCountdown,
            LangId.Settings_EnableSlideshowCountdown, LangId.Settings_Slideshow_Appearance, true);
        BindColorPicker(PART_BgColor, ConfigId.SlideshowBackgroundColor, "#000000",
            LangId._BackgroundColor, LangId.Settings_Slideshow_Appearance);

        BindToggle(PART_LoopSlideshow, ConfigId.EnableLoopSlideshow,
            LangId.Settings_EnableLoopSlideshow, LangId.Settings_Slideshow_Playback, true);
        BindToggle(PART_RandomInterval, ConfigId.EnableSlideshowRandomInterval,
            LangId.Settings_EnableSlideshowRandomInterval, LangId.Settings_Slideshow_Playback);

        BindDoubleInput(PART_SlideshowInterval, ConfigId.SlideshowInterval,
            LangId.Settings_SlideshowInterval, LangId.Settings_Slideshow_Playback, 5d, MIN_INTERVAL);
        BindDoubleInput(PART_SlideshowIntervalTo, ConfigId.SlideshowIntervalTo,
            LangId.Settings_SlideshowInterval, LangId.Settings_Slideshow_Playback, 5d, MIN_INTERVAL);

        PART_RandomInterval.IsCheckedChanged += (_, _) => UpdateIntervalUI();
        PART_SlideshowInterval.TextChanged += (_, _) => UpdateIntervalHeading();
        PART_SlideshowIntervalTo.TextChanged += (_, _) => UpdateIntervalHeading();
        AddLangRefresher(UpdateIntervalHeading);
        UpdateIntervalUI();

        BindUIntInput(PART_NotifySound, ConfigId.SlideshowImagesToNotifySound,
            LangId.Settings_SlideshowImagesToNotifySound, LangId.Settings_Slideshow_Playback);

        BindTransitionEditor(PART_Transition, PART_TransitionDuration, PART_TransitionError,
            PART_OpenTransitionsFolder,
            ConfigId.SlideshowTransition, ConfigId.SlideshowTransitionDuration, 1000u, LangId.Settings_Slideshow_Playback);
    }

    private void UpdateIntervalUI()
    {
        var isRandom = PART_RandomInterval.IsChecked ?? false;
        PART_IntervalToSection.IsVisible = isRandom;
        PART_IntervalFromLabel.IsVisible = isRandom;

        PART_SlideshowInterval.Width = isRandom ? 150 : 200;
        UpdateIntervalHeading();
    }

    private void UpdateIntervalHeading()
    {
        var heading = $"{Core.Lang[LangId.Settings_SlideshowInterval]} {FormatInterval(PART_SlideshowInterval.Text)}";
        if (PART_RandomInterval.IsChecked ?? false)
        {
            heading += $" - {FormatInterval(PART_SlideshowIntervalTo.Text)}";
        }

        PART_IntervalHeading.Text = heading;
    }

    private static string FormatInterval(string? secondsText)
    {
        _ = double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);
        var clamped = Math.Max(MIN_INTERVAL, seconds);

        return TimeSpan.FromSeconds(clamped).ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

}
