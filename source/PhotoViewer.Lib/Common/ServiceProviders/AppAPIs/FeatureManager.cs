using Avalonia.Controls;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using System;
using System.Collections.Frozen;
using System.Threading;

namespace PhotoViewer.Common.ServiceProviders;

internal static class FeatureManager
{
    private static FrozenSet<string> _locked = FrozenSet<string>.Empty;
    private static readonly Lock _lock = new();

    public static void Refresh()
    {
        var newLocked = Config.LockedFeatures;

        lock (_lock)
        {
            _locked = newLocked;
        }
    }

    public static bool IsLocked(API api) => _locked.Contains(api.ToString("G"));

    public static bool IsLocked(string? apiName) => !string.IsNullOrEmpty(apiName) && _locked.Contains(apiName);

    public static bool IsZoomLocked() => IsLocked(API.IG_ZoomIn) || IsLocked(API.IG_ZoomOut);

    public static bool IsPanLocked() => IsLocked(API.IG_PanLeft) || IsLocked(API.IG_PanRight)
        || IsLocked(API.IG_PanUp) || IsLocked(API.IG_PanDown);

    public static bool IsLocked(LangId? langKey)
    {
        var action = AppAPIProvider.GetMenuAction(langKey);
        return IsLocked(action?.Executable);
    }

    public static void HideLockedMenuItems(ItemCollection items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is not PhMenuItem mnu) continue;

            if (IsLocked(mnu.LangKey))
            {
                items.RemoveAt(i);
                continue;
            }

            if (mnu.Items.Count > 0)
            {
                HideLockedMenuItems(mnu.Items);

                if (mnu.Items.Count == 0)
                {
                    items.RemoveAt(i);
                }
            }
        }

        CleanupSeparators(items);
    }

    private static void CleanupSeparators(ItemCollection items)
    {
        while (items.Count > 0 && items[0] is PhMenuItem { Header: "-" })
        {
            items.RemoveAt(0);
        }

        while (items.Count > 0 && items[^1] is PhMenuItem { Header: "-" })
        {
            items.RemoveAt(items.Count - 1);
        }

        for (int i = items.Count - 2; i >= 0; i--)
        {
            if (items[i] is PhMenuItem { Header: "-" } &&
                items[i + 1] is PhMenuItem { Header: "-" })
            {
                items.RemoveAt(i + 1);
            }
        }
    }
}
