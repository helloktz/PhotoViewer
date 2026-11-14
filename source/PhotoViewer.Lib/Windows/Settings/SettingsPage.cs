using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PhotoViewer.Common.Localization;
using PhotoViewer.UI;
using System;

namespace PhotoViewer.Common.Windows;

public sealed class SettingsPage : PhControl
{
    private bool _isBuilt;
    private readonly SettingsViewModel _vm;
    private readonly Func<SettingsViewModel, SettingsNavId, LangId?, Control> _createView;

    public SettingsNavId NavId { get; }

    public LangId? NavLabel { get; set; }

    public SettingsPage(SettingsViewModel vm, SettingsNavId navId,
        Func<SettingsViewModel, SettingsNavId, LangId?, Control> createView)
    {
        _vm = vm;
        NavId = navId;
        _createView = createView;
    }

    public void EnsureBuilt()
    {
        if (_isBuilt) return;
        _isBuilt = true;

        Content = _createView(_vm, NavId, NavLabel);
    }

    public static void ScrollToItem(SettingItem item)
    {
        var target = item.Target;
        if (target is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            target.BringIntoView();
            target.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }
}
