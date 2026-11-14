using Avalonia.Controls;
using Avalonia.Media;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.Plugins;
using ImageGlass.SDK.Plugins;
using PhotoViewer.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoViewer.Common.Windows;

public partial class PluginInfoWindowView : PhControl
{
    private static readonly FontFamily _codeFont = new(Const.FONT_CODE);

    private string _pluginDir = string.Empty;
    private string _pluginId = string.Empty;

    private string[] _declaredDecode = [];
    private string[] _declaredEncode = [];
    private readonly HashSet<string> _disabledDecode = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabledEncode = new(StringComparer.OrdinalIgnoreCase);

    private readonly DirectionColumn _decodeColumn;
    private readonly DirectionColumn _encodeColumn;

    private bool _isEditable;
    private bool _isLoaded;

    public IReadOnlyCollection<string> DisabledDecodeExtensions => _disabledDecode;

    public IReadOnlyCollection<string> DisabledEncodeExtensions => _disabledEncode;

    public bool ChoicesChanged { get; private set; }

    public PluginInfoWindowView()
    {
        InitializeComponent();

        _decodeColumn = new DirectionColumn(_disabledDecode);
        _encodeColumn = new DirectionColumn(_disabledEncode);

        PART_OpenFolder.Click += (_, _) => BHelper.OpenFolderPath(_pluginDir);
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        PART_OpenFolder.Text = Core.Lang[LangId.Settings_Plugins_OpenPluginFolder];
        PART_TabInfo.Header = Core.Lang[LangId.Settings_Plugins_ViewMetadata];
        PART_TabFormats.Header = Core.Lang[LangId.Settings_FileFormats];

        if (_pluginId.Length > 0) RebuildExtensionsTable();
    }

    private void SetFormatsTabVisible(bool visible)
    {
        PART_TabFormats.IsVisible = visible;

        if (visible) PART_Tabs.Classes.Remove("singleTab");
        else PART_Tabs.Classes.Add("singleTab");

        if (!visible) PART_Tabs.SelectedItem = PART_TabInfo;
    }

    public void LoadData(PluginManifest manifest, string pluginDir, bool allowEdit = false,
        bool showFormats = true)
    {
        _pluginDir = pluginDir;
        _pluginId = manifest.Id;

        _isEditable = allowEdit && !Config.IsConfigLocked(ConfigId.PluginTrust);

        SetField(PART_IdRow, PART_Id, manifest.Id);
        SetField(PART_NameRow, PART_Name, manifest.Name);
        SetField(PART_VersionRow, PART_Version, manifest.Version);
        SetField(PART_TypeRow, PART_Type, manifest.Kind.ToString());
        SetField(PART_DescriptionRow, PART_Description, manifest.Description);
        SetField(PART_ExecutableRow, PART_Executable, manifest.Executable);

        _isLoaded = Core.PluginRegistry.IsLoaded(manifest.Id);
        (_declaredDecode, _declaredEncode) = Core.PluginRegistry.GetDeclaredCodecExtensions(manifest.Id);

        var (disabledDecode, disabledEncode) = PluginTrustPolicy.GetExtensionExclusions(manifest.Id);
        _disabledDecode.Clear();
        _disabledEncode.Clear();
        foreach (var ext in disabledDecode) _disabledDecode.Add(ext);
        foreach (var ext in disabledEncode) _disabledEncode.Add(ext);
        ChoicesChanged = false;

        SetFormatsTabVisible(showFormats);
        RebuildExtensionsTable();

        PART_Folder.Text = pluginDir;
        ToolTip.SetTip(PART_Folder, pluginDir);
    }

    public void ShowConsentWarning(PluginManifest manifest, bool hashChanged)
    {
        var name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name;
        var msg = Core.Lang[LangId.Settings_Plugins_TrustPrompt, name];
        if (hashChanged)
        {
            msg = Core.Lang[LangId.Settings_Plugins_TrustChangedWarning] + "\n\n" + msg;
        }

        PART_ConsentTitle.Text = Core.Lang[LangId.Settings_Plugins_TrustTitle];
        PART_ConsentMessage.Text = msg;
        PART_ConsentRow.IsVisible = true;
    }

    public void ShowFormatsAfterEnable()
    {
        PART_ConsentRow.IsVisible = false;
        if (PART_TabFormats.IsVisible) PART_Tabs.SelectedItem = PART_TabFormats;
    }

    private void RebuildExtensionsTable()
    {
        var rowExts = new List<string>(_declaredDecode);
        foreach (var ext in _declaredEncode)
        {
            if (!rowExts.Contains(ext, StringComparer.OrdinalIgnoreCase)) rowExts.Add(ext);
        }
        rowExts.Sort(StringComparer.OrdinalIgnoreCase);

        PART_ExtensionsTable.IsVisible = rowExts.Count > 0;
        PART_ExtensionsHint.Text = rowExts.Count > 0
            ? (_isEditable ? Core.Lang[LangId.Settings_Plugins_ExtensionsHint] : string.Empty)
            : Core.Lang[_isLoaded ? LangId.Settings_Plugins_NoFormats : LangId.Settings_Plugins_FormatsAfterEnable];
        PART_ExtensionsHint.IsVisible = !string.IsNullOrEmpty(PART_ExtensionsHint.Text);

        _decodeColumn.Reset();
        _encodeColumn.Reset();

        if (rowExts.Count == 0) return;

        PhTableColumn[] columns =
        [
            new() { Header = Core.Lang[LangId._FileExtension], Star = true, MinWidth = 140 },
            new() { HeaderContent = DirectionHeader(Core.Lang[LangId._Decoder], _decodeColumn) },
            new() { HeaderContent = DirectionHeader(Core.Lang[LangId._Encoder], _encodeColumn) },
        ];

        var rows = rowExts.Select(ext => new PhTableRow
        {
            Key = ext,
            Cells =
            [
                PhTableControl.TextCell(ext, selectable: true, font: _codeFont),
                DirectionCell(ext, _declaredDecode, _decodeColumn),
                DirectionCell(ext, _declaredEncode, _encodeColumn),
            ],
        }).ToList();

        PART_ExtensionsTable.EmptyText = Core.Lang[LangId._Empty];
        PART_ExtensionsTable.Build(columns, rows);

        SyncDirectionHeader(_decodeColumn);
        SyncDirectionHeader(_encodeColumn);
    }

    private CheckBox DirectionHeader(string text, DirectionColumn column)
    {
        var check = new SelectAllCheckBox
        {
            Content = text,
            FontWeight = FontWeight.SemiBold,
            IsThreeState = true,
            MinWidth = 0,
        };
        column.Header = check;

        check.Click += (_, _) => SetDirection(column, check.IsChecked != true);

        return check;
    }

    private Control DirectionCell(string ext, string[] declared, DirectionColumn column)
    {
        if (!declared.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            return PhTableControl.TextCell("-", muted: true);
        }

        var check = new CheckBox
        {
            IsChecked = !column.Disabled.Contains(ext),
            IsEnabled = _isEditable,
        };
        column.Rows.Add((ext, check));

        check.Click += (_, _) =>
        {
            column.SetExcluded(ext, check.IsChecked != true);
            ChoicesChanged = true;

            SyncDirectionHeader(column);
        };

        return PhTableControl.WrapCell(check);
    }

    private void SetDirection(DirectionColumn column, bool isChecked)
    {
        if (!_isEditable || column.Rows.Count == 0) return;

        foreach (var (ext, check) in column.Rows)
        {
            check.IsChecked = isChecked;
            column.SetExcluded(ext, !isChecked);
        }

        ChoicesChanged = true;
        SyncDirectionHeader(column);
    }

    private void SyncDirectionHeader(DirectionColumn column)
    {
        if (column.Header is not { } header) return;

        var total = column.Rows.Count;
        var ticked = column.Rows.Count(r => r.Check.IsChecked == true);

        header.IsChecked = ticked == 0 ? false : ticked == total ? true : null;
        header.IsEnabled = _isEditable && total > 0;
    }

    private static void SetField(Control row, SelectableTextBlock value, string? text)
    {
        value.Text = text ?? string.Empty;
        row.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private sealed class DirectionColumn(HashSet<string> disabled)
    {

        public HashSet<string> Disabled { get; } = disabled;

        public List<(string Ext, CheckBox Check)> Rows { get; } = [];

        public CheckBox? Header { get; set; }

        public void SetExcluded(string ext, bool excluded)
        {
            if (excluded) Disabled.Add(ext);
            else Disabled.Remove(ext);
        }

        public void Reset()
        {
            Rows.Clear();
            Header = null;
        }
    }

    private sealed class SelectAllCheckBox : CheckBox
    {
        protected override Type StyleKeyOverride => typeof(CheckBox);

        protected override void Toggle() { }
    }

}
