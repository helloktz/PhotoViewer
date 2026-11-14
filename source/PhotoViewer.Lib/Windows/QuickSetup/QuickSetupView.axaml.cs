using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Windows;

public partial class QuickSetupView : PhControl
{

    private readonly List<Control> _stepPanels;
    private readonly List<Border> _dots = [];

    private bool _isPopulatingLangs;
    private string _selectedLangValue = "English";
    private readonly Action _loadLangAction;

    private Lang _previewLang = Core.Lang;

    private static DefaultAppScope DefaultViewerScope
        => Core.ShellProvider?.GetDefaultViewerScope() ?? DefaultAppScope.CurrentUser;

    public Lang PreviewLang => _previewLang;

    public event EventHandler? PreviewLanguageChanged;

    public int StepCount => _stepPanels.Count;

    private static bool CanSetDefaultViewer => BHelper.OS == OSType.Windows
        && Core.ShellProvider?.IsDefaultViewerConfigurable != false;

    private static bool CanRegisterAppMenuEntry => Core.ShellProvider?.CanRegisterAppMenuEntry == true;

    public int CurrentStep { get; private set; } = 1;

    public string SelectedLanguageValue => _selectedLangValue;

    public bool IsProfessional { get; private set; }

    public QuickSetupView()
    {
        InitializeComponent();

        _loadLangAction = () => _ = LoadSelectedLanguageAsync();

        _stepPanels = [PART_Step1, PART_Step2];

        if (CanSetDefaultViewer) _stepPanels.Add(PART_Step3);
        else PART_Step3.IsVisible = false;

        if (CanRegisterAppMenuEntry) _stepPanels.Add(PART_StepAppMenu);
        else PART_StepAppMenu.IsVisible = false;

        BuildStepDots();

        PART_LanguageList.SelectionChanged += Language_SelectionChanged;

        PART_BtnStandard.Click += (_, _) => SelectProfile(false);
        PART_BtnProfessional.Click += (_, _) => SelectProfile(true);
        SelectProfile(false);

        PART_BtnRegisterViewer.Click += async (_, _) => await AppAPIProvider.SetDefaultPhotoViewerAsync(
            true, TopLevel.GetTopLevel(this) as PhWindow, _previewLang);

        PART_BtnRegisterAppMenu.Click += async (_, _) => await RegisterAppMenuEntryAsync();

        LocalizeAll();
        SetStep(1);
        _ = LoadLanguagesAsync();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);

        UpdateStepDots();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        AnimateStepIn(_stepPanels[CurrentStep - 1], isFirstOpen: true);
    }

    public void ShowStep(int step)
    {
        SetStep(step);
        AnimateStepIn(_stepPanels[CurrentStep - 1], isFirstOpen: false);
    }

    private void SetStep(int step)
    {
        CurrentStep = Math.Clamp(step, 1, StepCount);

        for (var i = 0; i < _stepPanels.Count; i++)
        {
            _stepPanels[i].IsVisible = i == CurrentStep - 1;
        }

        UpdateStepInfo();
        UpdateStepDots();
    }

    private static void AnimateStepIn(Control panel, bool isFirstOpen)
    {
        var duration = TimeSpan.FromMilliseconds(isFirstOpen ? 420 : 260);
        var offset = isFirstOpen ? 26 : 12;
        var easing = new CubicEaseOut();

        panel.Transitions = null;
        panel.Opacity = 0;
        panel.RenderTransform = TransformOperations.Parse($"translateY({offset}px)");

        panel.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = easing },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = duration, Easing = easing },
        ];

        Dispatcher.UIThread.Post(() =>
        {
            panel.Opacity = 1;
            panel.RenderTransform = TransformOperations.Parse("translateY(0px)");
        }, DispatcherPriority.Render);
    }

    private void Language_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingLangs || PART_LanguageList.SelectedItem is not Lang lang) return;

        _selectedLangValue = ToConfigValue(lang);
        BHelper.Debounce(120, _loadLangAction);
    }

    private async Task LoadSelectedLanguageAsync()
    {
        var path = Lang.ResolveFilePath(_selectedLangValue);
        var lang = new Lang(path);
        await lang.LoadAsync();

        Dispatcher.UIThread.Post(() =>
        {
            _previewLang = lang;
            LocalizeAll();
            PreviewLanguageChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task LoadLanguagesAsync()
    {
        var packs = await Lang.LoadAllLanguagePacksAsync();

        List<Lang> langs = [new(string.Empty), .. packs];

        _isPopulatingLangs = true;
        PART_LanguageList.ItemsSource = langs;

        var current = Core.Config.Language;
        PART_LanguageList.SelectedItem = langs
            .FirstOrDefault(l => ToConfigValue(l).Equals(current, StringComparison.OrdinalIgnoreCase))
            ?? langs[0];

        _selectedLangValue = current;
        _isPopulatingLangs = false;
    }

    private static string ToConfigValue(Lang lang) => lang.IsBuiltIn ? "English" : lang.FileName;

    private void SelectProfile(bool professional)
    {
        IsProfessional = professional;

        PART_BtnStandard.IsChecked = !professional;
        PART_BtnProfessional.IsChecked = professional;

        PART_ChkColorManagement.IsChecked = professional;
        PART_ChkHdrToneMapping.IsChecked = professional;
        PART_ChkExplorerSortOrder.IsChecked = professional;
        PART_ChkRawThumbnail.IsChecked = !professional;
    }

    private async Task RegisterAppMenuEntryAsync()
    {
        PART_BtnRegisterAppMenu.IsEnabled = false;

        var ok = await AppAPIProvider.RegisterAppMenuEntryAsync(true,
            TopLevel.GetTopLevel(this) as PhWindow, _previewLang);

        PART_BtnRegisterAppMenu.IsEnabled = !ok;
    }

    private void BuildStepDots()
    {
        for (var i = 0; i < StepCount; i++)
        {
            var dot = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            _dots.Add(dot);
            PART_StepDots.Children.Add(dot);
        }
    }

    private void UpdateStepDots()
    {
        var accent = Core.AccentColor.ToBrush();
        var inactive = Resx.Get<IBrush>(ResxId.IG_BorderNeutralBrush);

        for (var i = 0; i < _dots.Count; i++)
        {
            var isActive = i == CurrentStep - 1;
            _dots[i].Width = isActive ? 22 : 8;
            _dots[i].Background = isActive ? accent : inactive;
        }
    }

    private void UpdateStepInfo()
    {
        PART_StepInfo.Text = _previewLang[LangId.QuickSetup_StepInfo, CurrentStep, StepCount];
    }

    private void LocalizeAll()
    {
        var lang = _previewLang;

        PART_LblLanguage.Text = lang[LangId.QuickSetup_SelectLanguage];

        PART_LblSelectProfile.Text = lang[LangId.QuickSetup_SelectProfile];
        PART_LblStandard.Text = lang[LangId.QuickSetup_StandardUser];
        PART_LblProfessional.Text = lang[LangId.QuickSetup_ProfessionalUser];
        PART_LblApplied.Text = lang[LangId.QuickSetup_SettingsWillBeApplied];
        PART_LblColorManagement.Text = lang[LangId.Settings_ColorManagement];
        PART_LblHdrToneMapping.Text = lang[LangId.Settings_EnableHdrToneMapping];
        PART_LblExplorerSort.Text = lang[LangId.Settings_EnableExplorerSortOrder];
        PART_LblRawThumbnail.Text = lang[LangId.Settings_EnableOnlyLoadRawPreview];
        PART_LblProfileNote.Text = lang[LangId.QuickSetup_SettingProfileDescription];

        PART_LblDefaultViewer.Text = lang[LangId.Settings_DefaultPhotoViewer];
        PART_LblDefaultViewerDesc.Text = lang[LangId.QuickSetup_RegisterImageFormats];
        PART_LblDefaultViewerWarning.Text = lang[LangId.Settings_UnmanagedSettingReminder];
        PART_LblDefaultViewerScope.Text = lang[DefaultViewerScope == DefaultAppScope.LocalMachine
            ? LangId.Settings_DefaultPhotoViewer_ScopePerMachine
            : LangId.Settings_DefaultPhotoViewer_ScopePerUser];
        PART_BtnRegisterViewer.Text = lang[LangId._Register];

        PART_LblAppMenu.Text = lang[LangId.Settings_AppMenuEntry];
        PART_LblAppMenuDesc.Text = lang[LangId.Settings_AppMenuEntry_Description];
        PART_LblAppMenuWarning.Text = lang[LangId.Settings_UnmanagedSettingReminder];
        PART_BtnRegisterAppMenu.Text = lang[LangId._Register];

        UpdateStepInfo();
    }

}
