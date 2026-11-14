using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;

namespace PhotoViewer.UI;

partial class PhTableControl
{

    private sealed class RowSlot(int index, string? key, Border highlight, Border flash,
        Border actions, List<PhToolButton> buttons)
    {
        public int Index { get; } = index;
        public string? Key { get; } = key;
        public Border Highlight { get; } = highlight;
        public Border Flash { get; } = flash;
        public Border Actions { get; } = actions;
        public List<PhToolButton> Buttons { get; } = buttons;

        public Control[] Layers { get; init; } = [];

        public Control?[] Cells { get; init; } = [];

        public double Height = double.NaN;

        public double Y;

        public bool IsRealized;
    }

    private sealed class ColumnLayout
    {
        private bool[] _isStar = [];
        private double[] _minWidths = [];
        private double[] _autoMax = [];

        public double[] Widths { get; private set; } = [];
        public double[] Offsets { get; private set; } = [];

        public bool Dirty { get; private set; }

        public int Count => Widths.Length;

        public bool IsStar(int col) => _isStar[col];

        public void Reset(IReadOnlyList<PhTableColumn> columns, int totalCols)
        {
            _isStar = new bool[totalCols];
            _minWidths = new double[totalCols];
            _autoMax = new double[totalCols];
            Widths = new double[totalCols];
            Offsets = new double[totalCols];

            for (var c = 0; c < columns.Count && c < totalCols; c++)
            {
                _isStar[c] = columns[c].Star;
                _minWidths[c] = columns[c].MinWidth;
            }

            Dirty = false;
        }

        public void Observe(int col, double width)
        {
            if (_isStar[col] || width <= _autoMax[col]) return;

            _autoMax[col] = width;
            Dirty = true;
        }

        public void Resolve(double availableWidth)
        {
            var used = 0d;
            var stars = 0;

            for (var c = 0; c < Count; c++)
            {
                if (_isStar[c])
                {
                    stars++;
                    continue;
                }

                Widths[c] = Math.Max(_autoMax[c], _minWidths[c]);
                used += Widths[c];
            }

            if (stars > 0)
            {
                var share = (availableWidth - used) / stars;
                for (var c = 0; c < Count; c++)
                {
                    if (_isStar[c]) Widths[c] = Math.Max(_minWidths[c], share);
                }
            }

            var x = 0d;
            for (var c = 0; c < Count; c++)
            {
                Offsets[c] = x;
                x += Widths[c];
            }

            Dirty = false;
        }
    }

    private sealed class RowsPanel(PhTableControl owner) : Panel
    {
        private const double REALIZE_BUFFER = 120;
        private const double FALLBACK_VIEWPORT = 600;
        private const double FALLBACK_WIDTH = 600;
        private const double DEFAULT_ROW_HEIGHT = 34;
        private const int MAX_WIDTH_PASSES = 3;

        private readonly ColumnLayout _layout = new();

        private int _firstRealized = -1;
        private int _lastRealized = -1;

        private double _measuredSum;
        private int _measuredCount;
        private double _bodyHeight;

        private Rect _viewport;

        public double HeaderHeight { get; private set; }

        public double EstimatedRowHeight
            => _measuredCount > 0 ? _measuredSum / _measuredCount : DEFAULT_ROW_HEIGHT;

        public double RowHeightOf(RowSlot slot)
            => double.IsNaN(slot.Height) ? EstimatedRowHeight : slot.Height;

        public void Reset(IReadOnlyList<PhTableColumn> columns, int totalCols)
        {
            Children.Clear();
            foreach (var slot in owner._rows) slot.IsRealized = false;

            _firstRealized = _lastRealized = -1;
            _measuredSum = 0;
            _measuredCount = 0;
            _bodyHeight = 0;
            HeaderHeight = 0;

            _viewport = default;
            _layout.Reset(columns, totalCols);

            if (owner._rows.Count > 0)
            {
                foreach (var spanner in owner._headerSpanners) Children.Add(spanner);
                foreach (var cell in owner._headerCells)
                {
                    if (cell is not null) Children.Add(cell);
                }

                ResolveRowOffsets();
                EnsureRealized();
            }

            InvalidateMeasure();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            EffectiveViewportChanged += Panel_EffectiveViewportChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            EffectiveViewportChanged -= Panel_EffectiveViewportChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void Panel_EffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
        {
            _viewport = e.EffectiveViewport;
            EnsureRealized();
        }

        public void EnsureRealized()
        {
            if (owner._rows.Count == 0) return;

            var (first, last) = VisibleRange();
            if (first == _firstRealized && last == _lastRealized) return;

            if (_firstRealized >= 0)
            {
                for (var i = _firstRealized; i <= _lastRealized; i++)
                {
                    if (i < first || i > last) Unrealize(owner._rows[i]);
                }
            }

            _firstRealized = first;
            _lastRealized = last;

            for (var i = first; i <= last; i++) Realize(owner._rows[i], i);

            InvalidateMeasure();
        }

        public int RowAt(double y)
        {
            var rows = owner._rows;
            var body = y - HeaderHeight;
            if (body < 0) return -1;

            for (var i = 0; i < rows.Count; i++)
            {
                var slot = rows[i];
                if (body < slot.Y) return -1;
                if (body < slot.Y + RowHeightOf(slot)) return i;
            }

            return -1;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (owner._rows.Count == 0) return default;

            var width = ResolveWidth(availableSize);

            MeasureHeader();

            var pass = 0;
            for (; pass < MAX_WIDTH_PASSES; pass++)
            {
                _layout.Resolve(width);
                MeasureRows(width);
                if (!_layout.Dirty) break;
            }

            var headerConstraint = new Size(width, HeaderHeight);
            foreach (var spanner in owner._headerSpanners) spanner.Measure(headerConstraint);

            ResolveRowOffsets();

            return new Size(width, HeaderHeight + _bodyHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (owner._rows.Count == 0) return finalSize;

            var offsetY = owner._scroll.Offset.Y;

            foreach (var spanner in owner._headerSpanners)
            {
                spanner.Arrange(new Rect(0, offsetY, finalSize.Width, HeaderHeight));
            }
            for (var c = 0; c < owner._headerCells.Length; c++)
            {
                owner._headerCells[c]?.Arrange(
                    new Rect(_layout.Offsets[c], offsetY, _layout.Widths[c], HeaderHeight));
            }

            if (_firstRealized >= 0)
            {
                for (var i = _firstRealized; i <= _lastRealized; i++)
                {
                    ArrangeRow(owner._rows[i], finalSize.Width);
                }
            }

            return new Size(finalSize.Width, Math.Max(finalSize.Height, HeaderHeight + _bodyHeight));
        }

        private void MeasureHeader()
        {
            var constraint = new Size(double.PositiveInfinity, double.PositiveInfinity);
            var height = 0d;

            for (var c = 0; c < owner._headerCells.Length; c++)
            {
                var cell = owner._headerCells[c];
                if (cell is null) continue;

                cell.Measure(constraint);
                _layout.Observe(c, cell.DesiredSize.Width);
                height = Math.Max(height, cell.DesiredSize.Height);
            }

            HeaderHeight = height;
        }

        private void MeasureRows(double width)
        {
            if (_firstRealized < 0) return;

            var layerConstraint = new Size(width, double.PositiveInfinity);

            for (var i = _firstRealized; i <= _lastRealized; i++)
            {
                var slot = owner._rows[i];
                var height = 0d;

                for (var c = 0; c < slot.Cells.Length; c++)
                {
                    var cell = slot.Cells[c];
                    if (cell is null) continue;

                    var available = _layout.IsStar(c) ? _layout.Widths[c] : double.PositiveInfinity;
                    cell.Measure(new Size(available, double.PositiveInfinity));

                    _layout.Observe(c, cell.DesiredSize.Width);
                    height = Math.Max(height, cell.DesiredSize.Height);
                }

                foreach (var layer in slot.Layers) layer.Measure(layerConstraint);

                SetRowHeight(slot, height);
            }
        }

        private void ArrangeRow(RowSlot slot, double width)
        {
            var top = HeaderHeight + slot.Y;
            var height = RowHeightOf(slot);

            var full = new Rect(0, top, width, height);
            foreach (var layer in slot.Layers) layer.Arrange(full);

            for (var c = 0; c < slot.Cells.Length; c++)
            {
                slot.Cells[c]?.Arrange(new Rect(_layout.Offsets[c], top, _layout.Widths[c], height));
            }
        }

        private void SetRowHeight(RowSlot slot, double height)
        {
            if (!double.IsNaN(slot.Height))
            {
                if (Math.Abs(slot.Height - height) < 0.5) return;

                _measuredSum -= slot.Height;
                _measuredCount--;
            }

            slot.Height = height;
            _measuredSum += height;
            _measuredCount++;
        }

        private void ResolveRowOffsets()
        {
            var rows = owner._rows;
            var estimate = EstimatedRowHeight;
            var y = 0d;

            for (var i = 0; i < rows.Count; i++)
            {
                var slot = rows[i];
                slot.Y = y;
                y += double.IsNaN(slot.Height) ? estimate : slot.Height;
            }

            _bodyHeight = y;
        }

        private double ResolveWidth(Size available)
        {
            if (!double.IsInfinity(available.Width) && available.Width > 0) return available.Width;

            var viewport = owner._scroll.Viewport.Width;
            return viewport > 0 ? viewport : FALLBACK_WIDTH;
        }

        private (int First, int Last) VisibleRange()
        {
            var rows = owner._rows;

            var height = _viewport.Height > 0 ? _viewport.Height : FALLBACK_VIEWPORT;
            var viewTop = _viewport.Height > 0 ? _viewport.Y : owner._scroll.Offset.Y;

            var top = viewTop - HeaderHeight - REALIZE_BUFFER;
            var bottom = viewTop - HeaderHeight + height + REALIZE_BUFFER;

            var first = -1;
            var last = -1;

            for (var i = 0; i < rows.Count; i++)
            {
                var slot = rows[i];
                if (slot.Y >= bottom) break;
                if (slot.Y + RowHeightOf(slot) <= top) continue;

                if (first < 0) first = i;
                last = i;
            }

            return first < 0 ? (0, 0) : (first, last);
        }

        private void Realize(RowSlot slot, int index)
        {
            if (slot.IsRealized) return;
            slot.IsRealized = true;

            var at = ChildIndexFor(index);
            foreach (var layer in slot.Layers) Children.Insert(at++, layer);
            foreach (var cell in slot.Cells)
            {
                if (cell is not null) Children.Insert(at++, cell);
            }

            owner.RefreshRowReveal(slot);
        }

        private int ChildIndexFor(int index)
        {
            var at = owner.HeaderVisualCount;

            for (var i = _firstRealized; i < index; i++)
            {
                var slot = owner._rows[i];
                if (!slot.IsRealized) continue;

                at += slot.Layers.Length;
                foreach (var cell in slot.Cells)
                {
                    if (cell is not null) at++;
                }
            }

            return at;
        }

        private void Unrealize(RowSlot slot)
        {
            if (!slot.IsRealized) return;
            slot.IsRealized = false;

            foreach (var layer in slot.Layers) Children.Remove(layer);
            foreach (var cell in slot.Cells)
            {
                if (cell is not null) Children.Remove(cell);
            }
        }

    }

}
