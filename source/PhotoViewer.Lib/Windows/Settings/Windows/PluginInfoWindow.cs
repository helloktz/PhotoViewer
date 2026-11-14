using Avalonia;
using Avalonia.Layout;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using ImageGlass.SDK.Plugins;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

internal enum PluginInfoWindowMode
{

    View,

    Enable,

    Configure,
}

internal sealed class PluginInfoWindow : DialogWindow
{
    private readonly PluginInfoWindowView _view;
    private readonly PluginManifest _manifest;
    private readonly string _pluginDir;
    private readonly bool _showFormats;
    private readonly Func<Task<bool>>? _enableAsync;
    private readonly PhButton _deleteButton;
    private readonly string _pluginName;
    private PluginInfoWindowMode _mode;
    private bool _isEnabling;

    protected override int MIN_WIDTH => 500;
    protected override int MAX_WIDTH => 500;
    protected override Thickness ContentPadding => new(0);

    public bool DeleteRequested { get; private set; }

    public IReadOnlyCollection<string> DisabledDecodeExtensions => _view.DisabledDecodeExtensions;

    public IReadOnlyCollection<string> DisabledEncodeExtensions => _view.DisabledEncodeExtensions;

    public bool ChoicesChanged => _view.ChoicesChanged;

    public bool PluginEnabled { get; private set; }

    public PluginInfoWindow(PluginManifest manifest, string pluginDir,
        PluginInfoWindowMode mode = PluginInfoWindowMode.View, bool hashChanged = false,
        Func<Task<bool>>? enableAsync = null)
    {
        _mode = mode;
        _manifest = manifest;
        _pluginDir = pluginDir;
        _enableAsync = enableAsync;
        _pluginName = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name;

        _showFormats = manifest.Kind == IGPluginKind.Codec;

        if (mode is PluginInfoWindowMode.Enable or PluginInfoWindowMode.Configure)
        {
            IsButton1Visible = true;
            IsButton2Visible = true;
            IsButton3Visible = false;

            var cta = mode == PluginInfoWindowMode.Configure ? DialogButton.Button1 : DialogButton.Button2;
            DefaultButton = cta;
            DefaultFocus = cta == DialogButton.Button1 ? DialogFocus.Button1 : DialogFocus.Button2;
        }
        else
        {
            IsButton1Visible = true;
            IsButton2Visible = false;
            IsButton3Visible = false;
            DefaultButton = DialogButton.Button1;
            DefaultFocus = DialogFocus.Button1;
        }

        _view = new PluginInfoWindowView();
        _view.LoadData(manifest, pluginDir,
            allowEdit: mode == PluginInfoWindowMode.Configure,
            showFormats: _showFormats);
        if (mode == PluginInfoWindowMode.Enable)
        {
            Button1Icon = Resx.GetIcon(ResxIconId.IconVerify);
            _btn1.IconStrokeThickness = 1.5;
            _view.ShowConsentWarning(manifest, hashChanged);
        }
        DialogContent = _view;

        _deleteButton = NewFooterLink(() => DeleteRequested = true);
        DialogFooterLeftContent = _deleteButton;
    }

    private PhButton NewFooterLink(Action flag)
    {
        var button = new PhButton
        {
            Variant = PhButtonVariant.Link,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) =>
        {
            flag();
            OnDialogCancelled(new DialogEventArgs(DialogAction.Cancel));
        };
        return button;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = _pluginName;
        _deleteButton.Text = Core.Lang[LangId._Delete];

        UpdateButtonTexts();
    }

    protected override async void OnDialogSubmitted(DialogEventArgs e)
    {
        if (_mode != PluginInfoWindowMode.Enable || _enableAsync is null)
        {
            base.OnDialogSubmitted(e);
            return;
        }
        if (!e.CanProceed || _isEnabling) return;

        SetEnabling(true);
        try
        {
            PluginEnabled = await _enableAsync();
        }
        finally
        {
            SetEnabling(false);
        }

        if (DialogResult != DialogExitCode.None) return;

        if (!_showFormats || !Core.PluginRegistry.IsLoaded(_manifest.Id))
        {
            base.OnDialogSubmitted(e);
            return;
        }

        SwitchToConfigure();
    }

    protected override void OnDialogAborted()
    {
        if (_isEnabling) return;

        base.OnDialogAborted();
    }

    private void SetEnabling(bool isEnabling)
    {
        _isEnabling = isEnabling;
        _btn1.IsEnabled = !isEnabling;
        _btn2.IsEnabled = !isEnabling;
        _deleteButton.IsEnabled = !isEnabling;
    }

    private void SwitchToConfigure()
    {
        _mode = PluginInfoWindowMode.Configure;

        _view.LoadData(_manifest, _pluginDir, allowEdit: true, showFormats: _showFormats);
        _view.ShowFormatsAfterEnable();

        _btn2.IsDefault = false;
        _btn2.Variant = PhButtonVariant.Default;
        Button1Icon = null;
        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Button1;
        SetDefaultButton(DefaultButton);
        SetDefaultFocus();

        UpdateButtonTexts();
    }

    private void UpdateButtonTexts()
    {
        switch (_mode)
        {
            case PluginInfoWindowMode.Enable:
                Button1Text = Core.Lang[LangId.Settings_Plugins_TrustAndEnable];
                Button2Text = Core.Lang[LangId._Cancel];
                break;

            case PluginInfoWindowMode.Configure:
                Button1Text = Core.Lang[LangId._Save];
                Button2Text = Core.Lang[LangId._Cancel];
                break;

            default:
                Button1Text = Core.Lang[LangId._OK];
                break;
        }
    }

}
