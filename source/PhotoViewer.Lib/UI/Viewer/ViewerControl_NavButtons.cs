using Avalonia;
using PhotoViewer.Common.Types;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{
    internal NavButtonsInfo _navButtons = new();

    public bool EnableNavButtons
    {
        get => GetValue(EnableNavButtonsProperty);
        set => SetValue(EnableNavButtonsProperty, value);
    }

    public static readonly StyledProperty<bool> EnableNavButtonsProperty =
        AvaloniaProperty.Register<ViewerControl, bool>(nameof(EnableNavButtons), defaultValue: true);

    public event TEventHandler<ViewerControl, NavButtonClickedEventArgs>? NavButtonClicked;

    internal void OnNavButtonClicked(NavButtonDirection direction)
    {
        NavButtonClicked?.Invoke(this, new NavButtonClickedEventArgs(direction));
    }
}
