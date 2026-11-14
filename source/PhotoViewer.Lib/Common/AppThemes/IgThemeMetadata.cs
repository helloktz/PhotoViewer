namespace PhotoViewer.Common.AppThemes;

public record IgThemeMetadata
{

    public double Version { get; set; } = 10;

    public string Description { get; set; } = "Photo Viewer Theme Configuration File";

    public string Docs { get; set; } = string.Empty;
}
