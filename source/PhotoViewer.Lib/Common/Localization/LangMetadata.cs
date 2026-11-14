namespace PhotoViewer.Common.Localization;

public record LangMetadata
{
    public string Code { get; set; } = string.Empty;
    public string EnglishName { get; set; } = string.Empty;
    public string LocalName { get; set; } = string.Empty;
    public float MinVersion { get; set; }
}
