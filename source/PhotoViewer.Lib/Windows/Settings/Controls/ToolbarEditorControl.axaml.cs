using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using PhotoViewer.Common.Photoing;
using Avalonia.Threading;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class ToolbarEditorControl : PhControl
{

    private enum EditorGroup { Primary, Secondary, Available }

    private const double ICON_SIZE = 24;
    private const double CHIP_PADDING = 6;
    private const double DRAG_THRESHOLD = 3;

    private readonly List<ToolbarItemModel> _primary = [];
    private readonly List<ToolbarItemModel> _secondary = [];
    private readonly List<ToolbarItemModel> _available = [];

    private readonly Dictionary<string, SvgSource?> _svgCache = new(StringComparer.OrdinalIgnoreCase);

    private List<ToolbarItemModel>? _catalog;
    private List<ToolbarItemModel> Catalog => _catalog ??= [.. Config.BuiltInToolbarItems];

    private Control? _dragChip;
    private ToolbarItemModel? _dragModel;
    private EditorGroup _dragSource;
    private Point _dragStart;
    private bool _isDragging;
    private bool _suppressClick;

    private Border? _ghost;
    private Border? _ghostTag;
    private Border? _marker;
    private Panel? _ghostHost;

    private bool _dragCanDelete;

    private ToolbarItemModel? _justMoved;

    private ToolbarItemModel? _focusAfterRender;

    public event EventHandler? ButtonsChanged;

    public ToolbarEditorControl()
    {
        InitializeComponent();

        PART_AddCustomBtn.Click += (_, _) => OnAddCustomClicked();
        PART_ResetBtn.Click += (_, _) => ResetToDefault();

        Focusable = true;
        KeyboardNavigation.SetTabNavigation(PART_PrimaryGroup, KeyboardNavigationMode.Once);
        KeyboardNavigation.SetTabNavigation(PART_SecondaryGroup, KeyboardNavigationMode.Once);
        KeyboardNavigation.SetTabNavigation(PART_AvailableGroup, KeyboardNavigationMode.Once);

        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void LoadButtons(IEnumerable<ToolbarItemModel> current)
    {
        _primary.Clear();
        _secondary.Clear();

        foreach (var item in current)
        {
            var clone = Clone(item);
            if (clone.Alignment == ToolbarItemAlignment.Right) _secondary.Add(clone);
            else _primary.Add(clone);
        }

        RecomputeAvailable();
        RenderAll();
    }

    public ObservableCollection<ToolbarItemModel> CurrentButtons
    {
        get
        {
            var list = new ObservableCollection<ToolbarItemModel>();
            foreach (var m in _primary) { m.Alignment = ToolbarItemAlignment.Left; list.Add(m); }
            foreach (var m in _secondary) { m.Alignment = ToolbarItemAlignment.Right; list.Add(m); }
            return list;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        _svgCache.Clear();
        base.OnLoaded(e);
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        PART_AddCustomBtn.Text = Core.Lang[LangId.Settings_Toolbar_AddCustomButton];
        PART_ResetBtn.Text = Core.Lang[LangId._ResetToDefault];

        RecomputeAvailable();
        RenderAll();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);

        _svgCache.Clear();
        RenderAll();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);

        if (ReferenceEquals(e.Source, this))
        {
            Dispatcher.UIThread.Post(() => { if (IsFocused) FocusFirstButton(); }, DispatcherPriority.Input);
        }
    }

    private List<ToolbarItemModel> ListFor(EditorGroup g) => g switch
    {
        EditorGroup.Primary => _primary,
        EditorGroup.Secondary => _secondary,
        _ => _available,
    };

    private WrapPanel PanelFor(EditorGroup g) => g switch
    {
        EditorGroup.Primary => PART_PrimaryGroup,
        EditorGroup.Secondary => PART_SecondaryGroup,
        _ => PART_AvailableGroup,
    };

    private Border ZoneFor(EditorGroup g) => g switch
    {
        EditorGroup.Primary => PART_PrimaryZone,
        EditorGroup.Secondary => PART_SecondaryZone,
        _ => PART_AvailableZone,
    };

    private Rectangle DashFor(EditorGroup g) => g switch
    {
        EditorGroup.Primary => PART_PrimaryDash,
        EditorGroup.Secondary => PART_SecondaryDash,
        _ => PART_AvailableDash,
    };

    private static ToolbarItemModel Clone(ToolbarItemModel m) => new()
    {
        Id = m.Id,
        Image = m.Image,
        Text = m.Text,
        ShowText = m.ShowText,
        ConfigBinding = m.ConfigBinding,
        ConfigBindingValue = m.ConfigBindingValue,
        Alignment = m.Alignment,
        OnClick = m.OnClick,
    };

    private void RecomputeAvailable()
    {
        _available.Clear();
        _available.Add(ToolbarItemModel.Separator);

        var currentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _primary) if (!m.IsSeparator) currentIds.Add(m.Id);
        foreach (var m in _secondary) if (!m.IsSeparator) currentIds.Add(m.Id);

        var others = Catalog
            .Where(b => !currentIds.Contains(b.Id))
            .OrderBy(b => b.DisplayText, StringComparer.CurrentCultureIgnoreCase);
        _available.AddRange(others);
    }

    private SvgSource? GetSvg(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_svgCache.TryGetValue(path, out var cached)) return cached;

        SvgSource? src = null;
        try { src = SvgSource.Load(path, null, SvgCodec.LocalResources); }
        catch { }

        _svgCache[path] = src;
        return src;
    }

    private void RenderAll()
    {
        RenderGroup(EditorGroup.Primary);
        RenderGroup(EditorGroup.Secondary);
        RenderGroup(EditorGroup.Available);
        _justMoved = null;

        var focusModel = _focusAfterRender;
        _focusAfterRender = null;

        if (focusModel is not null)
        {
            Dispatcher.UIThread.Post(() => FocusChipFor(focusModel), DispatcherPriority.Input);
        }
    }

    private void RenderGroup(EditorGroup g)
    {
        var panel = PanelFor(g);
        panel.Children.Clear();

        foreach (var model in ListFor(g))
        {
            panel.Children.Add(BuildChip(model));
        }
    }

    private PhToolButton BuildChip(ToolbarItemModel model)
    {
        var name = model.IsSeparator ? Core.Lang[LangId._Separator] : model.DisplayText;

        var chip = new PhToolButton
        {
            Tag = model,
            Padding = new Thickness(CHIP_PADDING),
            Margin = new Thickness(3),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true,
            Content = BuildIconVisual(model),
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }],
        };

        if (!string.IsNullOrWhiteSpace(name)) ToolTip.SetTip(chip, name);

        AutomationProperties.SetName(chip, name);

        chip.AddHandler(PointerPressedEvent, Chip_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        chip.AddHandler(PointerMovedEvent, Chip_PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        chip.AddHandler(PointerReleasedEvent, Chip_PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        chip.AddHandler(PointerCaptureLostEvent, Chip_PointerCaptureLost, handledEventsToo: true);

        chip.Click += Chip_Click;

        if (ReferenceEquals(model, _justMoved))
        {
            chip.Opacity = 0.25;
            Dispatcher.UIThread.Post(() => chip.Opacity = 1, DispatcherPriority.Background);
        }

        return chip;
    }

    private Control BuildIconVisual(ToolbarItemModel model)
    {
        if (model.IsSeparator)
        {
            var lineBrush = Resx.Get<IBrush>(ResxId.TextControlForeground);

            return new Border
            {
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                Child = new Border
                {
                    Width = 2,
                    Height = ICON_SIZE * 0.7,
                    CornerRadius = new CornerRadius(1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.5,
                    Background = lineBrush,
                },
            };
        }

        var src = GetSvg(model.ImagePath);
        if (src is null)
        {
            if (!model.IsPlaceholderIconVisible) return new Border { Width = ICON_SIZE, Height = ICON_SIZE };

            return new PathIcon
            {
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                Opacity = 0.6,
                Data = Resx.GetIcon(ResxIconId.IconPlaceholder),
            };
        }

        return new Image
        {
            Width = ICON_SIZE,
            Height = ICON_SIZE,
            Source = new SvgImage { Source = src },
        };
    }

    private void ResetToDefault()
    {
        LoadButtons(Config.DefaultToolbarItems);
        ButtonsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddCustomClicked() => _ = OpenAddDialogAsync();

    private async Task OpenAddDialogAsync()
    {
        var win = new ToolbarButtonEditWindow(null, CollectTakenIds(except: null), isBuiltIn: false);
        if (await win.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (win.ResultModel is not { } model) return;

        if (model.Alignment == ToolbarItemAlignment.Right) _secondary.Add(model);
        else _primary.Add(model);

        _justMoved = model;
        _focusAfterRender = model;
        Commit();
    }

    private async Task OpenEditDialogAsync(ToolbarItemModel model)
    {
        if (model.IsSeparator) return;

        var win = new ToolbarButtonEditWindow(model, CollectTakenIds(except: model), IsBuiltIn(model));
        if (await win.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (win.ResultModel is not { } edited) return;

        ApplyEdit(model, edited);
        _justMoved = model;
        _focusAfterRender = model;
        Commit();
    }

    private void ApplyEdit(ToolbarItemModel model, ToolbarItemModel edited)
    {
        model.Id = edited.Id;
        model.Image = edited.Image;
        model.Text = edited.Text;
        model.ShowText = edited.ShowText;
        model.ConfigBinding = edited.ConfigBinding;
        model.ConfigBindingValue = edited.ConfigBindingValue;
        model.OnClick = edited.OnClick;

        var group = GroupOf(model);
        if (group == EditorGroup.Primary && edited.Alignment == ToolbarItemAlignment.Right)
        {
            _primary.Remove(model);
            model.Alignment = ToolbarItemAlignment.Right;
            _secondary.Add(model);
        }
        else if (group == EditorGroup.Secondary && edited.Alignment == ToolbarItemAlignment.Left)
        {
            _secondary.Remove(model);
            model.Alignment = ToolbarItemAlignment.Left;
            _primary.Add(model);
        }
        else
        {
            model.Alignment = edited.Alignment;
        }
    }

    private HashSet<string> CollectTakenIds(ToolbarItemModel? except)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in Catalog) if (!c.IsSeparator) set.Add(c.Id);
        foreach (var m in _primary) if (!m.IsSeparator) set.Add(m.Id);
        foreach (var m in _secondary) if (!m.IsSeparator) set.Add(m.Id);
        if (except is not null && !except.IsSeparator) set.Remove(except.Id);
        return set;
    }

    private bool IsBuiltIn(ToolbarItemModel model) => !model.IsSeparator
        && Catalog.Any(c => !c.IsSeparator && c.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));

    private void Commit()
    {
        RecomputeAvailable();
        RenderAll();
        ButtonsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Chip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control chip || chip.Tag is not ToolbarItemModel model) return;

        if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;

        _suppressClick = false;
        _dragChip = chip;
        _dragModel = model;
        _dragSource = GroupOfChip(chip);
        _dragStart = e.GetPosition(PART_DragLayer);
        _isDragging = false;

        e.Pointer.Capture(chip);
    }

    private void Chip_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragChip is null || !ReferenceEquals(sender, _dragChip)) return;
        if (!ReferenceEquals(e.Pointer.Captured, _dragChip)) return;

        var pos = e.GetPosition(PART_DragLayer);

        if (!_isDragging)
        {
            var delta = pos - _dragStart;
            if (Math.Abs(delta.X) < DRAG_THRESHOLD && Math.Abs(delta.Y) < DRAG_THRESHOLD) return;
            StartDrag(e);
        }

        var host = _ghostHost ?? (Panel)PART_DragLayer;
        var gpos = e.GetPosition(host);
        Canvas.SetLeft(_ghost!, gpos.X - _ghost!.Width / 2);
        Canvas.SetTop(_ghost, gpos.Y - _ghost.Height / 2);

        if (_ghostTag is not null)
        {
            var tagH = _ghostTag.Bounds.Height > 0 ? _ghostTag.Bounds.Height : 24;
            Canvas.SetLeft(_ghostTag, gpos.X - _ghost.Width / 2);
            Canvas.SetTop(_ghostTag, gpos.Y - _ghost.Height / 2 - tagH - 4);
        }

        UpdateDropTarget(e);
    }

    private void Chip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragChip is null || !ReferenceEquals(sender, _dragChip)) return;

        if (!_isDragging)
        {
            _dragChip = null;
            _dragModel = null;
            return;
        }

        var srcModel = _dragModel!;
        var srcGroup = _dragSource;
        var target = HitZone(e);
        var dropIndex = target is EditorGroup tg && tg != EditorGroup.Available
            ? ComputeInsertIndex(PanelFor(tg), e.GetPosition(PanelFor(tg)))
            : 0;

        e.Pointer.Capture(null);
        EndDrag();

        _dragChip = null;
        _dragModel = null;
        _isDragging = false;

        if (target is EditorGroup dst)
        {
            PerformDrop(srcModel, srcGroup, dst, dropIndex);
        }
        else
        {
            RenderAll();
        }
    }

    private void Chip_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ReferenceEquals(sender, _dragChip)) return;

        var wasDragging = _isDragging;
        EndDrag();
        _dragChip = null;
        _dragModel = null;
        _isDragging = false;

        if (wasDragging) RenderAll();
    }

    private void Chip_Click(object? sender, RoutedEventArgs e)
    {
        if (_suppressClick) { _suppressClick = false; return; }
        if (sender is not Control chip || chip.Tag is not ToolbarItemModel model) return;

        Dispatcher.UIThread.Post(() => _ = OpenEditDialogAsync(model));
    }

    private void PerformDrop(ToolbarItemModel src, EditorGroup srcGroup, EditorGroup dst, int index)
    {
        if (dst == EditorGroup.Available)
        {
            if (srcGroup == EditorGroup.Available) { RenderAll(); return; }
            ListFor(srcGroup).Remove(src);
        }
        else
        {
            var dstList = ListFor(dst);
            var dstAlignment = dst == EditorGroup.Secondary
                ? ToolbarItemAlignment.Right
                : ToolbarItemAlignment.Left;

            if (srcGroup == EditorGroup.Available)
            {
                var clone = src.IsSeparator ? ToolbarItemModel.Separator : Clone(src);
                index = Math.Clamp(index, 0, dstList.Count);
                dstList.Insert(index, clone);
                clone.Alignment = dstAlignment;
                _justMoved = clone;
            }
            else
            {
                var srcList = ListFor(srcGroup);
                var oldIndex = srcList.IndexOf(src);
                if (oldIndex < 0) { RenderAll(); return; }

                srcList.RemoveAt(oldIndex);
                if (srcGroup == dst && oldIndex < index) index--;
                index = Math.Clamp(index, 0, dstList.Count);
                dstList.Insert(index, src);
                src.Alignment = dstAlignment;
                _justMoved = src;
            }
        }

        Commit();
    }

    private void StartDrag(PointerEventArgs e)
    {
        _isDragging = true;
        _dragChip!.Opacity = 0.35;
        _suppressClick = true;
        _ghostHost = OverlayLayer.GetOverlayLayer(this) ?? (Panel)PART_DragLayer;

        var accent = Resx.Get<Color>(ResxId.SystemAccentColor).ToBrush();
        var ghostBg = Resx.Get<IBrush>(ResxId.IG_BackgroundNeutralBrush);

        _ghost = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = accent,
            Background = ghostBg,
            BoxShadow = BoxShadows.Parse("0 4 12 0 #40000000"),
            Width = _dragChip.Bounds.Width,
            Height = _dragChip.Bounds.Height,
            IsHitTestVisible = false,
            Child = BuildIconVisual(_dragModel!),
        };
        _ghostHost.Children.Add(_ghost);

        _dragCanDelete = _dragSource != EditorGroup.Available
            && !_dragModel!.IsSeparator
            && !IsBuiltIn(_dragModel);
        if (_dragCanDelete)
        {
            var dangerBg = Resx.Get<IBrush>(ResxId.IG_TextDangerBrush);
            var dangerFg = Core.Theme.BaseColor.ToBrush();

            _ghostTag = new Border
            {
                IsVisible = false,
                IsHitTestVisible = false,
                Background = dangerBg,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3),
                BoxShadow = BoxShadows.Parse("0 2 6 0 #40000000"),
                Child = new TextBlock
                {
                    Text = Core.Lang[LangId._Delete],
                    Foreground = dangerFg,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                },
            };
            _ghostHost.Children.Add(_ghostTag);
        }

        var hovered = HitZone(e);
        foreach (var g in ValidTargets())
        {
            var show = g != _dragSource;
            SetZoneState(g, valid: show, hover: show && g == hovered);
        }
    }

    private void EndDrag()
    {
        var host = _ghostHost ?? (Panel)PART_DragLayer;

        if (_ghost is not null)
        {
            host.Children.Remove(_ghost);
            _ghost = null;
        }
        if (_ghostTag is not null)
        {
            host.Children.Remove(_ghostTag);
            _ghostTag = null;
        }
        _ghostHost = null;
        _dragCanDelete = false;

        HideMarker();
        ResetZoneStates();

        if (_dragChip is not null) _dragChip.Opacity = 1;
    }

    private IEnumerable<EditorGroup> ValidTargets()
    {
        yield return EditorGroup.Primary;
        yield return EditorGroup.Secondary;
        if (_dragSource != EditorGroup.Available) yield return EditorGroup.Available;
    }

    private void UpdateDropTarget(PointerEventArgs e)
    {
        HideMarker();

        var hovered = HitZone(e);
        foreach (var g in ValidTargets())
        {
            var show = g != _dragSource;
            var isHover = show && g == hovered;

            var isDanger = isHover && g == EditorGroup.Available && _dragCanDelete;
            SetZoneState(g, valid: show, hover: isHover && !isDanger, danger: isDanger);
        }

        if (_ghostTag is not null) _ghostTag.IsVisible = hovered == EditorGroup.Available;

        if (hovered is EditorGroup hg && hg != EditorGroup.Available)
        {
            var panel = PanelFor(hg);
            ShowMarker(panel, ComputeInsertIndex(panel, e.GetPosition(panel)));
        }
    }

    private EditorGroup? HitZone(PointerEventArgs e)
    {
        foreach (var g in ValidTargets())
        {
            var zone = ZoneFor(g);
            var p = e.GetPosition(zone);
            if (p.X >= 0 && p.Y >= 0 && p.X <= zone.Bounds.Width && p.Y <= zone.Bounds.Height)
            {
                return g;
            }
        }

        return null;
    }

    private EditorGroup GroupOfChip(Control chip)
    {
        if (PART_PrimaryGroup.Children.Contains(chip)) return EditorGroup.Primary;
        if (PART_SecondaryGroup.Children.Contains(chip)) return EditorGroup.Secondary;
        return EditorGroup.Available;
    }

    private void SetZoneState(EditorGroup g, bool valid, bool hover, bool danger = false)
    {
        SetClass(ZoneFor(g), "valid", valid);
        SetClass(ZoneFor(g), "hover", hover);
        SetClass(DashFor(g), "valid", valid);
        SetClass(DashFor(g), "hover", hover);
        SetClass(DashFor(g), "danger", danger);
    }

    private void ResetZoneStates()
    {
        SetZoneState(EditorGroup.Primary, false, false);
        SetZoneState(EditorGroup.Secondary, false, false);
        SetZoneState(EditorGroup.Available, false, false);
    }

    private static int ComputeInsertIndex(WrapPanel panel, Point p)
    {
        var chips = panel.Children.Where(c => c.Tag is ToolbarItemModel).ToList();
        var n = chips.Count;
        if (n == 0) return 0;

        for (var i = 0; i < n; i++)
        {
            var b = chips[i].Bounds;

            if (p.Y > b.Bottom) continue;

            if (p.Y < b.Top) return i;

            if (p.X < b.X + b.Width / 2) return i;

            var lastInRow = i == n - 1 || chips[i + 1].Bounds.Top > b.Bottom - 0.5;
            if (lastInRow) return i + 1;
        }

        return n;
    }

    private void ShowMarker(WrapPanel panel, int index)
    {
        EnsureMarker();

        var chips = panel.Children.Where(c => c.Tag is ToolbarItemModel).ToList();
        double x, y, height;

        if (chips.Count == 0)
        {
            var topLeft = panel.TranslatePoint(default, PART_DragLayer) ?? default;
            x = topLeft.X;
            y = topLeft.Y;
            height = ICON_SIZE + CHIP_PADDING * 2;
        }
        else if (index >= chips.Count)
        {
            var last = chips[^1];
            var corner = last.TranslatePoint(new Point(last.Bounds.Width, 0), PART_DragLayer) ?? default;
            x = corner.X + 1;
            y = corner.Y;
            height = last.Bounds.Height;
        }
        else
        {
            var chip = chips[index];
            var corner = chip.TranslatePoint(default, PART_DragLayer) ?? default;
            x = corner.X - 4;
            y = corner.Y;
            height = chip.Bounds.Height;
        }

        _marker!.Height = height;
        Canvas.SetLeft(_marker, x);
        Canvas.SetTop(_marker, y);
        _marker.IsVisible = true;
    }

    private void EnsureMarker()
    {
        if (_marker is not null) return;

        _marker = new Border { Classes = { "dropMarker" }, IsVisible = false };
        PART_DragLayer.Children.Add(_marker);
    }

    private void HideMarker()
    {
        if (_marker is not null) _marker.IsVisible = false;
    }

    private static void SetClass(StyledElement el, string className, bool on)
    {
        var has = el.Classes.Contains(className);
        if (on && !has) el.Classes.Add(className);
        else if (!on && has) el.Classes.Remove(className);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Control focused
            || focused.Tag is not ToolbarItemModel model)
            return;

        _suppressClick = false;

        var group = GroupOfChip(focused);

        switch (e.Key)
        {
            case Key.Left:
                e.Handled = MoveFocusHorizontal(focused, group, -1);
                break;
            case Key.Right:
                e.Handled = MoveFocusHorizontal(focused, group, +1);
                break;
            case Key.Up:
                e.Handled = MoveFocusVertical(focused, group, up: true);
                break;
            case Key.Down:
                e.Handled = MoveFocusVertical(focused, group, up: false);
                break;

            case Key.Delete:
            case Key.Back:
                if (group != EditorGroup.Available) e.Handled = RemoveByKeyboard(model, group);
                break;
        }
    }

    private bool MoveFocusHorizontal(Control chip, EditorGroup group, int delta)
    {
        var chips = ChipsOf(PanelFor(group));
        var i = chips.IndexOf(chip);
        if (i < 0) return false;

        var target = i + delta;
        if (target >= 0 && target < chips.Count)
        {
            chips[target].Focus(NavigationMethod.Tab);
            return true;
        }

        Control? cross = null;
        if (group == EditorGroup.Primary && delta > 0) cross = FirstChip(PART_SecondaryGroup);
        else if (group == EditorGroup.Secondary && delta < 0) cross = LastChip(PART_PrimaryGroup);

        if (cross is null) return false;
        cross.Focus(NavigationMethod.Tab);
        return true;
    }

    private bool MoveFocusVertical(Control chip, EditorGroup group, bool up)
    {
        var chips = ChipsOf(PanelFor(group));
        if (chips.Count == 0) return false;

        var cur = chip.Bounds;
        var cx = cur.X + cur.Width / 2;

        double? rowTop = null;
        foreach (var c in chips)
        {
            if (ReferenceEquals(c, chip)) continue;
            var top = c.Bounds.Top;
            if (up)
            {
                if (top >= cur.Top - 0.5) continue;
                if (rowTop is null || top > rowTop) rowTop = top;
            }
            else
            {
                if (top <= cur.Top + 0.5) continue;
                if (rowTop is null || top < rowTop) rowTop = top;
            }
        }
        if (rowTop is null) return false;

        Control? best = null;
        var bestDx = double.PositiveInfinity;
        foreach (var c in chips)
        {
            if (ReferenceEquals(c, chip)) continue;
            var b = c.Bounds;
            if (Math.Abs(b.Top - rowTop.Value) > 1) continue;
            var dx = Math.Abs((b.X + b.Width / 2) - cx);
            if (dx < bestDx) { bestDx = dx; best = c; }
        }

        best?.Focus(NavigationMethod.Tab);
        return best is not null;
    }

    private EditorGroup? GroupOf(ToolbarItemModel model)
    {
        if (_primary.Contains(model)) return EditorGroup.Primary;
        if (_secondary.Contains(model)) return EditorGroup.Secondary;
        if (_available.Contains(model)) return EditorGroup.Available;
        return null;
    }

    private bool RemoveByKeyboard(ToolbarItemModel model, EditorGroup group)
    {
        var list = ListFor(group);
        var i = list.IndexOf(model);
        if (i < 0) return false;

        list.Remove(model);
        _focusAfterRender = list.Count > 0 ? list[Math.Clamp(i, 0, list.Count - 1)] : null;
        var emptied = _focusAfterRender is null;
        Commit();

        if (emptied) Dispatcher.UIThread.Post(FocusFirstButton, DispatcherPriority.Input);
        return true;
    }

    public void FocusFirstButton()
        => (FirstChip(PART_PrimaryGroup) ?? FirstChip(PART_SecondaryGroup) ?? FirstChip(PART_AvailableGroup))
            ?.Focus(NavigationMethod.Tab);

    private void FocusChipFor(ToolbarItemModel model)
    {
        foreach (var panel in new Panel[] { PART_PrimaryGroup, PART_SecondaryGroup, PART_AvailableGroup })
        {
            foreach (var c in panel.Children)
            {
                if (ReferenceEquals(c.Tag, model)) { c.Focus(NavigationMethod.Tab); return; }
            }
        }
    }

    private static List<Control> ChipsOf(Panel panel)
        => panel.Children.Where(c => c.Tag is ToolbarItemModel).ToList();

    private static Control? FirstChip(Panel panel)
        => panel.Children.FirstOrDefault(c => c.Tag is ToolbarItemModel);

    private static Control? LastChip(Panel panel)
        => panel.Children.LastOrDefault(c => c.Tag is ToolbarItemModel);

}
