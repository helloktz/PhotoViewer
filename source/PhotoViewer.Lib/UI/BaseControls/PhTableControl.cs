using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.UI;

public partial class PhTableControl : PhControl
{
    private static readonly Thickness CELL_PADDING = new(10, 6);
    private static readonly TimeSpan REVEAL_DURATION = TimeSpan.FromMilliseconds(120);
    private const double FLASH_OPACITY = 0.18;
    private const int MAX_SCROLL_PASSES = 4;

    private readonly Border _frame;
    private readonly RowsPanel _panel;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _emptyLabel;

    private readonly List<RowSlot> _rows = [];

    private Control[] _headerSpanners = [];
    private Control?[] _headerCells = [];
    private Border? _headerBg;

    private int _hoveredRow = -1;
    private int _focusedRow = -1;
    private RowSlot? _flashSlot;
    private DispatcherTimer? _flashTimer;

    public string EmptyText
    {
        get => _emptyLabel.Text ?? string.Empty;
        set => _emptyLabel.Text = value;
    }

    public PhTableControl()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;

        _panel = new RowsPanel(this) { Background = Brushes.Transparent };
        _panel.PointerMoved += Panel_PointerMoved;
        _panel.PointerExited += Panel_PointerExited;

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _panel,
        };

        _frame = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = _scroll,
        };
        _frame[!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_BackgroundNeutralBrush);
        _frame[!Border.BorderBrushProperty] = Resx.CreateBinding(ResxId.IG_BorderControlBrush);
        _frame[!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius);

        _emptyLabel = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            FontStyle = FontStyle.Italic,
            Opacity = 0.6,
            IsVisible = false,
        };

        Content = new Panel { Children = { _frame, _emptyLabel } };
    }

    public void Build(IReadOnlyList<PhTableColumn> columns, IReadOnlyList<PhTableRow> rows)
    {
        _flashTimer?.Stop();
        _flashSlot = null;
        _rows.Clear();
        _headerSpanners = [];
        _headerCells = [];
        _headerBg = null;
        _hoveredRow = _focusedRow = -1;

        _scroll.Offset = default;

        var hasRows = rows.Count > 0;
        _emptyLabel.IsVisible = !hasRows;
        _frame.IsVisible = hasRows;

        if (!hasRows)
        {
            _panel.Reset(columns, 0);
            return;
        }

        var contentCols = columns.Count;
        var totalCols = contentCols + 1;

        _headerBg = new Border { IsHitTestVisible = false };
        ApplyHeaderBackground();
        _headerSpanners = [_headerBg, HLine(ResxId.IG_BorderControlBrush, VerticalAlignment.Bottom)];
        foreach (var spanner in _headerSpanners) spanner.ZIndex = 1;

        _headerCells = new Control?[totalCols];
        for (var c = 0; c < contentCols; c++)
        {
            var column = columns[c];
            Control cell = column.HeaderContent is { } content
                ? WrapHeaderCell(content)
                : HeaderCell(column.Header);
            cell.ZIndex = 1;
            _headerCells[c] = cell;
        }

        for (var i = 0; i < rows.Count; i++) _rows.Add(BuildRow(rows[i], i, totalCols));

        _panel.Reset(columns, totalCols);
    }

    internal int HeaderVisualCount
    {
        get
        {
            var count = _headerSpanners.Length;
            foreach (var cell in _headerCells)
            {
                if (cell is not null) count++;
            }

            return count;
        }
    }

    public static Control TextCell(string text, double maxWidth = 0,
        bool selectable = false, bool muted = false, FontFamily? font = null)
    {
        TextBlock tb = selectable ? new SelectableTextBlock() : new TextBlock();
        tb.Text = text;
        tb.Padding = CELL_PADDING;
        tb.VerticalAlignment = VerticalAlignment.Center;
        tb.TextTrimming = TextTrimming.CharacterEllipsis;
        tb.IsTabStop = false;
        if (maxWidth > 0) tb.MaxWidth = maxWidth;
        if (font is not null) tb.FontFamily = font;

        if (muted)
        {
            tb.FontStyle = FontStyle.Italic;
            tb.Opacity = 0.6;
        }
        else if (!string.IsNullOrEmpty(text))
        {
            ToolTip.SetTip(tb, text);
        }

        return tb;
    }

    public static Border WrapCell(Control content)
    {
        content.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Padding = CELL_PADDING, Child = content };
    }

    public void FlashRow(string key)
    {
        var index = _rows.FindIndex(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) FlashRow(index);
    }

    public void FlashRow(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        var slot = _rows[index];

        Dispatcher.UIThread.Post(() =>
        {
            if (!_rows.Contains(slot)) return;

            BringRowIntoView(slot);

            _flashTimer?.Stop();
            if (_flashSlot is not null && _flashSlot != slot) _flashSlot.Flash.Opacity = 0;

            _flashSlot = slot;
            var pulses = 0;
            slot.Flash.Opacity = FLASH_OPACITY;

            _flashTimer = new DispatcherTimer { Interval = REVEAL_DURATION + TimeSpan.FromMilliseconds(140) };
            _flashTimer.Tick += (_, _) =>
            {
                slot.Flash.Opacity = slot.Flash.Opacity > 0 ? 0 : FLASH_OPACITY;
                if (++pulses >= 5)
                {
                    slot.Flash.Opacity = 0;
                    _flashSlot = null;
                    _flashTimer!.Stop();
                }
            };
            _flashTimer.Start();
        }, DispatcherPriority.Loaded);
    }

    private void BringRowIntoView(RowSlot slot)
    {
        for (var pass = 0; pass < MAX_SCROLL_PASSES; pass++)
        {
            var before = _scroll.Offset.Y;

            ScrollRowIntoView(slot);
            _panel.UpdateLayout();

            if (slot.IsRealized && Math.Abs(_scroll.Offset.Y - before) < 0.5) break;
        }
    }

    private void ScrollRowIntoView(RowSlot slot)
    {
        var headerHeight = _panel.HeaderHeight;
        var top = headerHeight + slot.Y;
        var bottom = top + _panel.RowHeightOf(slot);

        var viewport = _scroll.Viewport.Height;
        if (viewport <= 0) return;

        var viewTop = _scroll.Offset.Y + headerHeight;
        var viewBottom = _scroll.Offset.Y + viewport;
        if (top >= viewTop && bottom <= viewBottom) return;

        var target = top < viewTop ? top - headerHeight : bottom - viewport;
        var max = Math.Max(0, _scroll.Extent.Height - viewport);
        _scroll.Offset = new Vector(_scroll.Offset.X, Math.Clamp(target, 0, max));
    }

    private void Panel_PointerMoved(object? sender, PointerEventArgs e)
    {
        var y = e.GetPosition(_panel).Y;
        var overHeader = y >= _scroll.Offset.Y && y < _scroll.Offset.Y + _panel.HeaderHeight;

        var index = overHeader ? -1 : _panel.RowAt(y);
        if (index == _hoveredRow) return;

        var previous = _hoveredRow;
        _hoveredRow = index;
        UpdateReveal(previous, index);
    }

    private void Panel_PointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoveredRow == -1) return;

        var previous = _hoveredRow;
        _hoveredRow = -1;
        UpdateReveal(previous, -1);
    }

    private void UpdateReveal(params int[] changed)
    {
        foreach (var index in changed)
        {
            if (index >= 0 && index < _rows.Count) RefreshRowReveal(_rows[index]);
        }
    }

    private void RefreshRowReveal(RowSlot slot)
    {
        var isHovered = slot.Index == _hoveredRow;
        slot.Actions.Opacity = isHovered || slot.Index == _focusedRow ? 1 : 0;
        slot.Highlight.Opacity = isHovered ? 1 : 0;
    }

    private bool RowHasFocus(int row) => _rows[row].Buttons.Any(b => b.IsFocused);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Tab) return;

        var modifiers = e.KeyModifiers & ~KeyModifiers.Shift;
        if (modifiers != KeyModifiers.None) return;

        if (MoveActionFocus(back: e.KeyModifiers.HasFlag(KeyModifiers.Shift))) e.Handled = true;
    }

    private bool MoveActionFocus(bool back)
    {
        if (!TryFindFocusedRow(out var row, out var focused)) return false;

        var step = back ? -1 : 1;

        var current = FocusablesOf(_rows[row]);
        var at = current.IndexOf(focused);
        if (at >= 0)
        {
            var next = at + step;
            if (next >= 0 && next < current.Count) return current[next].Focus();
        }

        for (var r = row + step; r >= 0 && r < _rows.Count; r += step)
        {
            var slot = _rows[r];
            var candidates = FocusablesOf(slot);
            if (candidates.Count == 0) continue;

            BringRowIntoView(slot);

            var target = back ? candidates[^1] : candidates[0];
            if (target.Focus()) return true;
        }

        return false;
    }

    private static List<Control> FocusablesOf(RowSlot slot)
    {
        var found = new List<Control>();

        foreach (var cell in slot.Cells)
        {
            if (cell is not null) CollectFocusables(cell, found);
        }

        return found;
    }

    private static void CollectFocusables(Control control, List<Control> found)
    {
        if (!control.IsVisible || !control.IsEnabled) return;

        if (control.Focusable) found.Add(control);

        foreach (var child in control.GetVisualChildren())
        {
            if (child is Control c) CollectFocusables(c, found);
        }
    }

    private bool TryFindFocusedRow(out int row, out Control focused)
    {
        for (var r = 0; r < _rows.Count; r++)
        {
            foreach (var candidate in FocusablesOf(_rows[r]))
            {
                if (!candidate.IsFocused) continue;

                row = r;
                focused = candidate;
                return true;
            }
        }

        row = -1;
        focused = null!;
        return false;
    }

    private RowSlot BuildRow(PhTableRow spec, int index, int totalCols)
    {
        var highlight = new Border { IsHitTestVisible = false, Opacity = 0 };
        highlight[!Border.BackgroundProperty] = new DynamicResourceExtension("PhListItemFillSecondary");
        highlight.Transitions = new Transitions { FadeTransition() };

        var flash = new Border { IsHitTestVisible = false, Opacity = 0 };
        flash[!Border.BackgroundProperty] = new DynamicResourceExtension("PhAccentFill");
        flash.Transitions = new Transitions { FadeTransition() };

        var layers = new List<Control> { highlight, flash };

        if (index > 0) layers.Add(HLine(ResxId.IG_BorderNeutralBrush, VerticalAlignment.Top));

        var contentCols = totalCols - 1;
        var cells = new Control?[totalCols];
        for (var c = 0; c < contentCols && c < spec.Cells.Count; c++) cells[c] = spec.Cells[c];

        var (actionsCell, buttons) = BuildActionsCell(spec.Actions, index);
        cells[contentCols] = actionsCell;

        return new RowSlot(index, spec.Key, highlight, flash, actionsCell, buttons)
        {
            Layers = [.. layers],
            Cells = cells,
        };
    }

    private void ApplyHeaderBackground()
    {
        if (_headerBg is null) return;

        var c = Resx.Get<ISolidColorBrush>(ResxId.IG_BackgroundNeutralBrush)?.Color ?? Colors.Transparent;
        _headerBg.Background = new SolidColorBrush(new Color(255, c.R, c.G, c.B));
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        ApplyHeaderBackground();
    }

    private static TextBlock HeaderCell(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Padding = CELL_PADDING,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Border WrapHeaderCell(Control content)
    {
        content.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Padding = CELL_PADDING, Child = content };
    }

    private static Border HLine(ResxId brushId, VerticalAlignment align)
    {
        var line = new Border { Height = 1, VerticalAlignment = align };
        line[!Border.BackgroundProperty] = Resx.CreateBinding(brushId);
        return line;
    }

    private (Border cell, List<PhToolButton> buttons) BuildActionsCell(
        IReadOnlyList<PhTableAction> actions, int rowIndex)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var buttons = new List<PhToolButton>(actions.Count);
        foreach (var action in actions)
        {
            var btn = BuildActionButton(action, rowIndex);
            buttons.Add(btn);
            panel.Children.Add(btn);
        }

        var cell = new Border { Padding = new Thickness(8, 2), Opacity = 0, Child = panel };
        cell.Transitions = new Transitions { FadeTransition() };

        return (cell, buttons);
    }

    private PhToolButton BuildActionButton(PhTableAction action, int rowIndex)
    {
        var glyph = new Path
        {
            Width = Const.FONT_SIZE_BODY,
            Height = Const.FONT_SIZE_BODY,
            Data = Resx.GetIcon(action.Icon),
            Stretch = Stretch.Uniform,
        };
        glyph[!Shape.FillProperty] = Resx.CreateBinding(ResxId.TextControlForeground);

        var btn = new PhToolButton
        {
            Padding = new Thickness(7),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = action.IsVisible,
            Content = glyph,
        };
        if (!string.IsNullOrEmpty(action.Tooltip)) ToolTip.SetTip(btn, action.Tooltip);

        var click = action.Click;
        btn.Click += (_, _) => click?.Invoke();

        btn.GotFocus += (_, _) =>
        {
            var previous = _focusedRow;
            _focusedRow = rowIndex;
            UpdateReveal(previous, rowIndex);
        };
        btn.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_focusedRow == rowIndex && rowIndex < _rows.Count && !RowHasFocus(rowIndex))
            {
                _focusedRow = -1;
                UpdateReveal(rowIndex);
            }
        });

        return btn;
    }

    private static DoubleTransition FadeTransition() => new()
    {
        Property = OpacityProperty,
        Duration = REVEAL_DURATION,
    };

}

public sealed class PhTableColumn
{

    public string Header { get; set; } = string.Empty;

    public Control? HeaderContent { get; set; }

    public bool Star { get; set; }

    public double MinWidth { get; set; }
}

public sealed class PhTableAction
{

    public ResxIconId Icon { get; set; }

    public string Tooltip { get; set; } = string.Empty;

    public bool IsVisible { get; set; } = true;

    public Action? Click { get; set; }
}

public sealed class PhTableRow
{

    public IReadOnlyList<Control> Cells { get; set; } = [];

    public IReadOnlyList<PhTableAction> Actions { get; set; } = [];

    public string? Key { get; set; }
}
