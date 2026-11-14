using Avalonia.Media;
using PhotoViewer.Common.Types;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PhotoViewer.Common.AppThemes;

[JsonSerializable(typeof(IgTheme))]
public partial class IgThemeJsonContext : JsonSerializerContext { }

public partial class IgTheme : PhReactive
{

    public IgThemeMetadata _Metadata { get; set; } = new();
    public IgThemeInfo Info { get; set; } = new();
    public IgThemeSettings Settings { get; set; } = new();
    public IgThemeColors Colors { get; set; } = new();
    public Dictionary<string, string> ToolbarIcons { get; set; } = [];

    public static float SPEC_VERSION => 10;

    public static string CONFIG_FILE => "igtheme.json";

    [JsonIgnore]
    public string FolderPath { get; set; } = "";

    [JsonIgnore]
    public string DisplayFolderPath => BHelper.GetRealPlatformPath(FolderPath);

    [JsonIgnore]
    public string FolderName => Path.GetFileName(FolderPath);

    [JsonIgnore]
    public string ConfigFilePath => Path.Combine(FolderPath, IgTheme.CONFIG_FILE);

    [JsonIgnore]
    public bool IsValid { get; set; } = false;

    [JsonIgnore]
    public Color AccentColor => BHelper.ColorFromHex(Colors.AccentColor);

    [JsonIgnore]
    public bool UseSystemAccent => string.IsNullOrWhiteSpace(Colors.AccentColor);

    [JsonIgnore]
    public Color BaseColor => Settings.IsDarkMode
        ? Avalonia.Media.Colors.Black
        : Avalonia.Media.Colors.White;

    [JsonIgnore]
    public Color InvertedBaseColor => Settings.IsDarkMode
        ? Avalonia.Media.Colors.White
        : Avalonia.Media.Colors.Black;

    public IgTheme Load(string themeFolderPath)
    {
        return BHelper.RunSync(() => LoadAsync(themeFolderPath));
    }

    public async Task<IgTheme> LoadAsync(string themeFolderPath)
    {
        FolderPath = themeFolderPath;

        var th = await Task.Run(async () =>
        {
            if (!File.Exists(ConfigFilePath)) return null;

            try
            {
                var jsonOptions = BHelper.CreateJsonOptions();
                var jsonContext = new IgThemeJsonContext(jsonOptions);

                var th = await BHelper.ReadJsonFromFileAsync(ConfigFilePath, jsonContext.IgTheme);

                return th;
            }
            catch { }

            return null;
        }).ConfigureAwait(false);

        IsValid = th != null;

        if (th != null)
        {
            _Metadata = th._Metadata;
            Info = th.Info;
            Settings = th.Settings;
            Colors = th.Colors;
            ToolbarIcons = th.ToolbarIcons;
        }

        return this;
    }

    public string GetIconPath(IgThemeIcon iconName)
    {
        var svgPath = "";

        try
        {
            var themeIconName = "";

            if (iconName == IgThemeIcon.AppLogo)
            {
                themeIconName = Settings.AppLogo;
            }
            else if (!ToolbarIcons.TryGetValue(iconName.ToString(), out themeIconName))
            {
                return svgPath;
            }

            svgPath = Path.Combine(FolderPath, themeIconName);
            if (!File.Exists(svgPath)) return "";
        }
        catch { }

        return svgPath;
    }

}
