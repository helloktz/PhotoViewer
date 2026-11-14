using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Svg.Skia;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.UI;

public partial class ToolbarControl : PhControl
{
    public ToolbarControlModel VM => (ToolbarControlModel)DataContext!;

    public event TEventHandler<object, ToolbarItemClickEventArgs>? ItemClicked;

    private readonly Dictionary<string, List<int>> _configBindingsMap = [];
    private readonly Dictionary<int, ToolbarItemMetadata> _metadataMap = [];
    public readonly List<ToolbarItemModel> _groupPrimaryItemModels = [];
    public readonly List<ToolbarItemModel> _groupSecondaryItemModels = [];
    public readonly List<ToolbarItemModel> _groupOverflowItemModels = [];
    private readonly List<Control> _itemElements = [];
    private double _lastOverflowWidth;
    private double _overflowSlotWidth;
    private bool _overflowUpdateQueued;

    public ObservableCollection<ToolbarItemModel> ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
    public static readonly StyledProperty<ObservableCollection<ToolbarItemModel>> ItemsSourceProperty =
        AvaloniaProperty.Register<ToolbarControl, ObservableCollection<ToolbarItemModel>>(nameof(ItemsSource), []);

    public PlacementMode ItemTooltipPlacement
    {
        get => GetValue(ItemTooltipPlacementProperty);
        set => SetValue(ItemTooltipPlacementProperty, value);
    }
    public static readonly StyledProperty<PlacementMode> ItemTooltipPlacementProperty =
        AvaloniaProperty.Register<ToolbarControl, PlacementMode>(nameof(ItemTooltipPlacement), PlacementMode.Bottom);

    public ToolbarControl()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        Core.Config.PropertyChanged += Config_PropertyChanged;
        ScheduleOverflowUpdate();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Core.Config.PropertyChanged -= Config_PropertyChanged;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (Math.Abs(e.NewSize.Width - _lastOverflowWidth) < 0.5) return;
        _lastOverflowWidth = e.NewSize.Width;

        ScheduleOverflowUpdate();
    }

    private void ScheduleOverflowUpdate()
    {
        if (_overflowUpdateQueued) return;
        _overflowUpdateQueued = true;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _overflowUpdateQueued = false;
            HandleOverflow();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override async void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == ItemsSourceProperty)
        {
            LoadItems();
        }
        else if (e.Property == ItemTooltipPlacementProperty)
        {
            UpdateItemTooltipPlacement();
        }
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        RefreshLanguage();
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (nameof(Core.Config.ToolbarIconHeight).Equals(e.PropertyName))
        {
            _ = VM.OnPropertyChanged(nameof(VM.ItemSpacing));
            _ = VM.OnPropertyChanged(nameof(VM.ItemPadding));
        }

        else
        {
            UpdateButtonCheckState(e.PropertyName);
        }

        if (nameof(Core.Config.ZoomMode).Equals(e.PropertyName))
        {
            _ = VM.OnPropertyChanged(nameof(ToolbarItemModel.IsZoomModeAutoZoom));
        }
    }

    private void ToolbarButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToolbarButton btn) return;

        ItemClicked?.Invoke(btn, new ToolbarItemClickEventArgs(btn.VM));
    }

    private void ToolbarItem_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Control.BoundsProperty) return;
        if (e.NewValue is not Rect bounds) return;
        if (bounds.Width == 0 || bounds.Height == 0) return;
        if (sender is not IToolbarItem item) return;

        if (_metadataMap.TryGetValue(item.VM.SourceIndex, out var meta))
        {
            meta.RenderedWidth = bounds.Width;
        }
    }

    private void PART_BtnOverflowMenu_DropdownOpened(ContextMenu sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu mnu) return;
        mnu.Items.Clear();

        foreach (var item in _groupOverflowItemModels)
        {
            if (item.IsSeparator)
            {
                mnu.Items.Add(new PhMenuItem() { Header = "-" });
                continue;
            }

            if (!item.HasClickAction) continue;

            var mnuItem = new PhMenuItem
            {
                ToggleType = item.IsToggle ? MenuItemToggleType.CheckBox : MenuItemToggleType.None,
                IsChecked = item.IsChecked,
                DataContext = item,
            };

            if (!string.IsNullOrEmpty(item.ImagePath))
            {
                try
                {
                    mnuItem.Icon = new Image
                    {
                        Source = new SvgImage
                        {
                            Source = SvgSource.Load(item.ImagePath, null, SvgCodec.LocalResources),
                        },
                    };
                }
                catch { }
            }

            else if (item.IsPlaceholderIconVisible)
            {
                mnuItem.Icon = new PathIcon
                {
                    Opacity = 0.6,
                    Data = Resx.GetIcon(ResxIconId.IconPlaceholder),
                };
            }

            mnuItem.Header = item.DisplayText;
            mnuItem.HotkeyText = item.HotkeyText;

            mnuItem.Click += OverflowMenuItem_Click;

            mnu.Items.Add(mnuItem);
        }
    }

    private void OverflowMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mnu) return;
        if (mnu.DataContext is not ToolbarItemModel vm) return;

        ItemClicked?.Invoke(mnu, new ToolbarItemClickEventArgs(vm));
    }

    private void PART_BtnMainMenu_DropdownOpened(ContextMenu sender, RoutedEventArgs e)
    {
        RefreshMainMenuState();
    }

    private void MainMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not PhMenuItem mnu) return;

        var action = AppAPIProvider.GetMenuAction(mnu.LangKey);
        _ = Core.API.RunActionAsync(action, mnu.CommandParameter?.ToString());
    }

    public async void MainMenu_ViewChannelItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not PhMenuItem mnu) return;

        var channelType = mnu.CommandParameter?.ToString();
        if (string.IsNullOrEmpty(channelType)) return;

        if (Enum.TryParse<ColorChannels>(channelType, out var channels))
        {
            if (mnu.ToggleType == MenuItemToggleType.CheckBox)
            {
                await SetColorChannelsAsync(channels, mnu.IsChecked);
            }

            else
            {
                await SetColorChannelsAsync(channels, null);
            }
        }
    }

    private void ClearItems()
    {
        foreach (var item in _itemElements)
        {
            item.PropertyChanged -= ToolbarItem_PropertyChanged;

            if (item is ToolbarButton itemBtn)
            {
                itemBtn.Click -= ToolbarButton_Click;
            }
        }

        _itemElements.Clear();
        _metadataMap.Clear();
        _configBindingsMap.Clear();

        _groupPrimaryItemModels.Clear();
        _groupOverflowItemModels.Clear();
        _groupSecondaryItemModels.Clear();

        PART_PrimaryGroup.Children.Clear();
        PART_SecondaryGroup.Children.Clear();
    }

    private void LoadItems()
    {
        ClearItems();

        var primaryList = new List<Control>();
        var secondaryList = new List<Control>();

        int srcIndex = -1;
        int primaryIndex = -1;
        int secondaryIndex = -1;

        foreach (var vm in ItemsSource)
        {
            srcIndex++;
            vm.SourceIndex = srcIndex;

            Control itemEl;
            if (vm.IsSeparator) itemEl = new ToolbarSeparator();
            else
            {
                var itemBtn = new ToolbarButton();
                itemBtn.IsChecked = ComputeCheckState(vm);

                itemBtn.IsEnabled = itemBtn.IsHitTestVisible = vm.HasClickAction;

                itemBtn.Click += ToolbarButton_Click;
                itemEl = itemBtn;
            }

            itemEl.Focusable = false;
            itemEl.DataContext = vm;
            itemEl.PropertyChanged += ToolbarItem_PropertyChanged;

            if (vm.Alignment == ToolbarItemAlignment.Right)
            {
                secondaryIndex++;
                _groupSecondaryItemModels.Add(vm);
                secondaryList.Add(itemEl);
            }
            else
            {
                primaryIndex++;
                _groupPrimaryItemModels.Add(vm);
                primaryList.Add(itemEl);
            }
            _itemElements.Add(itemEl);

            _metadataMap.TryAdd(srcIndex, new ToolbarItemMetadata()
            {
                SourceIndex = srcIndex,
                PrimaryItemIndex = primaryIndex,
                SecondaryItemIndex = secondaryIndex,
                RenderedWidth = 0,
            });

            var bindingIndice = _configBindingsMap.GetValueOrDefault(vm.ConfigBinding) ?? [];
            if (!string.IsNullOrWhiteSpace(vm.ConfigBinding))
            {
                bindingIndice.Add(srcIndex);
                _configBindingsMap[vm.ConfigBinding] = bindingIndice;
            }

            if (!string.IsNullOrWhiteSpace(vm.ConfigBinding))
            {
                vm.IsChecked = ComputeCheckState(vm);
            }
        }

        PART_PrimaryGroup.Children.AddRange(primaryList);
        PART_SecondaryGroup.Children.AddRange(secondaryList);

        UpdateItemTooltipPlacement();
    }

    private void HandleOverflow()
    {
        var rootWidth = Bounds.Width;
        if (rootWidth <= 0.5) return;

        var spacing = ToolbarControlModel.ItemSpacing;
        var iconSize = Core.Config.ToolbarIconHeight;
        var padding = PART_Root.Padding.Left + PART_Root.Padding.Right;

        if (PART_BtnOverflowMenu.IsVisible && PART_BtnOverflowMenu.Bounds.Width > 0)
        {
            _overflowSlotWidth = PART_BtnOverflowMenu.Bounds.Width + spacing;
        }
        var overflowSlot = _overflowSlotWidth > 0 ? _overflowSlotWidth : iconSize + spacing;

        var rightWidth = PART_RightGroup.Bounds.Width
            - (PART_BtnOverflowMenu.IsVisible ? overflowSlot : 0);
        if (rightWidth < 0) rightWidth = 0;

        var primaryCount = _groupPrimaryItemModels.Count;
        var primaryNaturalWidth = 0d;
        foreach (var item in _groupPrimaryItemModels)
        {
            if (_metadataMap.TryGetValue(item.SourceIndex, out var m)) primaryNaturalWidth += m.RenderedWidth;
        }
        primaryNaturalWidth += spacing * Math.Max(0, primaryCount - 1);

        if (primaryCount > 0 && primaryNaturalWidth <= 0) return;

        var availableNoBtn = rootWidth - padding - rightWidth;
        var fitsAll = primaryNaturalWidth <= availableNoBtn;

        var availableWidth = availableNoBtn - (fitsAll ? 0 : overflowSlot);

        _groupOverflowItemModels.Clear();
        var usedWidth = 0d;
        for (var i = 0; i < primaryCount; i++)
        {
            var item = _groupPrimaryItemModels[i];
            if (!_metadataMap.TryGetValue(item.SourceIndex, out var meta)) continue;

            usedWidth += meta.RenderedWidth + (i > 0 ? spacing : 0);
            item.IsNotOverflow = usedWidth <= availableWidth;
            if (!item.IsNotOverflow) _groupOverflowItemModels.Add(item);
        }
        var hasOverflow = _groupOverflowItemModels.Count > 0;
        PART_BtnOverflowMenu.IsVisible = hasOverflow;

        var centeredGap = rootWidth / 2 - primaryNaturalWidth / 2 - rightWidth - iconSize;
        PART_PrimaryGroup.HorizontalAlignment = (!hasOverflow && centeredGap > 0)
            ? Avalonia.Layout.HorizontalAlignment.Center
            : Avalonia.Layout.HorizontalAlignment.Left;
    }

    private void UpdateButtonCheckState(string? configName)
    {
        if (string.IsNullOrWhiteSpace(configName)) return;
        if (_configBindingsMap.GetValueOrDefault(configName) is not List<int> itemIndice) return;

        var items = ItemsSource;
        if (items is null || items.Count == 0) return;

        foreach (var srcIndex in itemIndice)
        {
            if ((uint)srcIndex < (uint)items.Count)
            {
                items[srcIndex].IsChecked = ComputeCheckState(items[srcIndex]);
            }
        }
    }

    private static bool ComputeCheckState(ToolbarItemModel vm)
    {
        var configValue = Core.Config.GetAsString(vm.ConfigBinding);
        var configBindingValue = vm.ConfigBindingValue;
        var configBindingValueEqual = true;

        if (vm.ConfigBindingValue.StartsWith('!'))
        {
            configBindingValue = vm.ConfigBindingValue.Substring(1);
            configBindingValueEqual = false;
        }

        var isChecked = configValue.Equals(configBindingValue, StringComparison.Ordinal);
        if (!configBindingValueEqual) isChecked = !isChecked;

        return isChecked;
    }

    private void RefreshLanguage()
    {
        foreach (var vm in ItemsSource)
        {
            vm.RefreshLanguage();
        }

        if (DataContext is ToolbarControlModel model) model.RefreshLanguage();

        BuildExternalToolMenuItems();
        LocalizeMenuText(PART_MainMenu.Items);

        SyncNativeStates();
    }

    private static void LocalizeMenuText(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is not PhMenuItem mnuItem) continue;

            mnuItem.LocalizeText();

            if (mnuItem.Items.Count > 0)
            {
                LocalizeMenuText(mnuItem.Items);
            }
        }
    }

    private void UpdateMenuTextIfNeeded()
    {
        if (PART_MainMenu.Items is not ItemCollection items) return;

        LoadMenuText(items);
    }

    private static void LoadMenuText(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is not PhMenuItem mnuItem) continue;

            if (mnuItem.Items.Count > 0)
            {
                LoadMenuText(mnuItem.Items);
            }

            mnuItem.HotkeyText = AppAPIProvider.GetMenuHotkeyText(mnuItem.LangKey);
        }
    }

    private void UpdateItemTooltipPlacement()
    {
        var vOffset = 0;
        if (ItemTooltipPlacement is PlacementMode.Bottom
            or PlacementMode.BottomEdgeAlignedLeft
            or PlacementMode.BottomEdgeAlignedRight)
        {
            vOffset = 8;
        }

        ToolTip.SetPlacement(PART_BtnMainMenu, ItemTooltipPlacement);
        ToolTip.SetVerticalOffset(PART_BtnMainMenu, vOffset);

        ToolTip.SetPlacement(PART_OverflowMenu, ItemTooltipPlacement);
        ToolTip.SetVerticalOffset(PART_OverflowMenu, vOffset);

        foreach (var itemEl in _itemElements)
        {
            ToolTip.SetPlacement(itemEl, ItemTooltipPlacement);
            ToolTip.SetVerticalOffset(itemEl, vOffset);
        }
    }

    private async Task SetColorChannelsAsync(ColorChannels channels, bool? isEnabled)
    {
        var newChannels = Core.ColorChannels;

        if (isEnabled is not null)
        {
            if (IsEnabled)
            {
                newChannels ^= channels;
            }
            else
            {
                newChannels |= channels;
            }
        }

        else
        {
            newChannels = channels;
        }

        _ = await Core.API.RunApiAsync(API.IG_SetColorChannels, newChannels.ToString());
    }

    private void BuildExternalToolMenuItems()
    {
        var items = PART_MnuTools.Items;
        var sepEndIndex = items.IndexOf(PART_MnuExternalToolsEndSeparator);
        if (sepEndIndex < 0) return;

        var toRemove = items
            .OfType<PhMenuItem>()
            .Where(m => m.Tag is string tag && tag == "external_tool")
            .ToList();
        foreach (var m in toRemove) items.Remove(m);

        sepEndIndex = items.IndexOf(PART_MnuExternalToolsEndSeparator);

        var tools = Core.ToolRegistry.GetAllExternalToolManifests().ToList();
        if (tools.Count == 0) return;

        PART_MnuExternalToolsEndSeparator.IsVisible = true;

        for (var i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];

            var hotkeyText = tool.Hotkeys.Length > 0
                ? string.Join(", ", tool.Hotkeys.Select(hk => hk.KeyString))
                : string.Empty;

            var mnu = new PhMenuItem
            {
                Header = tool.DisplayName,
                HotkeyText = hotkeyText,
                Tag = "external_tool",
                CommandParameter = tool.ToolId,
            };
            mnu.Click += async (_, _) => await Core.API.RunApiAsync(API.IG_OpenTool, tool.ToolId);
            items.Insert(sepEndIndex + i, mnu);
        }
    }

    public void RefreshMainMenuState()
    {
        UpdateMenuTextIfNeeded();

        EditingApp.UpdateAppNameForMenuEdit(PART_MnuEdit);

        UpdateMenuItemEnableStates();

        BuildExternalToolMenuItems();
    }

    private void UpdateMenuItemEnableStates()
    {
        var isAnimator = Core.Photos.Current?.Bitmap is AnimatorImpl;
        PART_MnuToggleImageAnimation.IsEnabled = isAnimator;
        PART_MnuViewChannels.IsEnabled
            = PART_MnuInvertColors.IsEnabled
            = PART_MnuRotateLeft.IsEnabled
            = PART_MnuRotateRight.IsEnabled
            = PART_MnuFlipHorizontal.IsEnabled
            = PART_MnuFlipVertical.IsEnabled
            = !isAnimator;

        var hasMultiFrames = Core.Photos.CurrentMetadata?.FrameCount > 1;
        PART_MnuExportFrames.IsEnabled
            = PART_MnuViewNextFrame.IsEnabled
            = PART_MnuViewPreviousFrame.IsEnabled
            = PART_MnuViewFirstFrame.IsEnabled
            = PART_MnuViewLastFrame.IsEnabled
            = hasMultiFrames;

    }

    public (Dictionary<LangId, string> Paths, HashSet<LangId> AllKeys) GetMenuActionMap()
    {
        var paths = new Dictionary<LangId, string>();
        var allKeys = new HashSet<LangId>();
        CollectMenuActions(PART_MainMenu.Items, [], true, paths, allKeys);
        return (paths, allKeys);
    }

    private static void CollectMenuActions(ItemCollection items, List<string> parents, bool ancestorsVisible,
        Dictionary<LangId, string> paths, HashSet<LangId> allKeys)
    {
        foreach (var it in items)
        {
            if (it is not PhMenuItem item) continue;

            if (item.LangKey is LangId key) allKeys.Add(key);
            var visible = ancestorsVisible && item.IsVisible;

            if (item.Items.Count > 0)
            {
                CollectMenuActions(item.Items, [.. parents, GetNativeHeader(item)], visible, paths, allKeys);
            }
            else if (visible && item.LangKey is LangId leafKey)
            {
                paths[leafKey] = string.Join(" / ", parents.Append(GetNativeHeader(item)));
            }
        }
    }

}

public class ToolbarItemClickEventArgs(ToolbarItemModel vm) : RoutedEventArgs
{
    public ToolbarItemModel VM => vm;
}

public record ToolbarItemMetadata
{
    public int SourceIndex { get; set; } = -1;
    public int PrimaryItemIndex { get; set; } = -1;
    public int SecondaryItemIndex { get; set; } = -1;
    public double RenderedWidth { get; set; } = 0;
}
