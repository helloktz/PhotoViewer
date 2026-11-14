using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.Plugins;
using ImageGlass.SDK.Plugins;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class PluginsSettingsView : SettingsPageView
{
    private const double NAME_MAX_WIDTH = 220;

    private const string PLUGIN_PACKAGE_PATTERN = "*.igplugin.zip";

    private readonly List<(PluginManifest Manifest, string Dir)> _plugins = [];

    public PluginsSettingsView()
    {
        InitializeComponent();
    }

    public PluginsSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        SetLocalizedText(PART_AddPlugin, LangId._Add);
        PART_AddPlugin.Click += async (_, _) => await AddPluginsAsync();

        SetLocalizedText(PART_OpenFolder, LangId.Settings_Plugins_OpenPluginFolder);
        PART_OpenFolder.Click += (_, _) => BHelper.OpenFolderPath(BHelper.GetRealPlatformConfigDir(Dir.Plugins));

        ReloadPlugins();

        AddLangRefresher(RebuildTable);

        RegisterSearchKey(PART_AddPlugin, LangId.Settings_Nav_Plugins, null, LangId.Settings_Nav_Plugins);

        DisableIfLocked(ConfigId.PluginTrust, PART_AddPlugin, PART_Table);
    }

    private void ReloadPlugins()
    {
        _plugins.Clear();
        _plugins.AddRange(PluginRegistry.DiscoverManifests(BHelper.ConfigDir(Dir.Plugins)));
    }

    private async Task AddPluginsAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(Core.Lang[LangId.Settings_Nav_Plugins])
                {
                    Patterns = [PLUGIN_PACKAGE_PATTERN],
                },
            ],
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();
        if (paths.Count == 0) return;

        var pluginsDir = BHelper.ConfigDir(Dir.Plugins);
        var installedIds = await Task.Run(() =>
        {
            var ids = new List<string>();
            foreach (var file in paths)
            {
                if (!File.Exists(file)) continue;

                var id = InstallPackage(file, pluginsDir);
                if (id is not null) ids.Add(id);
            }
            return ids;
        });

        ReloadPlugins();

        try { await Core.PluginDiscoveryTask; } catch { }

        var reconciled = await ReconcileInstalledAsync(installedIds);
        RebuildTable();

        if (installedIds.Count > 0)
        {
            var hint = Core.Lang[LangId.Settings_Plugins_InstallSuccess];
            if (reconciled.LoadError is not null) hint += ". " + reconciled.LoadError;
            else if (reconciled.Pending > 0) hint += ". " + Core.Lang[LangId.Settings_Plugins_EnableToLoad];

            ShowHint(hint, isWarning: reconciled.LoadError is not null);
        }
    }

    private async Task<(int Pending, string? LoadError)> ReconcileInstalledAsync(List<string> pluginIds)
    {
        var pending = 0;
        string? loadError = null;

        foreach (var pluginId in pluginIds)
        {
            var index = _plugins.FindIndex(p => string.Equals(p.Manifest.Id, pluginId, StringComparison.Ordinal));
            if (index < 0) continue;

            var plugin = _plugins[index];
            var state = PluginTrustPolicy.GetState(plugin.Manifest, plugin.Dir);
            var wasLoaded = Core.PluginRegistry.IsLoaded(pluginId);

            if (state == PluginTrustPolicy.TrustState.Trusted)
            {
                if (!wasLoaded) await Core.EnablePluginAsync(plugin.Manifest, plugin.Dir);
                loadError ??= LoadErrorOf(pluginId);
                continue;
            }

            if (wasLoaded) Core.DisablePlugin(pluginId);
            pending++;
        }

        return (pending, loadError);
    }

    private static string? LoadErrorOf(string pluginId)
    {
        if (Core.PluginRegistry.IsLoaded(pluginId)) return null;

        return Core.PluginRegistry.GetLoadError(pluginId) ?? "IGE_PLUGIN_LOAD_FAILED";
    }

    private void ShowHint(string text, bool isWarning = false)
    {
        PART_InstallHint.Text = text;
        PART_HintContainer[!Border.BackgroundProperty] = Resx.CreateBinding(isWarning
            ? ResxId.IG_BackgroundWarningBrush
            : ResxId.IG_BackgroundInfoBrush);
        PART_HintContainer.IsVisible = true;
    }

    private static string? InstallPackage(string packageFile, string pluginsDir)
    {
        var staging = Path.Combine(Path.GetTempPath(), "ig_plugin_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(packageFile, staging, overwriteFiles: true);

            var manifestPath = FindManifest(staging);
            if (manifestPath is null) return null;

            var manifest = ReadManifest(manifestPath);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id)) return null;

            var srcDir = Path.GetDirectoryName(manifestPath)!;
            var destDir = Path.Combine(pluginsDir, MakeSafeFolderName(manifest.Id));

            ReplaceInstall(srcDir, destDir, pluginsDir);
            return manifest.Id;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static void ReplaceInstall(string srcDir, string destDir, string pluginsDir)
    {
        string? stash = null;
        if (Directory.Exists(destDir))
        {
            var trashRoot = Path.Combine(pluginsDir, PluginRegistry.TRASH_DIR_NAME);
            Directory.CreateDirectory(trashRoot);
            stash = Path.Combine(trashRoot, Guid.NewGuid().ToString("N"));
            Directory.Move(destDir, stash);
        }

        try
        {
            MoveDirectory(srcDir, destDir);
        }
        catch
        {
            try { if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true); } catch { }
            if (stash is not null && !Directory.Exists(destDir)) Directory.Move(stash, destDir);
            throw;
        }

        if (stash is not null)
        {
            try { Directory.Delete(stash, recursive: true); } catch { }
        }
    }

    private static string? FindManifest(string root)
    {
        var direct = Path.Combine(root, PluginManifest.FILE_NAME);
        if (File.Exists(direct)) return direct;

        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            var candidate = Path.Combine(sub, PluginManifest.FILE_NAME);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static PluginManifest? ReadManifest(string manifestPath)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginManifest);
        }
        catch
        {
            return null;
        }
    }

    private static void MoveDirectory(string src, string dest)
    {
        try
        {
            Directory.Move(src, dest);
            return;
        }
        catch { }

        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(src, dir)));
        }
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            File.Copy(f, Path.Combine(dest, Path.GetRelativePath(src, f)), overwrite: true);
        }
    }

    private static string MakeSafeFolderName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
        }
        return new string(chars);
    }

    private Border ToggleCell((PluginManifest Manifest, string Dir) plugin, PluginTrustPolicy.TrustState state)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = state == PluginTrustPolicy.TrustState.Trusted,
            IsEnabled = state != PluginTrustPolicy.TrustState.Missing,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            OnContent = null,
            OffContent = null,
            Width = 40,
        };

        toggle.Click += async (_, _) =>
        {
            _ = state == PluginTrustPolicy.TrustState.Trusted
                ? await DisablePluginAsync(plugin)
                : await EditPluginAsync(plugin);

            RebuildTable();
        };

        return PhTableControl.WrapCell(toggle);
    }

    public void FocusPlugin(string pluginId) => PART_Table.FlashRow(pluginId);

    private async Task<bool> EditPluginAsync((PluginManifest Manifest, string Dir) plugin)
    {
        var state = PluginTrustPolicy.GetState(plugin.Manifest, plugin.Dir);

        var mode = state == PluginTrustPolicy.TrustState.Missing
            ? PluginInfoWindowMode.View
            : Core.PluginRegistry.IsLoaded(plugin.Manifest.Id)
                ? PluginInfoWindowMode.Configure
                : PluginInfoWindowMode.Enable;

        var win = new PluginInfoWindow(plugin.Manifest, plugin.Dir, mode,
            hashChanged: state == PluginTrustPolicy.TrustState.Changed,
            enableAsync: () => EnablePluginAsync(plugin));
        var result = await win.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow);

        if (win.DeleteRequested)
        {
            await DeletePluginAsync(plugin);
            return false;
        }

        var changed = win.PluginEnabled;
        if (result != DialogExitCode.OK) return changed;

        var saved = await ApplyPluginChoicesAsync(plugin, win);
        return changed || saved;
    }

    private static async Task<bool> ApplyPluginChoicesAsync((PluginManifest Manifest, string Dir) plugin,
        PluginInfoWindow win)
    {
        if (!win.ChoicesChanged) return false;

        var saved = await PluginTrustPolicy.SetExtensionExclusionsAsync(plugin.Manifest.Id,
            win.DisabledDecodeExtensions, win.DisabledEncodeExtensions);
        if (!saved) return false;

        Core.ReloadPluginCodecs(plugin.Manifest.Id);
        return true;
    }

    private async Task DeletePluginAsync((PluginManifest Manifest, string Dir) plugin)
    {
        var owner = TopLevel.GetTopLevel(this) as PhWindow;
        var m = plugin.Manifest;
        var name = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name;
        var desc = new List<string> { name + Environment.NewLine, m.Description ?? string.Empty };

        var confirm = await ModalWindow.ShowWarningAsync(owner, new ModalWindowOptions
        {
            Title = name,
            Heading = Core.Lang[LangId.Settings_Plugins_DeleteConfirm],
            Description = string.Join(Environment.NewLine, desc),
            Note = plugin.Dir,
            NoteStyle = InfoBarSeverity.Info,
        }, ModalWindowButton.Yes_No);
        if (confirm.ExitCode != DialogExitCode.OK) return;

        if (!await PluginTrustPolicy.RemoveAsync(plugin.Manifest.Id)) return;

        Core.DisablePlugin(plugin.Manifest.Id);
        DeletePluginFolder(plugin.Dir);

        ReloadPlugins();
        RebuildTable();
    }

    private static void DeletePluginFolder(string pluginDir)
    {
        if (!Directory.Exists(pluginDir)) return;

        try
        {
            Directory.Delete(pluginDir, recursive: true);
            return;
        }
        catch { }

        try
        {
            var trashRoot = Path.Combine(BHelper.ConfigDir(Dir.Plugins), PluginRegistry.TRASH_DIR_NAME);
            Directory.CreateDirectory(trashRoot);
            Directory.Move(pluginDir, Path.Combine(trashRoot, Guid.NewGuid().ToString("N")));
        }
        catch { }
    }

    private async Task<bool> EnablePluginAsync((PluginManifest Manifest, string Dir) plugin)
    {
        if (!await PluginTrustPolicy.TrustAsync(plugin.Manifest, plugin.Dir)) return false;

        await Core.EnablePluginAsync(plugin.Manifest, plugin.Dir, formatsOff: true);

        if (LoadErrorOf(plugin.Manifest.Id) is { } error) ShowHint(error, isWarning: true);

        return true;
    }

    private void RebuildTable()
    {
        PhTableColumn[] columns =
        [
            new() { Header = string.Empty },
            new() { Header = Core.Lang[LangId._Name] },
            new() { Header = Core.Lang[LangId._Type] },
            new() { Header = Core.Lang[LangId.Settings_Plugins_Status], Star = true },
        ];

        var rows = _plugins.Select(plugin =>
        {
            var m = plugin.Manifest;
            var state = PluginTrustPolicy.GetState(m, plugin.Dir);
            var notLoaded = state == PluginTrustPolicy.TrustState.Trusted && IsEnabledButNotLoaded(m.Id);

            return new PhTableRow
            {
                Key = m.Id,
                Cells =
                [
                    ToggleCell(plugin, state),
                    NameCell(plugin),
                    PhTableControl.TextCell(m.Kind.ToString()),
                    StatusCell(state, notLoaded, m.Id),
                ],
                Actions = BuildActions(plugin),
            };
        }).ToList();

        PART_Table.EmptyText = Core.Lang[LangId._Empty];
        PART_Table.Build(columns, rows);

        this.FindAncestorOfType<SettingsWindowView>()?.NotifyPluginsChanged();
    }

    private List<PhTableAction> BuildActions((PluginManifest Manifest, string Dir) plugin)
    {
        return
        [
            new()
            {
                Icon = ResxIconId.IconSettings,
                Tooltip = Core.Lang[LangId.Menu_MnuSettings],
                IsVisible = false,
            },
            new()
            {
                Icon = ResxIconId.IconEdit,
                Tooltip = Core.Lang[LangId._Edit],
                Click = () => _ = EditAndRefreshAsync(plugin),
            },
            new()
            {
                Icon = ResxIconId.IconClose,
                Tooltip = Core.Lang[LangId._Delete],
                Click = () => _ = DeletePluginAsync(plugin),
            },
        ];
    }

    private async Task EditAndRefreshAsync((PluginManifest Manifest, string Dir) plugin)
    {
        _ = await EditPluginAsync(plugin);
        RebuildTable();
    }

    private static async Task<bool> DisablePluginAsync((PluginManifest Manifest, string Dir) plugin)
    {
        if (!await PluginTrustPolicy.DisableAsync(plugin.Manifest.Id)) return false;

        Core.DisablePlugin(plugin.Manifest.Id);
        return true;
    }

    private static PhChip? StatusChip(PluginTrustPolicy.TrustState state, bool notLoaded)
    {
        var (key, variant) = state switch
        {
            PluginTrustPolicy.TrustState.Trusted when notLoaded
                => (LangId.Settings_Plugins_StatusNotLoaded, PhChipVariant.Warning),
            PluginTrustPolicy.TrustState.Trusted => (LangId.Settings_Plugins_StatusEnabled, PhChipVariant.Success),
            PluginTrustPolicy.TrustState.Changed => (LangId.Settings_Plugins_StatusChanged, PhChipVariant.Warning),
            PluginTrustPolicy.TrustState.Disabled => (LangId.Settings_Plugins_StatusDisabled, PhChipVariant.Neutral),
            PluginTrustPolicy.TrustState.Untrusted => (LangId.Settings_Plugins_StatusUntrusted, PhChipVariant.Neutral),
            _ => ((LangId?)null, PhChipVariant.Neutral),
        };

        if (key is null) return null;

        return new PhChip
        {
            Text = Core.Lang[key.Value],
            Variant = variant,
        };
    }

    private static Control StatusCell(PluginTrustPolicy.TrustState state, bool notLoaded, string pluginId)
    {
        if (StatusChip(state, notLoaded) is not { } chip) return PhTableControl.TextCell(string.Empty);

        if (notLoaded && LoadErrorOf(pluginId) is { } error) ToolTip.SetTip(chip, error);

        return PhTableControl.WrapCell(chip);
    }

    private static bool IsEnabledButNotLoaded(string pluginId)
        => Core.PluginDiscoveryTask.IsCompleted && !Core.PluginRegistry.IsLoaded(pluginId);

    private Border NameCell((PluginManifest Manifest, string Dir) plugin)
    {
        var m = plugin.Manifest;
        var nameText = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name;

        var name = new PhButton
        {
            Text = nameText,
            Variant = PhButtonVariant.Link,
            MaxWidth = NAME_MAX_WIDTH,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        ToolTip.SetTip(name, nameText);
        name.Click += (_, _) => _ = EditAndRefreshAsync(plugin);

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(name);

        if (!string.IsNullOrWhiteSpace(m.Version))
        {
            stack.Children.Add(new TextBlock
            {
                Text = m.Version,
                FontSize = Const.FONT_SIZE_SMALL,
                Opacity = 0.6,
            });
        }

        return PhTableControl.WrapCell(stack);
    }

}
