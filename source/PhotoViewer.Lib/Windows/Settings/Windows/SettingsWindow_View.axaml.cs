using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.Common.Windows;

public partial class SettingsWindowView : PhControl
{
    private SettingsViewModel _vm = null!;
    private List<SettingsNavItem> _navItems = [];
    private readonly Dictionary<SettingsNavId, SettingsPage> _pages = [];

    private static readonly Hotkey _searchHotkey = new(Hotkey.Ctrl, Key.K);
    private TopLevel? _topLevel;

    private bool _sidebarPressing;
    private bool _sidebarTapped;

    public SettingsNavId CurrentNavId { get; private set; }

    public SettingsWindowView()
    {
        InitializeComponent();
    }

    public SettingsWindowView(SettingsViewModel vm) : this()
    {
        InitSettingsPage(vm);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, TopLevel_KeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(KeyDownEvent, TopLevel_KeyDown);
        _topLevel = null;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        if (PART_Search is not null)
        {
            PART_Search.PlaceholderText = GetSearchPlaceholder();
        }

        if (PART_Sidebar is not null)
        {
            PART_Sidebar.ItemsSource = null;
            PART_Sidebar.ItemsSource = _navItems;
            NavigateTo(CurrentNavId);
        }
    }

    private void Sidebar_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_sidebarPressing) return;
        if (PART_Sidebar.SelectedItem is SettingsNavItem nav) ShowPage(nav);
    }

    private void Sidebar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _sidebarPressing = true;
        _sidebarTapped = false;
    }

    private void Sidebar_Tapped(object? sender, TappedEventArgs e)
    {
        _sidebarTapped = true;
        if (PART_Sidebar.SelectedItem is SettingsNavItem nav) ShowPage(nav);
    }

    private void Sidebar_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_sidebarPressing) return;
        _sidebarPressing = false;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_sidebarTapped
                && PART_Sidebar.SelectedItem is SettingsNavItem nav
                && nav.NavId != CurrentNavId)
            {
                NavigateTo(CurrentNavId);
            }
        }, DispatcherPriority.Input);
    }

    private void TopLevel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != _searchHotkey.Key || e.KeyModifiers != _searchHotkey.Modifiers) return;

        FocusSearch();
        PART_Search.TextBox.SelectAll();
        e.Handled = true;
    }

    private void TxtSearch_TextChanged(object? sender, EventArgs e)
    {
        UpdateSearchResults();
    }

    private void TxtSearch_GotFocus(object? sender, FocusChangedEventArgs e)
    {
        TryReopenSearchPopup();
    }

    private void TxtSearch_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        TryReopenSearchPopup();
    }

    private void TxtSearch_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (!PART_SearchPopup.IsOpen) return;

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is not null && focused.GetSelfAndVisualAncestors().Contains(PART_SearchResults))
            return;

        PART_SearchPopup.IsOpen = false;
    }

    private void TryReopenSearchPopup()
    {
        if (PART_Search.IsClearButtonPointerOver) return;

        if (!PART_SearchPopup.IsOpen && !string.IsNullOrEmpty(PART_Search.Text))
        {
            UpdateSearchResults();
        }
    }

    private void UpdateSearchResults()
    {
        var results = _vm.Registry.Search(PART_Search.Text).Take(25).ToList();
        PART_SearchResults.ItemsSource = results;
        PART_SearchPopup.IsOpen = results.Count > 0;

        PART_SearchResults.SelectedIndex = results.Count > 0 ? 0 : -1;
    }

    private void TxtSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!PART_SearchPopup.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                MoveSearchSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSearchSelection(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                if (PART_SearchResults.SelectedItem is SettingItem item)
                {
                    e.Handled = true;
                    JumpToSetting(item);
                }
                break;

            case Key.Escape:
                PART_SearchPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void MoveSearchSelection(int delta)
    {
        var count = PART_SearchResults.ItemCount;
        if (count == 0) return;

        var index = PART_SearchResults.SelectedIndex + delta;
        if (index < 0) index = count - 1;
        else if (index >= count) index = 0;

        PART_SearchResults.SelectedIndex = index;
        PART_SearchResults.ScrollIntoView(index);
    }

    private void SearchResults_Tapped(object? sender, TappedEventArgs e)
    {
        if (PART_SearchResults.SelectedItem is SettingItem item)
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(() => JumpToSetting(item));
        }
    }

    private void InitSettingsPage(SettingsViewModel vm)
    {
        _vm = vm;
        _navItems = SettingsNavItem.CreateDefaultList();

        foreach (var navItem in _navItems)
        {
            var page = navItem.CreatePage(_vm);
            page.NavLabel = navItem.Label;
            page.EnsureBuilt();
            _pages[navItem.NavId] = page;
        }

        _vm.Registry.DisableLockedControls();
        PART_LockWarning.IsVisible = Config.AdminLockedConfigs.Count > 0;

        PART_Search.PlaceholderText = GetSearchPlaceholder();
        PART_Search.TextChanged += TxtSearch_TextChanged;
        PART_Search.TextBox.KeyDown += TxtSearch_KeyDown;
        PART_Search.TextBox.GotFocus += TxtSearch_GotFocus;
        PART_Search.TextBox.LostFocus += TxtSearch_LostFocus;
        PART_Search.TextBox.AddHandler(PointerReleasedEvent, TxtSearch_PointerReleased, RoutingStrategies.Tunnel);
        PART_SearchResults.Tapped += SearchResults_Tapped;

        PART_Sidebar.ItemsSource = _navItems;
        PART_Sidebar.SelectionChanged += Sidebar_SelectionChanged;
        PART_Sidebar.AddHandler(PointerPressedEvent, Sidebar_PointerPressed, RoutingStrategies.Tunnel);
        PART_Sidebar.AddHandler(PointerReleasedEvent, Sidebar_PointerReleased, RoutingStrategies.Tunnel);
        PART_Sidebar.Tapped += Sidebar_Tapped;

        NavigateTo(_navItems[0].NavId);
    }

    public void FocusSearch() => PART_Search?.FocusSearch();

    private static string GetSearchPlaceholder() => $"{Core.Lang[LangId.Settings_SearchPlaceholder]} ({_searchHotkey.KeyString})";

    public void NavigateTo(SettingsNavId navId)
    {
        var item = _navItems.FirstOrDefault(i => i.NavId == navId);
        if (item is null) return;

        PART_Sidebar.SelectedItem = item;
    }

    public void NavigateToPlugin(string pluginId)
    {
        NavigateTo(SettingsNavId.Plugins);

        if (_pages.TryGetValue(SettingsNavId.Plugins, out var page) && page.Content is PluginsSettingsView view)
        {
            view.FocusPlugin(pluginId);
        }
    }

    public void NavigateToTool(string? toolId)
    {
        if (string.IsNullOrEmpty(toolId)) return;

        NavigateTo(SettingsNavId.Tools);

        if (_pages.TryGetValue(SettingsNavId.Tools, out var page) && page.Content is ToolsSettingsView view)
        {
            Dispatcher.UIThread.Post(() => _ = view.EditToolAsync(toolId));
        }
    }

    public void NotifyPluginsChanged()
    {
        if (_pages.TryGetValue(SettingsNavId.FileAssociations, out var page)
            && page.Content is FileTypeAssociationsSettingsView view)
        {
            view.RefreshCodecFormats();
        }
    }

    public void NavigateToConfig(string? configId)
    {
        if (_vm.Registry.FindByConfigId(configId) is { } item)
        {
            JumpToSetting(item);
            return;
        }

        if (Enum.TryParse<SettingsNavId>(configId, true, out var navId)
            && _navItems.Any(i => i.NavId == navId))
        {
            NavigateTo(navId);
        }
    }

    private void ShowPage(SettingsNavItem nav)
    {
        if (!_pages.TryGetValue(nav.NavId, out var page)) return;

        PART_ContentHost.Content = page;
        PART_Title.Text = nav.LabelText;
        CurrentNavId = nav.NavId;

        Core.Config.LastOpenedSetting = nav.NavId.ToString();
    }

    private void JumpToSetting(SettingItem item)
    {
        PART_Search.FocusSearch();

        PART_SearchResults.SelectedItem = null;
        PART_SearchPopup.IsOpen = false;

        NavigateTo(item.PageNavId);
        if (_pages.TryGetValue(item.PageNavId, out var page))
        {
            SettingsPage.ScrollToItem(item);
        }
    }

}
