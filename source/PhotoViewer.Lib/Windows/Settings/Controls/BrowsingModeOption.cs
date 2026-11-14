using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;

namespace PhotoViewer.Common.Windows;

public sealed class BrowsingModeOption(BrowsingMode value, LangId nameKey, LangId descriptionKey)
    : PhReactive
{

    public BrowsingMode Value => value;

    public string Name => Core.Lang[nameKey];

    public string Description => Core.Lang[descriptionKey];

    public void RefreshLang()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }
}
