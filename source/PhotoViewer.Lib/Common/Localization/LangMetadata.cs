namespace PhotoViewer.Common.Localization;

public record LangMetadata
{
    public string Code { get; set; } = "en-US";
    public string EnglishName { get; set; } = "English";
    public string LocalName { get; set; } = "English";
    public float MinVersion { get; set; } = 10;
}
