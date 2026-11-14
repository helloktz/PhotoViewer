using Avalonia;
using Avalonia.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Types;
using System;

namespace PhotoViewer.UI;

public partial class PhLogo : PhControl
{
    public PhLogo()
    {
        InitializeComponent();

        ApplyLogoSize();
        UpdateLogo();
    }

    public double LogoSize
    {
        get => GetValue(LogoSizeProperty);
        set => SetValue(LogoSizeProperty, value);
    }
    public static readonly StyledProperty<double> LogoSizeProperty =
        AvaloniaProperty.Register<PhLogo, double>(nameof(LogoSize), 48d);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LogoSizeProperty) ApplyLogoSize();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        UpdateLogo();
    }

    private void ApplyLogoSize()
    {

        Width = LogoSize;
        Height = LogoSize;

    }

    private void UpdateLogo()
    {
        if (PART_Logo is null) return;

        PART_Logo.Source = LoadLogoImage();
    }

    public static IImage? LoadLogoImage()
    {
        try
        {
            var iconPath = Core.Theme.GetIconPath(IgThemeIcon.AppLogo);
            var svgSource = SvgSource.Load(iconPath, null, SvgCodec.LocalResources);
            if (svgSource is not null) return new SvgImage { Source = svgSource };
        }
        catch { }

        using var stream = AssetLoader.Open(new Uri("avares://PhotoViewer.Lib/Assets/photo-viewer-logo.png"));
        return new Bitmap(stream);
    }

}
