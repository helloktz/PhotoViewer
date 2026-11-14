using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using System.Linq;

namespace PhotoViewer.UI;

public class PhCommandPreview : PhControl
{
    private string PreviewFakePath { get; } = BHelper.OS == OSType.Windows
        ? @"C:\sample\photo.webp"
        : "/sample/photo.webp";

    private readonly SelectableTextBlock _previewText;

    public string? Executable
    {
        get => GetValue(ExecutableProperty);
        set => SetValue(ExecutableProperty, value);
    }
    public static readonly StyledProperty<string?> ExecutableProperty =
        AvaloniaProperty.Register<PhCommandPreview, string?>(nameof(Executable));

    public string? Argument
    {
        get => GetValue(ArgumentProperty);
        set => SetValue(ArgumentProperty, value);
    }
    public static readonly StyledProperty<string?> ArgumentProperty =
        AvaloniaProperty.Register<PhCommandPreview, string?>(nameof(Argument));

    public PhCommandPreview()
    {
        _previewText = new SelectableTextBlock
        {
            FontFamily = new FontFamily(Const.FONT_CODE),
            FontSize = Const.FONT_SIZE_SMALL,
            TextWrapping = TextWrapping.Wrap,
        };

        var box = new Border
        {
            Padding = new Thickness(10, 7),
            Child = _previewText,
        };
        box[!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_BackgroundNeutralBrush);
        box[!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius);

        Content = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new PhTextBlock { LangKey = LangId._CommandPreview },
                box,
            },
        };

        UpdatePreview();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == ExecutableProperty || e.Property == ArgumentProperty)
        {
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        var (exe, args) = BHelper.BuildExeArgs(
            Executable ?? string.Empty, Argument ?? string.Empty, PreviewFakePath);

        var join = exe.EndsWith(':') ? string.Empty : " ";
        _previewText.Text = string.Join(join, new[] { exe, args }.Where(s => !string.IsNullOrEmpty(s)));
    }
}
