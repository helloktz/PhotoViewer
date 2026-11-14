using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PhotoViewer.Common.Windows;

public partial class LayoutSettingsView : SettingsPageView
{

    private sealed record LayoutZone(Grid Grid, LayoutControl Owner, LayoutPosition Position);

    private static readonly string[] _availableTags =
    [
        nameof(AppStatusInfo.Name),
        nameof(AppStatusInfo.Path),
        nameof(AppStatusInfo.FileSize),
        nameof(AppStatusInfo.ModifiedDateTime),
        nameof(AppStatusInfo.Dimension),
        nameof(AppStatusInfo.FrameCount),
        nameof(AppStatusInfo.ListCount),
        nameof(AppStatusInfo.Zoom),
        nameof(AppStatusInfo.ColorSpace),
        nameof(AppStatusInfo.DPI),
        nameof(AppStatusInfo.HdrInfo),
        nameof(AppStatusInfo.ExifRating),
        nameof(AppStatusInfo.ExifDateTime),
        nameof(AppStatusInfo.ExifDateTimeOriginal),
        nameof(AppStatusInfo.DateTimeAuto),
        nameof(AppStatusInfo.AppName),
    ];

    private const string TAG_SEPARATOR = "; ";

    private PhButton _toolbarChip = null!;
    private PhButton _galleryChip = null!;
    private List<LayoutZone> _zones = [];
    private readonly Dictionary<Grid, TextBlock> _placeholders = [];

    private bool _suppressComboEvents;

    private PhButton _ghost = null!;
    private Panel? _ghostHost;

    private PhButton? _dragChip;
    private LayoutControl _dragControl;
    private Point _dragStart;
    private bool _isDragging;

    public LayoutSettingsView()
    {
        InitializeComponent();
    }

    public LayoutSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        BindToggle(PART_ShowAppIcon, ConfigId.ShowAppIcon,
            LangId.Settings_ShowAppIcon, LangId.Settings_Window, true);
        BindToggle(PART_EnableCenterWindowFit, ConfigId.EnableCenterWindowFit,
            LangId.Settings_EnableCenterWindowFit, LangId.Settings_Window, true);

        BuildImageInfoTags();
        BindEnumDropdown(PART_ImageInfoOverlayMode, ConfigId.ImageInfoOverlayMode, ImageInfoOverlayMode.BorderlessAutoHide,
            LangId.Settings_ImageInfoOverlay, LangId.Settings_ImageInfo);

        BuildPositionCombos();
        BuildArranger();

        DisableIfLocked(ConfigId.ImageInfoTags, PART_ImageInfoTags, PART_ResetImageInfoTags);
        DisableIfLocked(ConfigId.Layout, PART_ToolbarPosition, PART_GalleryPosition, PART_Preview);
    }

    private void BuildImageInfoTags()
    {
        var tags = VM.GetValue(ConfigId.ImageInfoTags,
            new ObservableCollection<string>(Config.DefaultImageInfoTags));
        PART_ImageInfoTags.Text = string.Join(TAG_SEPARATOR, tags);

        PART_ImageInfoTags.TextChanged += (_, _) => StageImageInfoTags(reformat: false);
        PART_ImageInfoTags.LostFocus += (_, _) => StageImageInfoTags(reformat: true);

        SetLocalizedText(PART_ResetImageInfoTags, LangId._ResetToDefault);
        PART_ResetImageInfoTags.Click += (_, _) =>
            PART_ImageInfoTags.Text = string.Join(TAG_SEPARATOR, Config.DefaultImageInfoTags);

        PART_AvailableTags.Text = string.Join(TAG_SEPARATOR, _availableTags);

        RegisterSearchKey(PART_ImageInfoTags, LangId.Settings_ImageInfoTags,
            ConfigId.ImageInfoTags, LangId.Settings_ImageInfo);
    }

    private void StageImageInfoTags(bool reformat)
    {
        var tokens = (PART_ImageInfoTags.Text ?? string.Empty)
            .Split([';', ',', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var tags = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            var canonical = Array.Find(_availableTags,
                t => t.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (canonical is not null && !tags.Contains(canonical)) tags.Add(canonical);
        }

        VM.SetValue(ConfigId.ImageInfoTags, new ObservableCollection<string>(tags));

        if (reformat)
        {
            var normalized = string.Join(TAG_SEPARATOR, tags);
            if (!string.Equals(PART_ImageInfoTags.Text, normalized, StringComparison.Ordinal))
                PART_ImageInfoTags.Text = normalized;
        }
    }

    private void BuildPositionCombos()
    {
        FillPositionCombo(PART_ToolbarPosition, [LayoutPosition.Top, LayoutPosition.Bottom]);
        FillPositionCombo(PART_GalleryPosition,
            [LayoutPosition.Top, LayoutPosition.Bottom, LayoutPosition.Left, LayoutPosition.Right]);

        PART_ToolbarPosition.SelectionChanged += (_, _) =>
        {
            if (_suppressComboEvents) return;
            if (PART_ToolbarPosition.SelectedItem is ComboBoxItem { Tag: LayoutPosition pos })
                ApplyPosition(LayoutControl.Toolbar, pos);
        };
        PART_GalleryPosition.SelectionChanged += (_, _) =>
        {
            if (_suppressComboEvents) return;
            if (PART_GalleryPosition.SelectedItem is ComboBoxItem { Tag: LayoutPosition pos })
                ApplyPosition(LayoutControl.Gallery, pos);
        };

        RegisterSearchKey(PART_ToolbarPosition, LangId.Settings_Layout_ToolbarPosition,
            ConfigId.Layout, LangId.Settings_Controls);
        RegisterSearchKey(PART_GalleryPosition, LangId.Settings_Layout_GalleryPosition,
            ConfigId.Layout, LangId.Settings_Controls);
    }

    private void FillPositionCombo(ComboBox combo, LayoutPosition[] positions)
    {
        foreach (var pos in positions)
        {
            var item = new ComboBoxItem { Tag = pos };
            var key = Lang.GetKey($"_Position_{pos}");
            AddLangRefresher(() => item.Content = key is { } k ? Core.Lang[k] : pos.ToString());
            combo.Items.Add(item);
        }
    }

    private static void SelectComboPosition(ComboBox combo, LayoutPosition pos)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem { Tag: LayoutPosition p } item && p == pos)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void BuildArranger()
    {
        _toolbarChip = CreateChip(LangId.Settings_Layout_Toolbar);
        _galleryChip = CreateChip(LangId.Settings_Layout_Gallery);

        _ghost = new PhButton
        {
            Variant = PhButtonVariant.Outline,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
            IsVisible = false,
        };

        _zones =
        [
            new(PART_ZoneToolbarTop, LayoutControl.Toolbar, LayoutPosition.Top),
            new(PART_ZoneToolbarBottom, LayoutControl.Toolbar, LayoutPosition.Bottom),
            new(PART_ZoneGalleryTop, LayoutControl.Gallery, LayoutPosition.Top),
            new(PART_ZoneGalleryBottom, LayoutControl.Gallery, LayoutPosition.Bottom),
            new(PART_ZoneGalleryLeft, LayoutControl.Gallery, LayoutPosition.Left),
            new(PART_ZoneGalleryRight, LayoutControl.Gallery, LayoutPosition.Right),
        ];

        foreach (var zone in _zones)
        {
            var ownerKey = zone.Owner == LayoutControl.Toolbar
                ? LangId.Settings_Layout_Toolbar
                : LangId.Settings_Layout_Gallery;

            var label = new TextBlock { Classes = { "slotLabel" } };
            AddLangRefresher(() => label.Text = Core.Lang[ownerKey]);

            _placeholders[zone.Grid] = label;
            zone.Grid.Children.Add(label);
        }

        _suppressComboEvents = true;
        SelectComboPosition(PART_ToolbarPosition, GetPosition(LayoutControl.Toolbar));
        SelectComboPosition(PART_GalleryPosition, GetPosition(LayoutControl.Gallery));
        _suppressComboEvents = false;

        RenderArranger();
    }

    private PhButton CreateChip(LangId labelKey)
    {
        var chip = new PhButton
        {
            Variant = PhButtonVariant.Outline,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AddLangRefresher(() => chip.Text = Core.Lang[labelKey]);

        chip.AddHandler(InputElement.PointerPressedEvent, Chip_PointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        chip.AddHandler(InputElement.PointerMovedEvent, Chip_PointerMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        chip.AddHandler(InputElement.PointerReleasedEvent, Chip_PointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);

        return chip;
    }

    private void RenderArranger()
    {
        PlaceChip(_toolbarChip, LayoutControl.Toolbar, GetPosition(LayoutControl.Toolbar));
        PlaceChip(_galleryChip, LayoutControl.Gallery, GetPosition(LayoutControl.Gallery));

        foreach (var zone in _zones)
        {
            var hasChip = zone.Grid.Children.Contains(_toolbarChip)
                || zone.Grid.Children.Contains(_galleryChip);
            _placeholders[zone.Grid].IsVisible = !hasChip;
        }
    }

    private void PlaceChip(PhButton chip, LayoutControl owner, LayoutPosition pos)
    {
        chip.VerticalAlignment = pos is LayoutPosition.Left or LayoutPosition.Right
            ? VerticalAlignment.Stretch
            : VerticalAlignment.Center;

        var target = _zones.First(z => z.Owner == owner && z.Position == pos).Grid;
        if (ReferenceEquals(chip.Parent, target)) return;

        (chip.Parent as Grid)?.Children.Remove(chip);
        target.Children.Add(chip);
    }

    private void ApplyPosition(LayoutControl control, LayoutPosition pos)
    {
        SetPosition(control, pos);

        _suppressComboEvents = true;
        SelectComboPosition(
            control == LayoutControl.Toolbar ? PART_ToolbarPosition : PART_GalleryPosition, pos);
        _suppressComboEvents = false;

        RenderArranger();
    }

    private LayoutPosition GetPosition(LayoutControl control)
    {
        var layout = VM.GetValue(ConfigId.Layout, new Dictionary<LayoutControl, LayoutPosition>());
        var defaultPos = control == LayoutControl.Toolbar ? LayoutPosition.Top : LayoutPosition.Bottom;
        var pos = layout.TryGetValue(control, out var p) ? p : defaultPos;

        if (control == LayoutControl.Toolbar && pos is LayoutPosition.Left or LayoutPosition.Right)
            pos = LayoutPosition.Top;

        return pos;
    }

    private void SetPosition(LayoutControl control, LayoutPosition pos)
    {
        var layout = new Dictionary<LayoutControl, LayoutPosition>(
            VM.GetValue(ConfigId.Layout, new Dictionary<LayoutControl, LayoutPosition>()));
        layout[control] = pos;
        VM.SetValue(ConfigId.Layout, layout);
    }

    private void Chip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not PhButton chip) return;
        if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;

        _dragChip = chip;
        _dragControl = ReferenceEquals(chip, _toolbarChip) ? LayoutControl.Toolbar : LayoutControl.Gallery;
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
            if (Math.Abs(delta.X) < 3 && Math.Abs(delta.Y) < 3) return;

            StartGhost();
        }

        var host = _ghostHost ?? (Panel)PART_DragLayer;
        var gpos = e.GetPosition(host);
        Canvas.SetLeft(_ghost, gpos.X - _ghost.Width / 2);
        Canvas.SetTop(_ghost, gpos.Y - _ghost.Height / 2);
        UpdateHoveredZone(e);
    }

    private void Chip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragChip is null || !ReferenceEquals(sender, _dragChip)) return;

        var control = _dragControl;
        var target = _isDragging ? FindZoneAt(e) : null;

        e.Pointer.Capture(null);
        EndGhost();

        _dragChip = null;
        _isDragging = false;

        if (target is not null && target.Owner == control && target.Position != GetPosition(control))
        {
            ApplyPosition(control, target.Position);
        }
        else
        {
            RenderArranger();
        }
    }

    private void StartGhost()
    {
        _isDragging = true;
        _dragChip!.Opacity = 0.35;

        _ghostHost = OverlayLayer.GetOverlayLayer(this) ?? (Panel)PART_DragLayer;
        (_ghost.Parent as Panel)?.Children.Remove(_ghost);
        _ghostHost.Children.Add(_ghost);

        _ghost.Text = _dragChip.Text;
        _ghost.Width = _dragChip.Bounds.Width;
        _ghost.Height = _dragChip.Bounds.Height;
        _ghost.IsVisible = true;

        HighlightValidZones(true);
    }

    private void EndGhost()
    {
        _ghost.IsVisible = false;

        var host = _ghostHost ?? (Panel)PART_DragLayer;
        host.Children.Remove(_ghost);
        _ghostHost = null;

        if (_dragChip is not null) _dragChip.Opacity = 1d;
        HighlightValidZones(false);
    }

    private IEnumerable<LayoutZone> ValidDropZones()
    {
        var current = GetPosition(_dragControl);
        return _zones.Where(z => z.Owner == _dragControl && z.Position != current);
    }

    private void HighlightValidZones(bool on)
    {
        foreach (var zone in ValidDropZones())
        {
            if (DashOf(zone) is not { } dash) continue;

            SetClass(dash, "valid", on);
            if (!on) SetClass(dash, "hover", false);
        }
    }

    private void UpdateHoveredZone(PointerEventArgs e)
    {
        var hovered = FindZoneAt(e);
        foreach (var zone in ValidDropZones())
        {
            if (DashOf(zone) is { } dash) SetClass(dash, "hover", ReferenceEquals(hovered, zone));
        }
    }

    private static Rectangle? DashOf(LayoutZone zone)
        => (zone.Grid.Parent as Border)?.Parent is Grid wrapper
            ? wrapper.Children.OfType<Rectangle>().FirstOrDefault()
            : null;

    private static void SetClass(StyledElement el, string className, bool on)
    {
        var has = el.Classes.Contains(className);
        if (on && !has) el.Classes.Add(className);
        else if (!on && has) el.Classes.Remove(className);
    }

    private LayoutZone? FindZoneAt(PointerEventArgs e)
    {
        foreach (var zone in ValidDropZones())
        {
            if (zone.Grid.Parent is not Border border) continue;

            var p = e.GetPosition(border);
            if (p.X >= 0 && p.Y >= 0 && p.X <= border.Bounds.Width && p.Y <= border.Bounds.Height)
            {
                return zone;
            }
        }

        return null;
    }

}
