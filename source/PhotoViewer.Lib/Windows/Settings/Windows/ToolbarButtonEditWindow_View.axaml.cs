using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PhotoViewer.Common.Actions;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class ToolbarButtonEditWindowView : PhControl
{

    private sealed record IconOption(IgThemeIcon? Icon, string Display);

    private List<IconOption> _imageOptions = [];

    private ISet<string> _takenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ToolbarButtonEditWindowView()
    {
        InitializeComponent();

        PopulateImageOptions();
        PART_Image.SelectionChanged += (_, _) => UpdateCustomImageRow();
        PART_EnableConfigBinding.IsCheckedChanged += (_, _) => UpdateConfigBindingVisibility();
        PART_BrowseImage.Click += async (_, _) => await BrowseImageAsync();
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        ToolTip.SetTip(PART_BrowseImage, Core.Lang[LangId._Browse]);
    }

    public void LoadData(ToolbarItemModel? model, bool isReadOnly, ISet<string> takenIds)
    {
        _takenIds = takenIds;

        PART_Id.Text = model?.Id ?? string.Empty;
        PART_Text.Text = model?.Text ?? string.Empty;
        PART_ShowText.IsChecked = model?.ShowText ?? false;
        PART_AlignRight.IsChecked = model?.Alignment == ToolbarItemAlignment.Right;
        PART_ConfigBinding.Text = model?.ConfigBinding ?? string.Empty;
        PART_ConfigBindingValue.Text = model?.ConfigBindingValue ?? string.Empty;
        PART_EnableConfigBinding.IsChecked = !string.IsNullOrWhiteSpace(model?.ConfigBinding);
        PART_Action.LoadAction(model?.OnClick);

        SelectImage(model?.Image);
        UpdateConfigBindingVisibility();

        PART_Fields.IsEnabled = !isReadOnly;
        PART_ReadonlyNote.IsVisible = isReadOnly;

        DataValidationErrors.ClearErrors(PART_Id);
        DataValidationErrors.ClearErrors(PART_CustomImagePath);
    }

    public bool Validate()
    {
        var idOk = PART_Id.ValidateAndShowError();
        if (idOk)
        {
            var id = PART_Id.Text?.Trim() ?? string.Empty;
            if (_takenIds.Contains(id))
            {
                DataValidationErrors.SetError(PART_Id,
                    new ValidationException(Core.Lang[LangId.Settings_Toolbar_Errors_ButtonIdDuplicated, id]));
                idOk = false;
            }
        }

        var exeOk = PART_Action.ValidateExecutable();

        var imgOk = (PART_Image.SelectedItem as IconOption)?.Icon is not null
            || PART_CustomImagePath.ValidateAndShowError();

        return idOk & exeOk & imgOk;
    }

    public ToolbarItemModel BuildModel()
    {
        var selected = PART_Image.SelectedItem as IconOption;
        var image = selected?.Icon is { } icon
            ? icon.ToString()
            : PART_CustomImagePath.Text?.Trim() ?? string.Empty;

        var text = PART_Text.Text?.Trim() ?? string.Empty;

        var bindEnabled = PART_EnableConfigBinding.IsChecked == true;

        var action = new HotkeySingleAction
        {
            Executable = PART_Action.Executable?.Trim() ?? string.Empty,
            Argument = PART_Action.Argument?.Trim() ?? string.Empty,
            Hotkeys = [.. PART_Action.Hotkeys],
            LangKey = text,
        };

        return new ToolbarItemModel
        {
            Id = PART_Id.Text?.Trim() ?? string.Empty,
            Image = image,
            Text = text,
            ShowText = PART_ShowText.IsChecked == true,
            ConfigBinding = bindEnabled ? PART_ConfigBinding.Text?.Trim() ?? string.Empty : string.Empty,
            ConfigBindingValue = bindEnabled ? PART_ConfigBindingValue.Text?.Trim() ?? string.Empty : string.Empty,
            Alignment = PART_AlignRight.IsChecked == true
                ? ToolbarItemAlignment.Right
                : ToolbarItemAlignment.Left,
            OnClick = action,
        };
    }

    private void UpdateConfigBindingVisibility()
        => PART_ConfigBindingInputs.IsVisible = PART_EnableConfigBinding.IsChecked == true;

    private void PopulateImageOptions()
    {
        _imageOptions = [new IconOption(null, Core.Lang[LangId.Settings_Toolbar_CustomIcon])];
        foreach (var icon in Enum.GetValues<IgThemeIcon>())
        {
            if (icon == IgThemeIcon.AppLogo) continue;
            _imageOptions.Add(new IconOption(icon, icon.ToString()));
        }

        PART_Image.ItemsSource = _imageOptions;
        PART_Image.ItemTemplate = new FuncDataTemplate<IconOption>((opt, _) =>
            opt is null ? null : BuildIconOptionVisual(opt));
    }

    private void SelectImage(string? image)
    {
        if (!string.IsNullOrWhiteSpace(image)
            && Enum.TryParse<IgThemeIcon>(image, out var icon)
            && icon != IgThemeIcon.AppLogo
            && _imageOptions.FirstOrDefault(o => o.Icon == icon) is { } match)
        {
            PART_Image.SelectedItem = match;
            return;
        }

        PART_Image.SelectedIndex = 0;
        PART_CustomImagePath.Text = image ?? string.Empty;
    }

    private void UpdateCustomImageRow()
    {
        PART_CustomImageRow.IsVisible = (PART_Image.SelectedItem as IconOption)?.Icon is null;
    }

    private static StackPanel BuildIconOptionVisual(IconOption opt)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (opt.Icon is { } icon
            && new ToolbarItemModel { Image = icon.ToString() }.ImagePath is { Length: > 0 } path)
        {
            panel.Children.Add(new Avalonia.Svg.Skia.Svg(new Uri("file:///"))
            {
                Width = 18,
                Height = 18,
                Path = path,
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = opt.Display,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return panel;
    }

    private async Task BrowseImageAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("SVG") { Patterns = ["*.svg"] }],
        });

        var path = (files.Count > 0 ? files[0] : null)?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        PART_CustomImagePath.Text = path;
    }

}
