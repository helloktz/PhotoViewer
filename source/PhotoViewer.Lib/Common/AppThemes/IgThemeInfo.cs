namespace PhotoViewer.Common.AppThemes;

public record IgThemeInfo
{

    public string Name { get; set; } = string.Empty;
    public double Version { get; set; } = 1.0d;
    public string Description { get; set; } = string.Empty;
}
