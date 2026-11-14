using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;

namespace PhotoViewer.UI;

public class MotionPhotoOverlayControl : PhOverlay
{
    private const double ICON_SIZE = 20;

    private readonly PhToolButton _btnPlay;

    static MotionPhotoOverlayControl()
    {
        HorizontalAlignmentProperty.OverrideDefaultValue<MotionPhotoOverlayControl>(HorizontalAlignment.Center);
        VerticalAlignmentProperty.OverrideDefaultValue<MotionPhotoOverlayControl>(VerticalAlignment.Bottom);
        MarginProperty.OverrideDefaultValue<MotionPhotoOverlayControl>(new Thickness(0, 0, 0, 10));
        TransitionDirectionProperty.OverrideDefaultValue<MotionPhotoOverlayControl>(PhOverlayDirection.Bottom);

        PaddingProperty.OverrideDefaultValue<MotionPhotoOverlayControl>(new Thickness(1));
    }

    public MotionPhotoOverlayControl()
    {
        _btnPlay = new PhToolButton
        {
            Padding = new Thickness(8),
            Focusable = false,
            Content = new PathIcon
            {
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                Data = Resx.GetIcon(ResxIconId.IconLivePhoto),
            },
        };
        _btnPlay.Click += BtnPlay_Click;
        Content = _btnPlay;

        UpdateTooltip();
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();
        UpdateTooltip();
    }

    private async void BtnPlay_Click(object? sender, RoutedEventArgs e)
    {
        _ = await Core.API.RunApiAsync(API.IG_ToggleImageAnimation);
    }

    private void UpdateTooltip()
    {
        var tooltip = AppAPIProvider.GetMenuTooltipText(LangId._PlayMotionVideo, LangId.Menu_MnuToggleImageAnimation);
        ToolTip.SetTip(_btnPlay, tooltip);
    }

}
