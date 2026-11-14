using PhotoViewer.Common.Windows;

namespace PhotoViewer.ViewModels;

public class MainWindowModel : MainWindowViewModel
{

    public override MainWindow Window => (MainWindow)base.Window;

    public MainWindowModel(MainWindow window) : base(window)
    {

    }
}
