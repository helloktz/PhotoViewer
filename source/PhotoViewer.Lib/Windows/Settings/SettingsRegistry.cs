using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.Common.Windows;

public sealed class SettingsRegistry
{
    private readonly List<SettingItem> _items = [];

    public IReadOnlyList<SettingItem> Items => _items;

    public void Register(SettingItem item) => _items.Add(item);

    public void Clear() => _items.Clear();

    public void DisableLockedControls()
    {
        foreach (var item in _items)
        {
            if (item.Id is { } id && item.Target is { } target && Config.IsConfigLocked(id))
            {
                target.IsEnabled = false;
            }
        }
    }

    public SettingItem? FindByConfigId(string? configId)
    {
        if (string.IsNullOrWhiteSpace(configId)) return null;
        if (!Enum.TryParse<ConfigId>(configId, out var id)) return null;

        return _items.FirstOrDefault(i => i.Id == id);
    }

    public IEnumerable<SettingItem> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        var q = query.Trim();

        foreach (var item in _items)
        {
            var matchLabel = item.LabelText.Contains(q, StringComparison.OrdinalIgnoreCase);
            var matchId = item.Id is { } id
                && id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);

            if (matchLabel || matchId) yield return item;
        }
    }
}
