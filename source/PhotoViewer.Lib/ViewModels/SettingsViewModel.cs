using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public sealed class SettingsViewModel : PhReactive
{
    private readonly Dictionary<ConfigId, object?> _pending = [];

    public SettingsRegistry Registry { get; } = new();

    public bool IsDirty => _pending.Count > 0;

    public T GetValue<T>(ConfigId id, T defaultValue)
    {
        if (_pending.TryGetValue(id, out var v) && v is T staged) return staged;
        return Core.Config.Get(id, defaultValue);
    }

    public void SetValue(ConfigId id, object? value)
    {
        _pending[id] = value;
        _ = OnPropertyChanged(value, null, id.ToString());
    }

    public async Task<bool> CommitAsync()
    {
        if (_pending.Count == 0) return true;

        var changedIds = new List<ConfigId>(_pending.Count);
        foreach (var (id, value) in _pending)
        {
            if (value is null || Config.IsConfigLocked(id)) continue;
            Core.Config.Set(id, value);
            changedIds.Add(id);
        }
        _pending.Clear();

        var isSaved = await Core.Config.SaveAsync();

        RunApplyActions(changedIds);

        return isSaved;
    }

    public void Discard() => _pending.Clear();

    internal static void RunApplyActions(IReadOnlyList<ConfigId> changedIds)
    {
        var reloadList = changedIds.Any(static id => id
            is ConfigId.ImageLoadingOrder
            or ConfigId.ImageLoadingOrderType
            or ConfigId.EnableExplorerSortOrder
            or ConfigId.EnableSubfoldersLoading
            or ConfigId.EnableImageFolderGrouping
            or ConfigId.EnableHiddenImagesLoading);

        var reloadPhoto = changedIds.Any(static id => id
            is ConfigId.EnableOnlyLoadRawPreview
            or ConfigId.EnableOnlyLoadNonRawPreview
            or ConfigId.EnableHdrToneMapping
            or ConfigId.EnableAlwaysApplyColorProfile
            or ConfigId.EnableVectorRenderer);

        if (changedIds.Contains(ConfigId.ColorProfile))
        {
            Core.UpdateDestColorProfile();
        }

        if (changedIds.Any(static id => id
            is ConfigId.EnableVectorRenderer
            or ConfigId.EnableOnlyLoadRawPreview
            or ConfigId.EnableOnlyLoadNonRawPreview))
        {
            Core.CodecRegistry.InvalidateSelectionCaches();
        }

        if (changedIds.Contains(ConfigId.Language))
        {
            _ = Core.Config.LoadCurrentLanguageAsync();
        }

        if (changedIds.Contains(ConfigId.Tools))
        {
            Core.ReloadExternalTools();
        }

        if (changedIds.Contains(ConfigId.ToolbarButtons)
            || changedIds.Contains(ConfigId.MenuHotkeys)
            || changedIds.Contains(ConfigId.Tools))
        {
            Core.API.RegisterHotkeys();
        }

        if (changedIds.Any(static id => id is ConfigId.DarkTheme or ConfigId.LightTheme)
            && Avalonia.Application.Current is App app)
        {
            _ = app.ApplyThemePackAsync(Core.IsSystemDarkMode);
        }

        if (reloadList) AppAPIProvider.IG_ReloadList();
        if (reloadPhoto) AppAPIProvider.IG_Reload(false);
    }
}
