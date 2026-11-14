using Avalonia;
using PhotoViewer.Common;
using PhotoViewer.Common.Actions;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using PhotoViewer.Common.Types.JsonTypeConverters;
using System;
using System.IO;
using System.Text.Json.Serialization;

namespace PhotoViewer.UI;

[JsonSerializable(typeof(ToolbarItemModel))]
public partial class ToolbarItemModelJsonContext : JsonSerializerContext { }

public partial class ToolbarItemModel : PhReactive, IJsonOnDeserialized
{
    public static Config Config => Core.Config;

    public static string ID_SEPARATOR => "SEPARATOR";

    public static ToolbarItemModel Separator => new(ID_SEPARATOR);

    public static double InnerSpacing => Core.Config.ToolbarIconHeight / 6f;

    public static Thickness ItemPadding => new(Core.Config.ToolbarIconHeight / 4.5f);

    public static Point SeparatorEndPoint => new Point(0, Core.Config.ToolbarIconHeight);

    public static bool IsZoomModeAutoZoom => Core.Config.ZoomMode == Viewer.ZoomMode.AutoZoom;
    public static bool IsZoomModeLockZoom => Core.Config.ZoomMode == Viewer.ZoomMode.LockZoom;
    public static bool IsZoomModeScaleToWidth => Core.Config.ZoomMode == Viewer.ZoomMode.ScaleToWidth;
    public static bool IsZoomModeScaleToHeight => Core.Config.ZoomMode == Viewer.ZoomMode.ScaleToHeight;
    public static bool IsZoomModeScaleToFit => Core.Config.ZoomMode == Viewer.ZoomMode.ScaleToFit;
    public static bool IsZoomModeScaleToFill => Core.Config.ZoomMode == Viewer.ZoomMode.ScaleToFill;

    public static bool IsLoadingByName => Core.Config.ImageLoadingOrder == ImageOrderBy.Name;
    public static bool IsLoadingByRandom => Core.Config.ImageLoadingOrder == ImageOrderBy.Random;
    public static bool IsLoadingByFileSize => Core.Config.ImageLoadingOrder == ImageOrderBy.FileSize;
    public static bool IsLoadingByExtension => Core.Config.ImageLoadingOrder == ImageOrderBy.Extension;
    public static bool IsLoadingByDateCreated => Core.Config.ImageLoadingOrder == ImageOrderBy.DateCreated;
    public static bool IsLoadingByDateAccessed => Core.Config.ImageLoadingOrder == ImageOrderBy.DateAccessed;
    public static bool IsLoadingByDateModified => Core.Config.ImageLoadingOrder == ImageOrderBy.DateModified;
    public static bool IsLoadingByExifDateTaken => Core.Config.ImageLoadingOrder == ImageOrderBy.ExifDateTaken;
    public static bool IsLoadingByExifRating => Core.Config.ImageLoadingOrder == ImageOrderBy.ExifRating;

    public static bool IsLoadingAsc => Core.Config.ImageLoadingOrderType == ImageOrderType.Asc;
    public static bool IsLoadingDesc => Core.Config.ImageLoadingOrderType == ImageOrderType.Desc;

    public static bool IsColorChannelR => Core.ColorChannels.HasFlag(ColorChannels.R);
    public static bool IsColorChannelG => Core.ColorChannels.HasFlag(ColorChannels.G);
    public static bool IsColorChannelB => Core.ColorChannels.HasFlag(ColorChannels.B);
    public static bool IsColorChannelA => Core.ColorChannels.HasFlag(ColorChannels.A);

    public string Id
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
            _ = OnPropertyChanged(nameof(IsSeparator));
        }
    } = "";

    public string Image
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
            _ = OnPropertyChanged(nameof(ImagePath));
            _ = OnPropertyChanged(nameof(IsPlaceholderIconVisible));
        }
    } = "";

    public string Text
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
            _ = OnPropertyChanged(nameof(DisplayText));
            _ = OnPropertyChanged(nameof(IsTextVisible));
            _ = OnPropertyChanged(nameof(Tooltip));
        }
    } = "";

    public string DisplayText => Core.Lang[Text];

    public bool ShowText
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
            _ = OnPropertyChanged(nameof(IsTextVisible));
        }
    } = false;

    public string ConfigBinding
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
            _ = OnPropertyChanged(nameof(IsToggle));
        }
    } = string.Empty;

    public string ConfigBindingValue
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
        }
    } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumSafeConverter<ToolbarItemAlignment>))]
    public ToolbarItemAlignment Alignment
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
        }
    } = ToolbarItemAlignment.Left;

    public HotkeySingleAction? OnClick
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
            _ = OnPropertyChanged(nameof(HasClickAction));
            _ = OnPropertyChanged(nameof(IsPlaceholderIconVisible));
        }
    } = null;

    [JsonIgnore]
    public string ImagePath
    {
        get
        {
            var svgPath = string.Empty;
            if (string.IsNullOrWhiteSpace(Image)) return svgPath;

            if (File.Exists(Image)) return Image;

            if (!Enum.TryParse<IgThemeIcon>(Image, out var themeIconNameEnum)) return svgPath;

            var themeIconName = Core.Theme.GetIconPath(themeIconNameEnum);
            if (string.IsNullOrWhiteSpace(themeIconName)) return svgPath;

            svgPath = Path.Combine(Core.Theme.FolderPath, themeIconName);

            return svgPath;
        }
    }

    [JsonIgnore]
    public bool IsToggle => !string.IsNullOrWhiteSpace(ConfigBinding);

    [JsonIgnore]
    public bool HasClickAction => !string.IsNullOrWhiteSpace(OnClick?.Executable);

    [JsonIgnore]
    public bool IsChecked
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
        }
    } = false;

    [JsonIgnore]
    public int SourceIndex
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
        }
    } = -1;

    [JsonIgnore]
    public bool IsNotOverflow
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
        }
    } = true;

    [JsonIgnore]
    public bool IsSeparator => Id.Equals(ID_SEPARATOR, StringComparison.InvariantCultureIgnoreCase);

    [JsonIgnore]
    public bool IsPlaceholderIconVisible
    {
        get
        {
            if (!HasClickAction) return false;
            if (!string.IsNullOrEmpty(ImagePath)) return false;

            return string.IsNullOrWhiteSpace(Image) || Core.Theme.IsValid;
        }
    }

    [JsonIgnore]
    public bool IsTextVisible => ShowText && !string.IsNullOrWhiteSpace(Text);

    [JsonIgnore]
    public string HotkeyText
    {
        get; set
        {
            if (field == value) return;
            field = value;

            _ = OnPropertyChanged();
            _ = OnPropertyChanged(nameof(Tooltip));
        }
    } = string.Empty;

    public string? Tooltip
    {
        get
        {
            var text = DisplayText;
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (string.IsNullOrWhiteSpace(HotkeyText))
                return text;

            return $"{text} ({HotkeyText})";
        }
    }

    public ToolbarItemModel() { }

    public ToolbarItemModel(string id)
    {
        Id = id;
    }

    public void RefreshLanguage()
    {
        _ = OnPropertyChanged(nameof(DisplayText));
        _ = OnPropertyChanged(nameof(Tooltip));
    }

    public override string ToString()
    {
        return $"[{SourceIndex} | {Id} | {Text} | {nameof(IsNotOverflow)}={IsNotOverflow}";
    }

    public void OnDeserialized()
    {
        OnClick?.LangKey = Text;
    }

}

public enum ToolbarItemAlignment
{
    Left,
    Right,
}
