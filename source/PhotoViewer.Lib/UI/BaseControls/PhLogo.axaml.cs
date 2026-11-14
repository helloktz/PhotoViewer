/*
ImageGlass - A Fast, Seamless Photo Viewer
Copyright (C) 2010 - 2027 DUONG DIEU PHAP
Project homepage: https://imageglass.org

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Types;

namespace PhotoViewer.UI;

/// <summary>
/// The app logo from the current theme pack.
/// </summary>
public partial class PhLogo : PhControl
{
    public PhLogo()
    {
        InitializeComponent();

        ApplyLogoSize();
        UpdateLogo();
    }


    #region Public Properties

    /// <summary>
    /// Gets or sets the logo size.
    /// </summary>
    public double LogoSize
    {
        get => GetValue(LogoSizeProperty);
        set => SetValue(LogoSizeProperty, value);
    }
    public static readonly StyledProperty<double> LogoSizeProperty =
        AvaloniaProperty.Register<PhLogo, double>(nameof(LogoSize), 48d);

    #endregion // Public Properties



    #region Overrides

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

    #endregion // Overrides



    #region Private Methods

    private void ApplyLogoSize()
    {

        Width = LogoSize;
        Height = LogoSize;


    }


    /// <summary>
    /// Shows the theme pack logo, falling back to the bundled app icon.
    /// </summary>
    private void UpdateLogo()
    {
        if (PART_Logo is null) return;

        PART_Logo.Source = LoadLogoImage();
    }

    #endregion // Private Methods



    #region Public Methods

    /// <summary>
    /// Loads the theme pack logo, falling back to the bundled app icon; <c>null</c> when neither loads.
    /// </summary>
    public static IImage? LoadLogoImage()
    {
        // 1. try load theme logo
        try
        {
            var iconPath = Core.Theme.GetIconPath(IgThemeIcon.AppLogo);
            var svgSource = SvgSource.Load(iconPath, null, SvgCodec.LocalResources);
            if (svgSource is not null) return new SvgImage { Source = svgSource };
        }
        catch { }

        // 2. load the default logo
        using var stream = Resx.GetDefaultWindowIconAsStream();
        if (stream is null) return null;

        return Bitmap.DecodeToHeight(stream, 256);
    }

    #endregion // Public Methods

}
