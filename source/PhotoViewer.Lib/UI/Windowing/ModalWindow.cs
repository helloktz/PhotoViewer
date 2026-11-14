using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace PhotoViewer.UI.Windowing;

public partial class ModalWindow : DialogWindow
{
    private readonly double THUMBNAIL_SIZE = 80;

    protected PhTextBox _txtInput;
    protected Image _thumbnailIconImage;
    protected Border _noteContainer;
    protected CheckBox _chkRememberOption;

    public object? ModalExtraContent
    {
        get => GetValue(ModalExtraContentProperty);
        set => SetValue(ModalExtraContentProperty, value);
    }
    public static readonly StyledProperty<object?> ModalExtraContentProperty =
        AvaloniaProperty.Register<ModalWindow, object?>(nameof(ModalExtraContent));

    public string? Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }
    public static readonly StyledProperty<string?> HeadingProperty =
        AvaloniaProperty.Register<ModalWindow, string?>(nameof(Heading));

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<ModalWindow, string?>(nameof(Description));

    public string? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }
    public static readonly StyledProperty<string?> DetailsProperty =
        AvaloniaProperty.Register<ModalWindow, string?>(nameof(Details));

    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }
    public static readonly StyledProperty<string?> NoteProperty =
        AvaloniaProperty.Register<ModalWindow, string?>(nameof(Note));

    public InfoBarSeverity NoteStyle
    {
        get => GetValue(NoteStyleProperty);
        set
        {
            SetValue(NoteStyleProperty, value);
            BindNoteBackground();
        }
    }
    public static readonly StyledProperty<InfoBarSeverity> NoteStyleProperty =
        AvaloniaProperty.Register<ModalWindow, InfoBarSeverity>(nameof(NoteStyle), InfoBarSeverity.Info);

    public IImage? Thumbnail
    {
        get => GetValue(ThumbnailProperty);
        set => SetValue(ThumbnailProperty, value);
    }
    public static readonly StyledProperty<IImage?> ThumbnailProperty =
        AvaloniaProperty.Register<ModalWindow, IImage?>(nameof(Thumbnail));

    public StockIconId? ThumbnailIcon
    {
        get => GetValue(ThumbnailIconProperty);
        set => SetValue(ThumbnailIconProperty, value);
    }
    public static readonly StyledProperty<StockIconId?> ThumbnailIconProperty =
        AvaloniaProperty.Register<ModalWindow, StockIconId?>(nameof(ThumbnailIcon));

    public string InputValue
    {
        get => _txtInput?.GetValue(TextBox.TextProperty) ?? string.Empty;
        set => _txtInput?.SetValue(TextBox.TextProperty, value);
    }
    public static readonly StyledProperty<string> InputValueProperty =
        AvaloniaProperty.Register<ModalWindow, string>(nameof(InputValue), string.Empty);

    public TextBoxAcceptValue AcceptValue
    {
        get => GetValue(AcceptValueProperty);
        set => SetValue(AcceptValueProperty, value);
    }
    public static readonly StyledProperty<TextBoxAcceptValue> AcceptValueProperty =
        AvaloniaProperty.Register<ModalWindow, TextBoxAcceptValue>(nameof(AcceptValue), TextBoxAcceptValue.Any);

    public string? InputPlaceholder
    {
        get => GetValue(InputPlaceholderProperty);
        set => SetValue(InputPlaceholderProperty, value);
    }
    public static readonly StyledProperty<string?> InputPlaceholderProperty =
        AvaloniaProperty.Register<ModalWindow, string?>(nameof(InputPlaceholder));

    public double ProgressValue
    {
        get => GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }
    public static readonly StyledProperty<double> ProgressValueProperty =
        AvaloniaProperty.Register<ModalWindow, double>(nameof(ProgressValue));

    public bool IsProgressVisible
    {
        get => GetValue(IsProgressVisibleProperty);
        set => SetValue(IsProgressVisibleProperty, value);
    }
    public static readonly StyledProperty<bool> IsProgressVisibleProperty =
        AvaloniaProperty.Register<ModalWindow, bool>(nameof(IsProgressVisible));

    public bool IsProgressIndeterminate
    {
        get => GetValue(IsProgressIndeterminateProperty);
        set => SetValue(IsProgressIndeterminateProperty, value);
    }
    public static readonly StyledProperty<bool> IsProgressIndeterminateProperty =
        AvaloniaProperty.Register<ModalWindow, bool>(nameof(IsProgressIndeterminate));

    public bool IsHeadingVisible => !string.IsNullOrWhiteSpace(Heading);
    public static readonly DirectProperty<ModalWindow, bool> IsHeadingVisibleProperty =
        AvaloniaProperty.RegisterDirect<ModalWindow, bool>(nameof(IsHeadingVisible), i => i.IsHeadingVisible);

    public bool IsDescriptionVisible => !string.IsNullOrWhiteSpace(Description);
    public static readonly DirectProperty<ModalWindow, bool> IsDescriptionVisibleProperty =
        AvaloniaProperty.RegisterDirect<ModalWindow, bool>(nameof(IsDescriptionVisible), i => i.IsDescriptionVisible);

    public bool IsDetailsVisible => !string.IsNullOrWhiteSpace(Details);
    public static readonly DirectProperty<ModalWindow, bool> IsDetailsVisibleProperty =
        AvaloniaProperty.RegisterDirect<ModalWindow, bool>(nameof(IsDetailsVisible), i => i.IsDetailsVisible);

    public bool IsThumbnailVisible => Thumbnail is not null;
    public static readonly DirectProperty<ModalWindow, bool> IsThumbnailVisibleProperty =
        AvaloniaProperty.RegisterDirect<ModalWindow, bool>(nameof(IsThumbnailVisible), i => i.IsThumbnailVisible);

    public bool IsThumbnailIconVisible => ThumbnailIcon is not null;
    public static readonly DirectProperty<ModalWindow, bool> IsThumbnailIconVisibleProperty =
        AvaloniaProperty.RegisterDirect<ModalWindow, bool>(nameof(IsThumbnailIconVisible), i => i.IsThumbnailIconVisible);

    public bool IsThumbnailSectionVisible => Thumbnail is not null || ThumbnailIcon is not null;
    public static readonly DirectProperty<ModalWindow, bool> IsThumbnailSectionVisibleProperty =
        AvaloniaProperty.RegisterDirect<ModalWindow, bool>(nameof(IsThumbnailSectionVisible), i => i.IsThumbnailSectionVisible);

    public bool IsNoteVisible => !string.IsNullOrWhiteSpace(Note);
    public static readonly DirectProperty<ModalWindow, bool> IsNoteVisibleProperty =
        AvaloniaProperty.RegisterDirect<ModalWindow, bool>(nameof(IsNoteVisible), i => i.IsNoteVisible);

    public bool IsInputVisible
    {
        get => GetValue(IsInputVisibleProperty);
        set => SetValue(IsInputVisibleProperty, value);
    }
    public static readonly StyledProperty<bool> IsInputVisibleProperty =
        AvaloniaProperty.Register<ModalWindow, bool>(nameof(IsInputVisible));

    public bool IsRememberOptionVisible
    {
        get => GetValue(IsRememberOptionVisibleProperty);
        set => SetValue(IsRememberOptionVisibleProperty, value);
    }
    public static readonly StyledProperty<bool> IsRememberOptionVisibleProperty =
        AvaloniaProperty.Register<ModalWindow, bool>(nameof(IsRememberOptionVisible));

    public string RememberOptionText
    {
        get => GetValue(RememberOptionTextProperty);
        set => SetValue(RememberOptionTextProperty, value);
    }
    public static readonly StyledProperty<string> RememberOptionTextProperty =
        AvaloniaProperty.Register<ModalWindow, string>(nameof(RememberOptionText), "[Don't show this message again]");

    public bool IsRememberOptionChecked => _chkRememberOption?.IsChecked ?? false;

    public ModalWindow()
    {
        DialogContent = CreateDialogContentElement();
        DialogFooterLeftContent = CreateDialogFooterLeftContentElement();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        LoadThumbnailIconSource();

        if (IsInputVisible)
        {
            _txtInput.Focus(Avalonia.Input.NavigationMethod.Tab);
            _txtInput.SelectAll();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == ThumbnailProperty)
        {
            RaisePropertyChanged(IsThumbnailVisibleProperty, default, IsThumbnailVisible);
            RaisePropertyChanged(IsThumbnailSectionVisibleProperty, default, IsThumbnailSectionVisible);
        }
        else if (e.Property == ThumbnailIconProperty)
        {
            RaisePropertyChanged(IsThumbnailIconVisibleProperty, default, IsThumbnailIconVisible);
            RaisePropertyChanged(IsThumbnailSectionVisibleProperty, default, IsThumbnailSectionVisible);
        }
        else if (e.Property == NoteProperty)
        {
            RaisePropertyChanged(IsNoteVisibleProperty, default, IsNoteVisible);
        }
        else if (e.Property == HeadingProperty)
        {
            RaisePropertyChanged(IsHeadingVisibleProperty, default, IsHeadingVisible);
        }
        else if (e.Property == DescriptionProperty)
        {
            RaisePropertyChanged(IsDescriptionVisibleProperty, default, IsDescriptionVisible);
        }
        else if (e.Property == DetailsProperty)
        {
            RaisePropertyChanged(IsDetailsVisibleProperty, default, IsDetailsVisible);
        }

    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        RememberOptionText = Core.Lang[LangId._DoNotShowThisMessageAgain];
    }

    protected override async void OnDialogSubmitted(DialogEventArgs e)
    {
        var isValid = _txtInput.ValidateAndShowError();
        if (!isValid)
        {
            await _txtInput.AnimateValidationErrorAsync();
            return;
        }

        base.OnDialogSubmitted(e);
    }

    [MemberNotNull(nameof(_thumbnailIconImage), nameof(_txtInput), nameof(_noteContainer))]
    private StackPanel CreateDialogContentElement()
    {
        var leftSection = new Grid
        {
            Width = THUMBNAIL_SIZE,
            Height = THUMBNAIL_SIZE,
            Margin = new Thickness(0, 0, 24, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            [!Grid.IsVisibleProperty] = this[!IsThumbnailSectionVisibleProperty],
        };
        var thumbnailViewbox = new Viewbox
        {
            Width = THUMBNAIL_SIZE,
            Height = THUMBNAIL_SIZE,
            Stretch = Avalonia.Media.Stretch.Uniform,
            StretchDirection = Avalonia.Media.StretchDirection.Both,
            [!Viewbox.IsVisibleProperty] = this[!IsThumbnailVisibleProperty],
            Child = new Image
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                [!Image.SourceProperty] = this[!ThumbnailProperty],
            },
        };
        _thumbnailIconImage = new Image
        {
            Width = 40,
            Height = 40,
            [!Image.IsVisibleProperty] = this[!IsThumbnailIconVisibleProperty],
        };
        leftSection.Children.Add(thumbnailViewbox);
        leftSection.Children.Add(_thumbnailIconImage);

        var rightSection = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 16,
        };
        var lblHeading = new TextBlock
        {
            MaxHeight = 200,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsTabStop = false,
            FontSize = Const.FONT_SIZE_TITLE,
            FontWeight = Avalonia.Media.FontWeight.Medium,
            [!TextBlock.TextProperty] = this[!HeadingProperty],
            [!TextBlock.IsVisibleProperty] = this[!IsHeadingVisibleProperty],
            [!TextBlock.ForegroundProperty] = Resx.CreateBinding(ResxId.IG_TextAccentColor),
        };
        var lblDescription = new TextBlock
        {
            MaxHeight = 200,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsTabStop = false,
            [!TextBlock.TextProperty] = this[!DescriptionProperty],
            [!TextBlock.IsVisibleProperty] = this[!IsDescriptionVisibleProperty],
        };
        _txtInput = new PhTextBox
        {
            [!PhTextBox.IsRequiredProperty] = this[!IsInputVisibleProperty],
            [!PhTextBox.AcceptValueProperty] = this[!AcceptValueProperty],
            [!TextBox.TextProperty] = this[!InputValueProperty],
            [!TextBox.IsVisibleProperty] = this[!IsInputVisibleProperty],
            [!TextBox.PlaceholderTextProperty] = this[!InputPlaceholderProperty],
        };
        var lblDetails = new Border
        {
            ClipToBounds = true,
            [!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius),
            [!SelectableTextBlock.IsVisibleProperty] = this[!IsDetailsVisibleProperty],
            Child = new ScrollViewer
            {
                MinHeight = 50,
                MaxHeight = 150,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new SelectableTextBlock
                {
                    Padding = new Thickness(6),
                    FontSize = Const.FONT_SIZE_SMALL,
                    FontFamily = Const.FONT_CODE,
                    FontWeight = Avalonia.Media.FontWeight.SemiLight,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    [!SelectableTextBlock.TextProperty] = this[!DetailsProperty],
                    [!SelectableTextBlock.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_BackgroundNeutralBrush),
                },
            }
        };
        _noteContainer = new Border
        {
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            [!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius),
            [!Border.BorderBrushProperty] = Resx.CreateBinding(ResxId.IG_BorderNeutralBrush),
            [!Border.IsVisibleProperty] = this[!IsNoteVisibleProperty],
            Child = new TextBlock
            {
                Padding = new Thickness(12),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                [!TextBlock.TextProperty] = this[!NoteProperty],
            }
        };

        var extraContentSlot = new ContentControl
        {
            [!ContentControl.ContentProperty] = this[!ModalExtraContentProperty],
        };

        var progressEl = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            [!ProgressBar.ValueProperty] = this[!ProgressValueProperty],
            [!ProgressBar.IsIndeterminateProperty] = this[!IsProgressIndeterminateProperty],
            [!ProgressBar.IsVisibleProperty] = this[!IsProgressVisibleProperty],
        };

        rightSection.Children.Add(lblHeading);
        rightSection.Children.Add(lblDescription);
        rightSection.Children.Add(_txtInput);
        rightSection.Children.Add(lblDetails);
        rightSection.Children.Add(_noteContainer);
        rightSection.Children.Add(extraContentSlot);
        rightSection.Children.Add(progressEl);

        Grid.SetColumn(leftSection, 0);
        Grid.SetColumn(rightSection, 1);
        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto, *"),
        };
        mainGrid.Children.Add(leftSection);
        mainGrid.Children.Add(rightSection);

        var root = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Spacing = 16,
            MinHeight = 100,
        };
        root.Children.Add(mainGrid);

        return root;
    }

    [MemberNotNull(nameof(_chkRememberOption))]
    private StackPanel CreateDialogFooterLeftContentElement()
    {
        _chkRememberOption = new CheckBox
        {
            [!CheckBox.IsVisibleProperty] = this[!IsRememberOptionVisibleProperty],
            Content = new TextBlock
            {
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                [!TextBlock.TextProperty] = this[!RememberOptionTextProperty],
            },
        };

        var root = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Spacing = 16,
        };
        root.Children.Add(_chkRememberOption);

        return root;
    }

    protected void LoadThumbnailIconSource()
    {
        if (_thumbnailIconImage.Source is not null) return;

        var bmp = Resx.GetStockIcon(ThumbnailIcon);

        _thumbnailIconImage.Width = Thumbnail is null ? 50 : 40;
        _thumbnailIconImage.Height = Thumbnail is null ? 50 : 40;
        _thumbnailIconImage.Source = bmp;

        if (Thumbnail is null)
        {
            _thumbnailIconImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            _thumbnailIconImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        }
        else
        {
            _thumbnailIconImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            _thumbnailIconImage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        }

        if (bmp is not null)
        {
            Icon = new WindowIcon(bmp);
        }
    }

    private void BindNoteBackground()
    {
        if (_noteContainer is null) return;

        _noteContainer[!Border.BackgroundProperty] = NoteStyle switch
        {
            InfoBarSeverity.Info => Resx.CreateBinding(ResxId.IG_BackgroundInfoBrush),
            InfoBarSeverity.Success => Resx.CreateBinding(ResxId.IG_BackgroundSuccessBrush),
            InfoBarSeverity.Warning => Resx.CreateBinding(ResxId.IG_BackgroundWarningBrush),
            InfoBarSeverity.Danger => Resx.CreateBinding(ResxId.IG_BackgroundDangerBrush),
            _ => Resx.CreateBinding(ResxId.IG_BackgroundNeutralBrush),
        };
    }

    public static async Task<ModalWindowResult> ShowAsync(PhWindow? owner,
        ModalWindowOptions options,
        ModalWindowButton buttons = ModalWindowButton.OK,
        DialogFocus defaultFocus = DialogFocus.Default)
    {
        var modal = new ModalWindow()
        {
            Title = options.Title ?? string.Empty,
            DefaultFocus = defaultFocus,
            Heading = options.Heading,
            Description = options.Description,
            Details = options.Details,
            Note = options.Note,
            NoteStyle = options.NoteStyle ?? InfoBarSeverity.Info,
            Thumbnail = options.Thumbnail,
            ThumbnailIcon = options.ThumbnailIcon,
            InputValue = options.InputValue,
            AcceptValue = options.AcceptValue,
            InputPlaceholder = options.InputPlaceholder,
            IsInputVisible = options.IsInputVisible ?? false,
            IsRememberOptionVisible = options.IsRememberOptionVisible,
            ShowInTaskbar = options.ShowInTaskbar ?? true,
            UseCustomWindowIcon = options.ThumbnailIcon != null,
        };

        if (!modal.IsInputVisible)
        {
            modal.DefaultFocus = DialogFocus.Button1;
        }

        switch (buttons)
        {
            case ModalWindowButton.OK:
                modal.Button1Text = Core.Lang[LangId._OK];
                modal.IsButton1Visible = true;
                modal.IsButton2Visible = modal.IsButton3Visible = false;
                break;

            case ModalWindowButton.Close:
                modal.Button1Text = Core.Lang[LangId._Close];
                modal.IsButton1Visible = true;
                modal.IsButton2Visible = modal.IsButton3Visible = false;
                break;

            case ModalWindowButton.Yes_No:
                modal.Button1Text = Core.Lang[LangId._Yes];
                modal.Button2Text = Core.Lang[LangId._No];
                modal.IsButton1Visible = modal.IsButton2Visible = true;
                modal.IsButton3Visible = false;
                break;

            case ModalWindowButton.OK_Cancel:
                modal.Button1Text = Core.Lang[LangId._OK];
                modal.Button2Text = Core.Lang[LangId._Cancel];
                modal.IsButton1Visible = modal.IsButton2Visible = true;
                modal.IsButton3Visible = false;
                break;

            case ModalWindowButton.OK_Close:
                modal.Button1Text = Core.Lang[LangId._OK];
                modal.Button2Text = Core.Lang[LangId._Close];
                modal.IsButton1Visible = modal.IsButton2Visible = true;
                modal.IsButton3Visible = false;
                break;

            case ModalWindowButton.LearnMore_Close:
                modal.Button1Text = Core.Lang[LangId._LearnMore];
                modal.Button2Text = Core.Lang[LangId._Close];
                modal.IsButton1Visible = modal.IsButton2Visible = true;
                modal.IsButton3Visible = false;
                break;

            case ModalWindowButton.Continue_Quit:
                modal.Button1Text = Core.Lang[LangId._Continue];
                modal.Button2Text = Core.Lang[LangId._Quit];
                modal.IsButton1Visible = modal.IsButton2Visible = true;
                modal.IsButton3Visible = false;

                modal.DefaultButton = DialogButton.Button2;
                modal.DefaultFocus = DialogFocus.Button2;
                break;

            default:
                break;
        }

        var exitCode = await modal.ShowAsync(owner);

        var result = new ModalWindowResult()
        {
            ExitCode = exitCode,
            InputValue = modal.InputValue,
            IsRememberOptionChecked = modal.IsRememberOptionChecked,
        };

        return result;
    }

    public static async Task<ModalWindowResult> ShowWarningAsync(PhWindow? owner,
        ModalWindowOptions options, ModalWindowButton buttons = ModalWindowButton.OK)
    {
        options.Heading ??= Core.Lang[LangId._Warning];
        options.NoteStyle ??= InfoBarSeverity.Warning;
        options.ThumbnailIcon ??= StockIconId.Warning;

        return await ShowAsync(owner, options, buttons);
    }

    public static async Task<ModalWindowResult> ShowErrorAsync(PhWindow? owner,
        ModalWindowOptions options, ModalWindowButton buttons = ModalWindowButton.OK)
    {
        options.Heading ??= string.Empty;
        options.NoteStyle ??= InfoBarSeverity.Danger;
        options.ThumbnailIcon ??= StockIconId.Error;

        return await ShowWarningAsync(owner, options, buttons);
    }

    public static async Task<ModalWindowResult> ShowInfoAsync(PhWindow? owner,
        ModalWindowOptions options, ModalWindowButton buttons = ModalWindowButton.OK)
    {
        options.Heading ??= string.Empty;
        options.NoteStyle ??= InfoBarSeverity.Info;
        options.ThumbnailIcon ??= StockIconId.Info;

        return await ShowAsync(owner, options, buttons);
    }

    public static async Task<ModalWindowResult> ShowInputAsync(PhWindow? owner,
        ModalWindowOptions options, ModalWindowButton buttons = ModalWindowButton.OK_Cancel)
    {
        options.IsInputVisible ??= true;

        return await ShowAsync(owner, options, buttons);
    }

    public static async Task ShowSettingsSaveErrorAsync(PhWindow? owner, string title)
    {
        if (Config.SavingException is not Exception ex) return;

        _ = await ShowErrorAsync(owner, new ModalWindowOptions
        {
            Title = title,
            Heading = ex.Message,
            Description = BHelper.GetRealPlatformConfigDir(Config.CONFIG_USER),
        });
    }

    public static async Task<bool> ShowUnhandledErrorAsync(Exception ex, PhWindow? owner = null,
        string? heading = null, string? description = null)
    {
        var details = BHelper.GetExceptionDetails(ex);
        var isContinue = false;

        var descriptionText = string.IsNullOrEmpty(heading)
            ? string.Empty
            : ex.Message;

        var result = await ShowErrorAsync(owner, new ModalWindowOptions
        {
            Title = $"{Core.Lang[LangId._UnhandledException]} – {BHelper.AppDisplayName}",
            Heading = heading ?? ex.Message,
            Description = descriptionText,
            Details = details,
            ShowInTaskbar = true,
            NoteStyle = InfoBarSeverity.Danger,
            Note = Core.Lang[LangId._UnhandledException_Description],
        }, ModalWindowButton.Continue_Quit);

        if (result.ExitCode == DialogExitCode.Cancel)
        {
            BHelper.ExitApp(true, 0);
        }
        else if (result.ExitCode == DialogExitCode.OK)
        {
            isContinue = true;
        }

        return isContinue;
    }

}
