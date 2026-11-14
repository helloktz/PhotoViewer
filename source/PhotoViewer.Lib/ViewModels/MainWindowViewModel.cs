using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;

namespace PhotoViewer.ViewModels;

public partial class MainWindowViewModel : PhReactive
{
    public static Config Config => Core.Config;

    public static ToolbarControlModel ToolbarVM { get; } = new();

    public virtual PhWindow Window { get; }

    public string Title
    {
        get; set
        {
            if (field.Equals(value)) return;

            field = value;
            OnPropertyChanged();
        }
    } = BHelper.AppDisplayName;

    public MainWindowViewModel(PhWindow window)
    {
        Window = window;
    }

}
