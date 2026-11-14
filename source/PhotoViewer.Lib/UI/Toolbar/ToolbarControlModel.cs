using Avalonia;
using PhotoViewer.Common;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;

namespace PhotoViewer.UI;

public class ToolbarControlModel : PhReactive
{
    public static Config Config => Core.Config;
    public static double ItemSpacing => Core.Config.ToolbarIconHeight / 6f;
    public static Thickness ItemPadding => ToolbarItemModel.ItemPadding;

    public ToolbarItemModel ButtonOverflowVM { get; } = new()
    {
        Text = nameof(LangId.Menu_MnuToolbarOverflow),
        Image = nameof(IgThemeIcon.MainMenu),
    };

    public ToolbarItemModel ButtonMenuVM
    {
        get; set
        {
            if (field == value) return;

            field = value;
            _ = OnPropertyChanged();
        }
    } = new()
    {
        Text = nameof(LangId.Menu_MnuMain),
        Image = nameof(IgThemeIcon.MainMenu),
    };

    public void RefreshLanguage()
    {
        ButtonMenuVM.RefreshLanguage();
        ButtonOverflowVM.RefreshLanguage();
    }

}
