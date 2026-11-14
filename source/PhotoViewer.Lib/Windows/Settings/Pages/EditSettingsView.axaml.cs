using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class EditSettingsView : SettingsPageView
{
    private static readonly FontFamily _codeFont = new(Const.FONT_CODE);

    private readonly Dictionary<string, EditingApp> _apps = [];

    public EditSettingsView()
    {
        InitializeComponent();
    }

    public EditSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        BindToggle(PART_DeleteConfirmation, ConfigId.EnableDeleteConfirmation,
            LangId.Settings_EnableDeleteConfirmation, LangId.Settings_Edit_Saving, true);
        BindToggle(PART_SaveConfirmation, ConfigId.EnableSaveConfirmation,
            LangId.Settings_EnableSaveConfirmation, LangId.Settings_Edit_Saving, true);
        BindToggle(PART_PreserveModifiedDate, ConfigId.EnablePreserveModifiedDate,
            LangId.Settings_EnablePreserveModifiedDate, LangId.Settings_Edit_Saving);
        BindToggle(PART_OpenSaveAsInCurrentFolder, ConfigId.EnableOpenSaveAsInCurrentFolder,
            LangId.Settings_EnableOpenSaveAsInCurrentFolder, LangId.Settings_Edit_Saving, true);

        BindUIntSlider(PART_ImageEditQuality, ConfigId.ImageEditQuality,
            LangId.Settings_ImageEditQuality, LangId.Settings_Edit_Saving, 80u, PART_ImageEditQualityLabel);

        BindToggle(PART_CopyMultipleFiles, ConfigId.EnableCopyMultipleFiles,
            LangId.Settings_EnableCopyMultipleFiles, LangId.Settings_Clipboard, true);
        BindToggle(PART_CutMultipleFiles, ConfigId.EnableCutMultipleFiles,
            LangId.Settings_EnableCutMultipleFiles, LangId.Settings_Clipboard, true);

        BindEnumDropdown(PART_AfterEditingAction, ConfigId.AfterEditingAction, AfterEditAppAction.Nothing,
            LangId.Settings_AfterEditingAction, LangId.Settings_EditApps);

        BuildEditApps();
    }

    private void BuildEditApps()
    {
        var stored = VM.GetValue(ConfigId.EditApps, new Dictionary<string, EditingApp?>());
        foreach (var (ext, app) in stored)
        {
            if (app is not null) _apps[ext] = app;
        }

        SetLocalizedText(PART_AddApp, LangId._Add);
        PART_AddApp.Click += async (_, _) => await AddOrEditAppAsync(null);

        AddLangRefresher(RebuildAppsTable);

        RegisterSearchKey(PART_AddApp, LangId.Settings_EditApps, ConfigId.EditApps, LangId.Settings_EditApps);

        DisableIfLocked(ConfigId.EditApps, PART_AddApp, PART_AppsTable);
    }

    private void StageEditApps()
    {
        var value = _apps.ToDictionary(kv => kv.Key, kv => (EditingApp?)kv.Value);
        VM.SetValue(ConfigId.EditApps, value);
    }

    private async Task AddOrEditAppAsync(string? extKey)
    {
        var existing = extKey is not null ? _apps.GetValueOrDefault(extKey) : null;
        var window = new EditAppWindow(extKey, existing);

        if (await window.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (string.IsNullOrEmpty(window.ResultExtKey)) return;

        if (extKey is not null) _apps.Remove(extKey);
        _apps[window.ResultExtKey] = window.ResultApp;

        StageEditApps();
        RebuildAppsTable();
    }

    private void DeleteApp(string extKey)
    {
        if (!_apps.Remove(extKey)) return;

        StageEditApps();
        RebuildAppsTable();
    }

    private void RebuildAppsTable()
    {
        PhTableColumn[] columns =
        [
            new() { Header = Core.Lang[LangId._FileExtension] },
            new() { Header = Core.Lang[LangId.Settings_EditApps_AppName] },
            new() { Header = Core.Lang[LangId._Executable], Star = true },
            new() { Header = Core.Lang[LangId._Argument] },
        ];

        var extKeys = _apps.Keys
            .OrderBy(IsWildcardKey)
            .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<PhTableRow>(extKeys.Count);
        foreach (var extKey in extKeys)
        {
            var app = _apps[extKey];
            var key = extKey;
            rows.Add(new PhTableRow
            {
                Cells =
                [
                    PhTableControl.TextCell(extKey, maxWidth: 160, selectable: true, font: _codeFont),
                    AppNameCell(key, app),
                    PhTableControl.TextCell(app.Executable, selectable: true),
                    string.IsNullOrEmpty(app.Argument)
                        ? PhTableControl.TextCell(Core.Lang[LangId._Empty], muted: true)
                        : PhTableControl.TextCell(app.Argument, maxWidth: 180, selectable: true),
                ],
                Actions =
                [
                    new() { Icon = ResxIconId.IconEdit, Tooltip = Core.Lang[LangId._Edit], Click = () => _ = AddOrEditAppAsync(key) },
                    new() { Icon = ResxIconId.IconClose, Tooltip = Core.Lang[LangId._Delete], Click = () => DeleteApp(key) },
                ],
            });
        }

        PART_AppsTable.EmptyText = Core.Lang[LangId._Empty];
        PART_AppsTable.Build(columns, rows);
    }

    private Control AppNameCell(string extKey, EditingApp app)
    {
        var displayName = string.IsNullOrWhiteSpace(app.AppName) ? extKey : app.AppName;

        var btn = new PhButton
        {
            Variant = PhButtonVariant.Link,
            Text = displayName,
            MaxWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
            ClipToBounds = true,
        };
        ToolTip.SetTip(btn, displayName);
        btn.Click += (_, _) => _ = AddOrEditAppAsync(extKey);

        return PhTableControl.WrapCell(btn);
    }

    private static bool IsWildcardKey(string extKey) => extKey
        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Contains(EditingApp.ALL_EXTENSIONS);

}
