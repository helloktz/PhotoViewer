namespace PhotoViewer.Common.AppThemes;

public record IgThemeSettings
{

    public bool IsDarkMode { get; set; } = true;

    public string AppLogo { get; set; } = string.Empty;

    public string PreviewImage { get; set; } = string.Empty;
}
