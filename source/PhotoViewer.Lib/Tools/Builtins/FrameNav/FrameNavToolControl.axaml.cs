using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PhotoViewer.Common;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Viewer;
using System;

namespace PhotoViewer.Tools;

public partial class FrameNavToolControl : PhControl, IToolControl
{

    public static string TOOL_ID => "Tool_FrameNav";
    public string ToolId => TOOL_ID;
    public string ToolName => Core.Lang[LangId.Menu_MnuFrameNav];
    public ResxIconId? ToolIcon => ResxIconId.IconToolFrameNav;
    public bool HasSettingsUI => false;
    public object? Settings { get; } = null;
    public ViewerControl Viewer { get; set; } = null!;

    public bool IsLivePhoto
    {
        get => GetValue(IsLivePhotoProperty);
        private set => SetValue(IsLivePhotoProperty, value);
    }
    public static readonly StyledProperty<bool> IsLivePhotoProperty =
        AvaloniaProperty.Register<FrameNavToolControl, bool>(nameof(IsLivePhoto));

    public bool HasMultiFrames
    {
        get => GetValue(HasMultiFramesProperty);
        private set => SetValue(HasMultiFramesProperty, value);
    }
    public static readonly StyledProperty<bool> HasMultiFramesProperty =
        AvaloniaProperty.Register<FrameNavToolControl, bool>(nameof(HasMultiFrames));

    public bool CanPlay
    {
        get => GetValue(CanPlayProperty);
        private set => SetValue(CanPlayProperty, value);
    }
    public static readonly StyledProperty<bool> CanPlayProperty =
        AvaloniaProperty.Register<FrameNavToolControl, bool>(nameof(CanPlay));

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        private set => SetValue(IsPlayingProperty, value);
    }
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<FrameNavToolControl, bool>(nameof(IsPlaying));

    public string FrameTextInfo
    {
        get => GetValue(FrameTextInfoProperty);
        private set => SetValue(FrameTextInfoProperty, value);
    }
    public static readonly StyledProperty<string> FrameTextInfoProperty =
        AvaloniaProperty.Register<FrameNavToolControl, string>(nameof(FrameTextInfo));

    public FrameNavToolControl()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateFrameInfo();
        UpdateTheme();

        Viewer.PhotoFrameChanged += Viewer_PhotoFrameChanged;

        PART_BtnViewFirstFrame.Click += PART_BtnViewFirstFrame_Click;
        PART_BtnViewPreviousFrame.Click += PART_BtnViewPreviousFrame_Click;
        PART_BtnToggleAnimation.Click += PART_BtnToggleAnimation_Click;
        PART_BtnViewNextFrame.Click += PART_BtnViewNextFrame_Click;
        PART_BtnViewLastFrame.Click += PART_BtnViewLastFrame_Click;
        PART_BtnExportFrame.Click += PART_BtnExportFrame_Click;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        Viewer.PhotoFrameChanged -= Viewer_PhotoFrameChanged;

        PART_BtnViewFirstFrame.Click -= PART_BtnViewFirstFrame_Click;
        PART_BtnViewPreviousFrame.Click -= PART_BtnViewPreviousFrame_Click;
        PART_BtnToggleAnimation.Click -= PART_BtnToggleAnimation_Click;
        PART_BtnViewNextFrame.Click -= PART_BtnViewNextFrame_Click;
        PART_BtnViewLastFrame.Click -= PART_BtnViewLastFrame_Click;
        PART_BtnExportFrame.Click -= PART_BtnExportFrame_Click;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();
        UpdateHotkeyTooltip();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        UpdateTheme();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == IsLivePhotoProperty)
        {
            UpdatePlayButtonTooltip();
        }
    }

    private void Viewer_PhotoFrameChanged(ViewerControl sender, PhotoFrameChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateFrameInfo(e);
        });
    }

    private async void PART_BtnViewFirstFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ViewFirstFrame);
    }

    private async void PART_BtnViewPreviousFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ViewPreviousFrame);
    }

    private async void PART_BtnToggleAnimation_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ToggleImageAnimation);
    }

    private async void PART_BtnViewNextFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ViewNextFrame);
    }

    private async void PART_BtnViewLastFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ViewLastFrame);
    }

    private async void PART_BtnExportFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ExportImageFrames);
    }

    private void UpdateTheme()
    {
        PART_PlaybackControlBorder.Background = Core.Theme.AccentColor
            .Blend(Core.Theme.InvertedBaseColor)
            .WithAlpha(20)
            .ToBrush();
    }

    private void UpdateFrameInfo(PhotoFrameChangedEventArgs? e = null)
    {
        var frameCount = e?.FrameCount ?? Viewer.Photo?.Metadata?.FrameCount ?? 0;
        var isAnimatedFormat = e?.CanAnimate ?? Viewer.Photo?.Metadata?.CanAnimate ?? false;

        HasMultiFrames = frameCount > 1;
        IsLivePhoto = e?.IsLivePhoto ?? Viewer.Photo?.Metadata?.IsLivePhoto ?? false;
        CanPlay = IsLivePhoto || isAnimatedFormat;
        IsPlaying = !IsLivePhoto && (e?.IsAnimating ?? false);

        if (frameCount > 0)
        {
            var currentFrame = Math.Max(0, Viewer.Photo?.FrameIndex ?? 0) + 1;
            var frameInfo = $"{currentFrame} / {frameCount}";

            FrameTextInfo = frameInfo;
        }
        else
        {
            FrameTextInfo = string.Empty;
        }
    }

    private void UpdateHotkeyTooltip()
    {
        ToolTip.SetTip(PART_BtnViewFirstFrame,
            AppAPIProvider.GetMenuTooltipText(LangId.Menu_MnuViewFirstFrame));

        ToolTip.SetTip(PART_BtnViewPreviousFrame,
            AppAPIProvider.GetMenuTooltipText(LangId.Menu_MnuViewPreviousFrame));

        UpdatePlayButtonTooltip();

        ToolTip.SetTip(PART_BtnViewNextFrame,
            AppAPIProvider.GetMenuTooltipText(LangId.Menu_MnuViewNextFrame));

        ToolTip.SetTip(PART_BtnViewLastFrame,
            AppAPIProvider.GetMenuTooltipText(LangId.Menu_MnuViewLastFrame));

        ToolTip.SetTip(PART_BtnExportFrame,
            AppAPIProvider.GetMenuTooltipText(LangId.Menu_MnuExportFrames));
    }

    private void UpdatePlayButtonTooltip()
    {
        var textKey = IsLivePhoto ? LangId._PlayMotionVideo : LangId.Menu_MnuToggleImageAnimation;

        ToolTip.SetTip(PART_BtnToggleAnimation,
            AppAPIProvider.GetMenuTooltipText(textKey, LangId.Menu_MnuToggleImageAnimation));
    }

}
