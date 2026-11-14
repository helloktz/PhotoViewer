using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.UI;

public partial class ToolbarControl
{
    private static readonly LangId[] _windowModeKeys =
    [
        LangId.Menu_MnuWindowFit,
        LangId.Menu_MnuFrameless,
        LangId.Menu_MnuFullScreen,
        LangId.Menu_MnuSlideshow,
    ];

    private readonly List<(NativeMenuItem Native, PhMenuItem Source)> _nativeMenuPairs = [];

    public NativeMenu BuildNativeWindowMenu()
    {
        PrepareSourceMenu();

        var root = new NativeMenu();

        foreach (var src in PART_MainMenu.Items)
        {
            if (src is not PhMenuItem item || item.Items.Count == 0) continue;
            if (!item.IsVisible) continue;

            var topItem = new NativeMenuItem { Header = GetNativeHeader(item) };
            var submenu = new NativeMenu();
            BuildNativeItems(submenu, item.Items);

            if (item.LangKey == LangId.Menu_MnuLayout)
            {
                submenu.Add(new NativeMenuItemSeparator());
                foreach (var key in _windowModeKeys)
                {
                    var wm = FindSourceItem(key);
                    if (wm is not null && wm.IsVisible) submenu.Add(CreateNativeLeaf(wm));
                }
            }

            topItem.Menu = submenu;
            submenu.Opening += (_, _) => SyncNativeStates();

            _nativeMenuPairs.Add((topItem, item));
            root.Add(topItem);
        }

        return root;
    }

    private void PrepareSourceMenu()
    {
        _nativeMenuPairs.Clear();
        if (DataContext is not ToolbarControlModel vm) return;

        PART_MainMenu.DataContext = vm.ButtonMenuVM;
        RefreshMainMenuState();
        _ = vm.ButtonMenuVM.OnPropertyChanged(string.Empty);
    }

    private void BuildNativeItems(NativeMenu target, ItemCollection sourceItems)
    {
        foreach (var src in sourceItems)
        {
            if (src is not PhMenuItem item) continue;
            if (!item.IsVisible) continue;

            if (item.Header is "-")
            {
                target.Add(new NativeMenuItemSeparator());
                continue;
            }

            if (item.Items.Count > 0)
            {
                var nativeParent = new NativeMenuItem { Header = GetNativeHeader(item) };
                var submenu = new NativeMenu();
                BuildNativeItems(submenu, item.Items);
                nativeParent.Menu = submenu;
                submenu.Opening += (_, _) => SyncNativeStates();

                _nativeMenuPairs.Add((nativeParent, item));
                target.Add(nativeParent);
            }
            else
            {
                target.Add(CreateNativeLeaf(item));
            }
        }
    }

    private NativeMenuItem CreateNativeLeaf(PhMenuItem source, KeyGesture? gesture = null)
    {
        var nativeItem = new NativeMenuItem
        {
            Header = GetNativeHeader(source),
            IsEnabled = source.IsEnabled,
            ToggleType = source.ToggleType,
            IsChecked = source.IsChecked,
            Gesture = gesture ?? GetNativeGesture(source),
        };

        var clickTarget = source;
        nativeItem.Click += (_, _) =>
            clickTarget.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        _nativeMenuPairs.Add((nativeItem, source));
        return nativeItem;
    }

    private void SyncNativeStates()
    {
        UpdateMenuItemEnableStates();
        EditingApp.UpdateAppNameForMenuEdit(PART_MnuEdit);
        if (DataContext is ToolbarControlModel vm)
            _ = vm.ButtonMenuVM.OnPropertyChanged(string.Empty);

        foreach (var (native, source) in _nativeMenuPairs)
        {
            native.IsEnabled = source.IsEnabled;
            native.IsChecked = source.IsChecked;
            native.Header = GetNativeHeader(source);
        }
    }

    private static string GetNativeHeader(PhMenuItem item)
    {
        if (item.Header is string h && !string.IsNullOrWhiteSpace(h) && h != "-") return h;

        if (item.LangKey is not null)
        {
            var text = Core.Lang[item.LangKey, item.LangParams];
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return item.Header?.ToString() ?? string.Empty;
    }

    private static KeyGesture? GetNativeGesture(PhMenuItem item)
    {
        if (item.LangKey is not LangId key) return null;

        var hotkeys = Core.Config.MenuHotkeys.GetValueOrDefault(key);
        if (hotkeys is null || hotkeys.Length == 0)
        {
            hotkeys = AppAPIProvider.GetMenuAction(key)?.Hotkeys;
        }

        var hk = hotkeys?.FirstOrDefault();
        if (hk is null || hk.Key == Key.None) return null;

        return hk.ToGesture();
    }

    private PhMenuItem? FindSourceItem(LangId langKey) => FindSourceItem(PART_MainMenu.Items, langKey);

    private static PhMenuItem? FindSourceItem(ItemCollection items, LangId langKey)
    {
        foreach (var it in items)
        {
            if (it is not PhMenuItem item) continue;
            if (item.LangKey == langKey) return item;

            if (item.Items.Count > 0)
            {
                var found = FindSourceItem(item.Items, langKey);
                if (found is not null) return found;
            }
        }

        return null;
    }
}
