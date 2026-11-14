using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using System;

namespace PhotoViewer.UI;

public class PhHotkeyChip : PhControl
{
    private const double KEYCAP_LIP = 2;
    private const double GLYPH_DIM_OPACITY = 0.4;
    private static readonly CornerRadius BASE_RADIUS = new(6);
    private static readonly CornerRadius FACE_RADIUS = new(4);

    private readonly Border _face;
    private readonly StackPanel _contentEl;
    private readonly TextBlock _label;
    private readonly PhToolButton _removeBtn;
    private readonly Path _removeIcon;
    private bool _showDelete;
    private PhHotkeyChipTone _tone;

    public event EventHandler? Deleted;

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    public bool ShowDelete
    {
        get => _showDelete;
        set
        {
            if (_showDelete == value) return;
            _showDelete = value;
            UpdateDeleteVisibility();
        }
    }

    public PhHotkeyChipTone Tone
    {
        get => _tone;
        set
        {
            if (_tone == value) return;
            _tone = value;
            ApplyTone();
        }
    }

    public PhHotkeyChip() : this(string.Empty)
    {
    }

    public PhHotkeyChip(string text, bool showDelete = false)
    {
        _label = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(Const.FONT_CODE),
            FontSize = Const.FONT_SIZE_SMALL,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _removeIcon = new Path
        {
            Width = 10,
            Height = 10,
            Data = Resx.GetIcon(ResxIconId.IconClose),
            Stretch = Stretch.Uniform,
            Opacity = GLYPH_DIM_OPACITY,
        };
        _removeIcon[!Shape.FillProperty] = Resx.CreateBinding(ResxId.TextControlForeground);

        _removeBtn = new PhToolButton
        {
            Padding = new Thickness(4),
            Content = _removeIcon,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(_removeBtn, Core.Lang[LangId._Delete]);
        _removeBtn.Click += (_, _) => Deleted?.Invoke(this, EventArgs.Empty);

        _removeBtn.PointerEntered += (_, _) => _removeIcon.Opacity = 1;
        _removeBtn.PointerExited += (_, _) => _removeIcon.Opacity = GLYPH_DIM_OPACITY;

        _contentEl = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _label },
        };

        _face = new Border
        {
            CornerRadius = FACE_RADIUS,
            Margin = new Thickness(1, 1, 1, KEYCAP_LIP),
            Child = _contentEl,
        };
        _face[!Border.BackgroundProperty] = new DynamicResourceExtension("PhButtonBackground");

        var keycapBase = new Border
        {
            CornerRadius = BASE_RADIUS,
            Child = _face,
        };
        keycapBase[!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.TextControlBorderBrush);

        Content = keycapBase;

        _showDelete = showDelete;
        UpdateDeleteVisibility();
        ApplyTone();
    }

    private void ApplyTone()
    {
        switch (_tone)
        {
            case PhHotkeyChipTone.Accent:
                _label.FontWeight = FontWeight.SemiBold;
                _label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PhAccentFill");
                break;

            case PhHotkeyChipTone.Danger:
                _label.FontWeight = FontWeight.SemiBold;
                _label[!TextBlock.ForegroundProperty] = Resx.CreateBinding(ResxId.IG_TextDangerBrush);
                break;

            default:
                _label.FontWeight = FontWeight.Normal;
                _label[!TextBlock.ForegroundProperty] = Resx.CreateBinding(ResxId.TextControlForeground);
                break;
        }
    }

    private void UpdateDeleteVisibility()
    {
        if (_showDelete)
        {
            if (!_contentEl.Children.Contains(_removeBtn)) _contentEl.Children.Add(_removeBtn);
            _face.Padding = new Thickness(6, 2, 4, 2);
        }
        else
        {
            _contentEl.Children.Remove(_removeBtn);
            _face.Padding = new Thickness(4, 2);
        }
    }

}

public enum PhHotkeyChipTone
{
    Normal,
    Accent,
    Danger,
}
