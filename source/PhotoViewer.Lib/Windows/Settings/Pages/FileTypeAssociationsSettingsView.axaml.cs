using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class FileTypeAssociationsSettingsView : SettingsPageView
{

    private const double MIN_TABLE_HEIGHT = 220;
    private const double BOTTOM_GAP = 40;

    private static readonly FontFamily _codeFont = new(Const.FONT_CODE);

    private ScrollViewer? _pageScroll;

    private readonly HashSet<string> _exts = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _pluginExts = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _pluginEncodeExts = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<CodecInfo> _codecs = [];

    private readonly Dictionary<string, CodecInfo?> _decoderCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CodecInfo?> _encoderCache = new(StringComparer.OrdinalIgnoreCase);

    private string _filter = string.Empty;

    public FileTypeAssociationsSettingsView()
    {
        InitializeComponent();
    }

    public FileTypeAssociationsSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }

    protected override void Build()
    {
        BuildExtensionIcons();
        BuildDefaultPhotoViewer();
        BuildAppMenuEntry();
        BuildFileFormats();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _pageScroll = this.FindAncestorOfType<ScrollViewer>();
        if (_pageScroll is not null) _pageScroll.PropertyChanged += PageScroll_PropertyChanged;

        Dispatcher.UIThread.Post(UpdateTableMaxHeight, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_pageScroll is not null) _pageScroll.PropertyChanged -= PageScroll_PropertyChanged;
        _pageScroll = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void PageScroll_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty) UpdateTableMaxHeight();
    }

    private void UpdateTableMaxHeight()
    {
        if (_pageScroll is null) return;

        var viewport = _pageScroll.Bounds.Height;
        if (viewport <= 0) return;

        if (PART_Table.TranslatePoint(new Point(0, 0), _pageScroll) is not { } pt) return;

        var tableTop = pt.Y + _pageScroll.Offset.Y;
        PART_Table.MaxHeight = Math.Max(MIN_TABLE_HEIGHT, viewport - tableTop - BOTTOM_GAP);
    }

    private void BuildExtensionIcons()
    {
        if (Core.ShellProvider?.IsDefaultViewerConfigurable == false)
        {
            PART_ExtIconsHeading.IsVisible = false;
            PART_ExtIconsGroup.IsVisible = false;
            return;
        }

        SetLocalizedText(PART_OpenExtIconFolder, LangId.Settings_OpenExtensionIconFolder);
        PART_OpenExtIconFolder.Click += (_, _) =>
            BHelper.OpenFolderPath(BHelper.GetRealPlatformConfigDir(Dir.ExtIcons));

        AddLangRefresher(() => PART_ExtIconsDesc.LangParams = Core.Lang[LangId._Register]);

        RegisterSearchKey(PART_OpenExtIconFolder, LangId.Settings_FileExtensionIcons, null,
            LangId.Settings_FileExtensionIcons);
    }

    private void BuildDefaultPhotoViewer()
    {
        if (Core.ShellProvider?.IsDefaultViewerConfigurable == false)
        {
            PART_DefaultViewerHeading.IsVisible = false;
            PART_DefaultViewerGroup.IsVisible = false;
            return;
        }

        var scope = Core.ShellProvider?.GetDefaultViewerScope() ?? DefaultAppScope.CurrentUser;

        var appQueryKey = scope == DefaultAppScope.LocalMachine ? "registeredAppMachine" : "registeredAppUser";
        var defaultAppsUri = $"ms-settings:defaultapps?{appQueryKey}={Uri.EscapeDataString(BHelper.AppName)}";

        SetLocalizedText(PART_Register, LangId._Register);
        AddLangRefresher(() => ToolTip.SetTip(PART_Register, Core.Lang[LangId.Settings_UnmanagedSettingReminder]));
        PART_Register.Click += async (_, _) => await AppAPIProvider.IG_SetDefaultPhotoViewerAsync();

        SetLocalizedText(PART_Unregister, LangId._Unregister);
        PART_Unregister.Click += async (_, _) => await AppAPIProvider.IG_RemoveDefaultPhotoViewerAsync();

        AddLangRefresher(() => PART_ScopeInfo.Text = Core.Lang[scope == DefaultAppScope.LocalMachine
            ? LangId.Settings_DefaultPhotoViewer_ScopePerMachine
            : LangId.Settings_DefaultPhotoViewer_ScopePerUser]);

        SetLocalizedText(PART_OpenDefaultApps, LangId.Settings_OpenDefaultAppsSetting);
        PART_OpenDefaultApps.Click += async (_, _) => await BHelper.OpenSystemSettingsAsync(this, defaultAppsUri);

        RegisterSearchKey(PART_Register, LangId.Settings_DefaultPhotoViewer, null, LangId.Settings_DefaultPhotoViewer);
    }

    private void BuildAppMenuEntry()
    {
        if (Core.ShellProvider?.CanRegisterAppMenuEntry != true) return;

        PART_AppMenuHeading.IsVisible = true;
        PART_AppMenuGroup.IsVisible = true;

        SetLocalizedText(PART_RegisterAppMenu, LangId._Register);
        AddLangRefresher(() => ToolTip.SetTip(PART_RegisterAppMenu, Core.Lang[LangId.Settings_UnmanagedSettingReminder]));
        PART_RegisterAppMenu.Click += async (_, _) => await AppAPIProvider.RegisterAppMenuEntryAsync(true);

        SetLocalizedText(PART_UnregisterAppMenu, LangId._Unregister);
        PART_UnregisterAppMenu.Click += async (_, _) => await AppAPIProvider.RegisterAppMenuEntryAsync(false);

        RegisterSearchKey(PART_RegisterAppMenu, LangId.Settings_AppMenuEntry, null, LangId.Settings_AppMenuEntry);
    }

    private void BuildFileFormats()
    {
        SnapshotCodecs();

        var stored = VM.GetValue(ConfigId.FileFormats,
            new HashSet<string>(Config.DefaultFileFormats, StringComparer.OrdinalIgnoreCase));
        foreach (var ext in stored) _exts.Add(ext);

        PART_Table.MinHeight = MIN_TABLE_HEIGHT;

        SetLocalizedText(PART_AddFormat, LangId._Add);
        PART_AddFormat.Click += async (_, _) => await AddExtensionAsync();

        SetLocalizedText(PART_ResetFormats, LangId._ResetToDefault);
        PART_ResetFormats.Click += (_, _) => ResetFormats();

        AddLangRefresher(() => PART_Search.PlaceholderText = Core.Lang[LangId._TypeToFilter]);
        PART_Search.TextChanged += (_, _) =>
        {
            _filter = PART_Search.Text ?? string.Empty;
            RebuildTable();
        };

        AddLangRefresher(RebuildTable);

        RegisterSearchKey(PART_AddFormat, LangId.Settings_FileFormats, ConfigId.FileFormats,
            LangId.Settings_FileFormats);

        DisableIfLocked(ConfigId.FileFormats, PART_AddFormat, PART_ResetFormats, PART_Table);
    }

    private void StageFormats() => VM.SetValue(ConfigId.FileFormats, new HashSet<string>(_exts, StringComparer.OrdinalIgnoreCase));

    private async Task AddExtensionAsync()
    {
        var win = TopLevel.GetTopLevel(this) as PhWindow;
        var result = await ModalWindow.ShowInputAsync(win, new ModalWindowOptions
        {
            Title = Core.Lang[LangId.Settings_AddNewFileExtension],
            Description = Core.Lang[LangId._FileExtension],
            InputPlaceholder = ".jpg",
            AcceptValue = TextBoxAcceptValue.FileExtensionValueOnly,
        });
        if (result.ExitCode != DialogExitCode.OK) return;

        var raw = result.InputValue?.Trim().ToLowerInvariant() ?? string.Empty;
        if (raw.Length == 0) return;
        var ext = raw.StartsWith('.') ? raw : "." + raw;

        if (_exts.Add(ext))
        {
            StageFormats();
            RebuildTable();
        }

        PART_Table.FlashRow(ext);
    }

    private void DeleteExtension(string ext)
    {
        if (!_exts.Remove(ext)) return;

        StageFormats();
        RebuildTable();
    }

    private void ResetFormats()
    {
        _exts.Clear();
        foreach (var ext in Config.DefaultFileFormats) _exts.Add(ext);

        StageFormats();
        RebuildTable();
    }

    private void RebuildTable()
    {
        PhTableColumn[] columns =
        [
            new() { Header = string.Empty },
            new() { Header = Core.Lang[LangId._FileExtension], MinWidth = 160 },
            new() { Header = Core.Lang[LangId._Decoder], MinWidth = 150 },
            new() { Header = Core.Lang[LangId._Encoder], Star = true, MinWidth = 150 },
        ];

        var browsable = new HashSet<string>(_exts, StringComparer.OrdinalIgnoreCase);
        foreach (var ext in _pluginExts) browsable.Add(ext);

        var all = new HashSet<string>(browsable, StringComparer.OrdinalIgnoreCase);
        foreach (var ext in _pluginEncodeExts) all.Add(ext);

        var q = _filter.Trim();
        var sorted = all
            .Where(e => q.Length == 0
                || e.Contains(q, StringComparison.OrdinalIgnoreCase)
                || DecodeCodecNameFor(e).Contains(q, StringComparison.OrdinalIgnoreCase)
                || EncodeCodecNameFor(e).Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<PhTableRow>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var ext = sorted[i];
            var key = ext;

            PhTableAction[] actions = _exts.Contains(ext) ? [
                new() {
                    Icon = ResxIconId.IconClose,
                    Tooltip = Core.Lang[LangId._Delete],
                    Click = () => DeleteExtension(key),
                }
            ] : [];

            rows.Add(new PhTableRow
            {
                Key = ext,
                Cells =
                [
                    PhTableControl.TextCell((i + 1).ToString()),
                    PhTableControl.TextCell(ext, selectable: true, font: _codeFont),
                    CodecCell(DecodeCodecFor(ext)),
                    CodecCell(EncodeCodecFor(ext)),
                ],
                Actions = actions,
            });
        }

        PART_Table.EmptyText = "–";
        PART_Table.Build(columns, rows);

        PART_TotalFormats.LangParams = browsable.Count;
    }

    private Control CodecCell(CodecInfo? codec)
    {
        var name = codec?.CodecName ?? string.Empty;
        if (name.Length == 0)
        {
            return PhTableControl.TextCell("–", muted: true);
        }

        var pluginId = codec?.PluginId;
        if (string.IsNullOrEmpty(pluginId)) return PhTableControl.TextCell(name);

        var link = new PhButton
        {
            Text = name,
            Variant = PhButtonVariant.Link,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        ToolTip.SetTip(link, name);
        link.Click += (_, _) => OpenPluginSettings(pluginId);

        return PhTableControl.WrapCell(link);
    }

    private void OpenPluginSettings(string pluginId)
        => this.FindAncestorOfType<SettingsWindowView>()?.NavigateToPlugin(pluginId);

    private void SnapshotCodecs()
    {
        _codecs = Core.CodecRegistry.GetCodecInfos();
        _pluginExts.Clear();
        _pluginEncodeExts.Clear();
        _decoderCache.Clear();
        _encoderCache.Clear();

        foreach (var codec in _codecs)
        {
            if (!codec.IsPlugin) continue;
            foreach (var ext in codec.DecodingExtensions) _pluginExts.Add(ext);
            foreach (var ext in codec.EncodingExtensions) _pluginEncodeExts.Add(ext);
        }
    }

    public void RefreshCodecFormats()
    {
        SnapshotCodecs();
        RebuildTable();
    }

    private CodecInfo? DecodeCodecFor(string ext)
    {
        if (_decoderCache.TryGetValue(ext, out var cached)) return cached;

        CodecInfo? found = null;

        foreach (var codec in _codecs)
        {
            if (codec.DecodingExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                found = codec;
                break;
            }
        }

        found ??= _codecs.FirstOrDefault(c => c.IsFallback);

        _decoderCache[ext] = found;
        return found;
    }

    private CodecInfo? EncodeCodecFor(string ext)
    {
        if (_encoderCache.TryGetValue(ext, out var cached)) return cached;

        var found = Core.CodecRegistry.GetEncodeCodecInfo(ext);
        _encoderCache[ext] = found;
        return found;
    }

    private string DecodeCodecNameFor(string ext) => DecodeCodecFor(ext)?.CodecName ?? string.Empty;

    private string EncodeCodecNameFor(string ext) => EncodeCodecFor(ext)?.CodecName ?? string.Empty;

}
