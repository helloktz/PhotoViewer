using Avalonia;
using Avalonia.Data;
using System;

namespace PhotoViewer.UI;

public partial class PhSearchBoxControl : PhControl
{

    public PhTextBox TextBox => PART_TextBox;

    public bool IsClearButtonPointerOver => PART_ClearButton.IsPointerOver;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<PhSearchBoxControl, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<PhSearchBoxControl, string?>(nameof(PlaceholderText));

    public event EventHandler? TextChanged;

    public PhSearchBoxControl()
    {
        InitializeComponent();

        PART_TextBox.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        PART_ClearButton.Click += (_, _) => Clear();
    }

    public void FocusSearch() => PART_TextBox.Focus();

    public void Clear()
    {
        PART_TextBox.Text = string.Empty;
        PART_TextBox.Focus();
    }

}
