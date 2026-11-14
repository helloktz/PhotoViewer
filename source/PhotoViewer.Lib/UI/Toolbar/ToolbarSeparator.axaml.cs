using Avalonia.Controls;
using System;

namespace PhotoViewer.UI;

public partial class ToolbarSeparator : StackPanel, IToolbarItem
{
    protected override Type StyleKeyOverride => typeof(StackPanel);
    public ToolbarItemModel VM => (ToolbarItemModel)DataContext!;

    public ToolbarSeparator()
    {
        InitializeComponent();
    }
}
