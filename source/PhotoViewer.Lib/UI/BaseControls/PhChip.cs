using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PhotoViewer.Common.Types;

namespace PhotoViewer.UI;

public enum PhChipVariant
{
    Neutral,
    Success,
    Warning,
    Danger,
    Info,
    Accent,
}

public class PhChip : PhControl
{
    public const double CHIP_CORNER_RADIUS = 4;
    public static readonly Thickness CHIP_PADDING = new(7, 1);

    private readonly Border _border;
    private readonly TextBlock _label;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<PhChip, string?>(nameof(Text));

    public PhChipVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
    public static readonly StyledProperty<PhChipVariant> VariantProperty =
        AvaloniaProperty.Register<PhChip, PhChipVariant>(nameof(Variant), PhChipVariant.Neutral);

    public PhChip()
    {
        _label = new TextBlock
        {
            FontSize = Const.FONT_SIZE_SMALL,
            IsTabStop = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _label[!TextBlock.TextProperty] = this[!TextProperty];

        _border = new Border
        {
            CornerRadius = new CornerRadius(CHIP_CORNER_RADIUS),
            Padding = CHIP_PADDING,
            Child = _label,
        };

        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        Content = _border;

        ApplyVariant(Variant);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == VariantProperty)
        {
            ApplyVariant(Variant);
        }
    }

    private void ApplyVariant(PhChipVariant variant)
    {
        ResxId bgId;
        ResxId? fgId;

        switch (variant)
        {
            case PhChipVariant.Success:
                bgId = ResxId.IG_BackgroundSuccessBrush;
                fgId = ResxId.IG_TextSuccessBrush;
                break;
            case PhChipVariant.Warning:
                bgId = ResxId.IG_BackgroundWarningBrush;
                fgId = ResxId.IG_TextWarningBrush;
                break;
            case PhChipVariant.Danger:
                bgId = ResxId.IG_BackgroundDangerBrush;
                fgId = ResxId.IG_TextDangerBrush;
                break;
            case PhChipVariant.Info:
                bgId = ResxId.IG_BackgroundInfoBrush;
                fgId = null;
                break;
            case PhChipVariant.Accent:
                bgId = ResxId.IG_AccentBrush;
                fgId = ResxId.AccentButtonForeground;
                break;
            default:
                bgId = ResxId.IG_BackgroundNeutralBrush;
                fgId = null;
                break;
        }

        _border[!Border.BackgroundProperty] = Resx.CreateBinding(bgId);

        if (fgId is not null)
        {
            _label[!TextBlock.ForegroundProperty] = Resx.CreateBinding(fgId.Value);
            _label.Opacity = 1;
        }
        else
        {
            _label.ClearValue(TextBlock.ForegroundProperty);
            _label.Opacity = 0.75;
        }
    }
}
