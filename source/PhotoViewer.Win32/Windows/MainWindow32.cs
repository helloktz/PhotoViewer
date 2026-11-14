using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoViewer.Common;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.Common.Windows;
using PhotoViewer.Win32.Common;
using PhotoViewer.Win32.Common.ServiceProviders;

namespace PhotoViewer.Win32.Windows;

public partial class MainWindow32 : MainWindow
{

    public override bool UseCustomBackdrop => true;

    public MainWindow32()
    {

    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        Core.ColorProfileProvider = new Win32ColorProfileProvider();
        Core.ColorProfileProvider.Changed += ColorProfileProvider_Changed;
        Core.ColorProfileProvider.Initialize(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        Core.ColorProfileProvider?.Changed -= ColorProfileProvider_Changed;
    }

    protected override void OnIgBackdropStyleChanged(BackdropStyle style)
    {
        _canUseBackdrop = !BHelper.IsWindows10 && style != BackdropStyle.None;

        UpdateBackground(true);

        var type = style switch
        {
            BackdropStyle.Mica => SystemBackdropType.Mica,
            BackdropStyle.MicaAlt => SystemBackdropType.MicaAlt,
            BackdropStyle.Acrylic => SystemBackdropType.Acrylic,
            BackdropStyle.None => SystemBackdropType.Auto,
            _ => SystemBackdropType.Auto,
        };

        Win32WindowApi.SetWindowBackdrop(Handle, type);
    }

    protected override void OnIgTitleBarIconVisibilityChanged(bool show)
    {
        Win32WindowApi.SetTitleBarIconVisible(Handle, show);
    }

    private void ColorProfileProvider_Changed(IWindowColorProfileProvider sender, ColorProfileChangedEventArgs e)
    {
        if (Core.Config.ColorProfile == nameof(ColorProfileOption.CurrentMonitorProfile))
        {
            Core.UpdateDestColorProfile(requiresPhotoReload: false);
        }
    }

}
