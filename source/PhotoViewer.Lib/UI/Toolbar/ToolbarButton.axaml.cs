using Avalonia.Interactivity;
using PhotoViewer.Common;

namespace PhotoViewer.UI;

public partial class ToolbarButton : PhToolButton, IToolbarItem
{
    public ToolbarItemModel VM => (ToolbarItemModel)DataContext!;

    public ToolbarButton()
    {
        DataContext = new ToolbarItemModel();
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        Core.Config.PropertyChanged += Config_PropertyChanged;

        RefreshIconState();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        Core.Config.PropertyChanged -= Config_PropertyChanged;
    }

    protected override void OnIgDropdownMenuOpened(RoutedEventArgs e)
    {
        base.OnIgDropdownMenuOpened(e);
        IsChecked = true;
    }

    protected override void OnIgDropdownMenuClosed(RoutedEventArgs e)
    {
        base.OnIgDropdownMenuClosed(e);
        IsChecked = false;
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);

        RefreshIconState();
    }

    private void RefreshIconState()
    {
        _ = VM.OnPropertyChanged(nameof(VM.ImagePath));
        _ = VM.OnPropertyChanged(nameof(VM.IsPlaceholderIconVisible));
    }

    private void Config_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (nameof(Core.Config.ToolbarIconHeight).Equals(e.PropertyName))
        {
            _ = VM.OnPropertyChanged(nameof(VM.InnerSpacing));
            _ = VM.OnPropertyChanged(nameof(VM.ItemPadding));
            _ = VM.OnPropertyChanged(nameof(VM.SeparatorEndPoint));
        }
    }

}
