using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PhotoViewer.Common;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Types;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoViewer.UI.Windowing;

public partial class DialogWindow : PhWindow
{
    protected virtual int MIN_WIDTH => 400;
    protected virtual int MAX_WIDTH => 600;
    protected virtual Thickness ContentPadding => new(24, 14, 24, 20);

    protected Grid _contentEl;
    protected Border _footerEl;
    protected PhButton _btn1;
    protected PhButton _btn2;
    protected PhButton _btn3;

    protected TaskCompletionSource<DialogExitCode> _taskSourceExitCode = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public object DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }
    public static readonly StyledProperty<object> DialogContentProperty =
        AvaloniaProperty.Register<DialogWindow, object>(nameof(DialogContent));

    public object DialogFooterLeftContent
    {
        get => GetValue(DialogFooterLeftContentProperty);
        set => SetValue(DialogFooterLeftContentProperty, value);
    }
    public static readonly StyledProperty<object> DialogFooterLeftContentProperty =
        AvaloniaProperty.Register<DialogWindow, object>(nameof(DialogFooterLeftContent));

    public bool IsTitleVisible => !string.IsNullOrWhiteSpace(Title) && BHelper.OS != OSType.Linux;
    public static readonly DirectProperty<DialogWindow, bool> IsTitleVisibleProperty =
        AvaloniaProperty.RegisterDirect<DialogWindow, bool>(nameof(IsTitleVisible), i => i.IsTitleVisible);

    public string Button1Text
    {
        get => GetValue(Button1TextProperty);
        set => SetValue(Button1TextProperty, value);
    }
    public static readonly StyledProperty<string> Button1TextProperty =
        AvaloniaProperty.Register<DialogWindow, string>(nameof(Button1Text), "[Button 1]");

    public string Button2Text
    {
        get => GetValue(Button2TextProperty);
        set => SetValue(Button2TextProperty, value);
    }
    public static readonly StyledProperty<string> Button2TextProperty =
        AvaloniaProperty.Register<DialogWindow, string>(nameof(Button2Text), "[Button 2]");

    public string Button3Text
    {
        get => GetValue(Button3TextProperty);
        set => SetValue(Button3TextProperty, value);
    }
    public static readonly StyledProperty<string> Button3TextProperty =
        AvaloniaProperty.Register<DialogWindow, string>(nameof(Button3Text), "[Button 3]");

    public Geometry? Button1Icon
    {
        get => GetValue(Button1IconProperty);
        set => SetValue(Button1IconProperty, value);
    }
    public static readonly StyledProperty<Geometry?> Button1IconProperty =
        AvaloniaProperty.Register<DialogWindow, Geometry?>(nameof(Button1Icon));

    public Geometry? Button2Icon
    {
        get => GetValue(Button2IconProperty);
        set => SetValue(Button2IconProperty, value);
    }
    public static readonly StyledProperty<Geometry?> Button2IconProperty =
        AvaloniaProperty.Register<DialogWindow, Geometry?>(nameof(Button2Icon));

    public Geometry? Button3Icon
    {
        get => GetValue(Button3IconProperty);
        set => SetValue(Button3IconProperty, value);
    }
    public static readonly StyledProperty<Geometry?> Button3IconProperty =
        AvaloniaProperty.Register<DialogWindow, Geometry?>(nameof(Button3Icon));

    public bool IsButton1Visible
    {
        get => GetValue(IsButton1VisibleProperty);
        set => SetValue(IsButton1VisibleProperty, value);
    }
    public static readonly StyledProperty<bool> IsButton1VisibleProperty =
        AvaloniaProperty.Register<DialogWindow, bool>(nameof(IsButton1Visible), true);

    public bool IsButton2Visible
    {
        get => GetValue(IsButton2VisibleProperty);
        set => SetValue(IsButton2VisibleProperty, value);
    }
    public static readonly StyledProperty<bool> IsButton2VisibleProperty =
        AvaloniaProperty.Register<DialogWindow, bool>(nameof(IsButton2Visible), false);

    public bool IsButton3Visible
    {
        get => GetValue(IsButton3VisibleProperty);
        set => SetValue(IsButton3VisibleProperty, value);
    }
    public static readonly StyledProperty<bool> IsButton3VisibleProperty =
        AvaloniaProperty.Register<DialogWindow, bool>(nameof(IsButton3Visible), false);

    public DialogButton DefaultButton { get; set; } = DialogButton.Button1;

    public DialogFocus DefaultFocus { get; set; } = DialogFocus.Default;

    public bool PressEnterToSubmit { get; set; } = true;

    public DialogExitCode DialogResult { get; set; } = DialogExitCode.None;

    public DialogWindow()
    {
        CanResize = false;
        ShowInTaskbar = false;
        CanMinimize = false;

        SizeToContent = SizeToContent.WidthAndHeight;
        BackdropStyle = BHelper.OS == OSType.Windows ? BackdropStyle.MicaAlt : BackdropStyle.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CloseWindowHotkeys = [new(Avalonia.Input.Key.Escape)];

        Content = CreateContentElement();
        _ = UpdateWindowIconAsync();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ApplyTheme();
        SetDefaultButton(DefaultButton);

        await Task.Delay(200);
        SetDefaultFocus();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (DialogResult == DialogExitCode.None) DialogResult = DialogExitCode.Abort;

        _ = _taskSourceExitCode.TrySetResult(DialogResult);

        Owner?.Activate();
    }

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);

        ApplyTheme();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!PressEnterToSubmit) return;

        var hk = new Hotkey(e.KeyModifiers, e.Key);
        if (hk.IsSame(Key.Enter))
        {
            e.Handled = true;

            if (DefaultButton == DialogButton.Button1)
            {
                Button1_Click(_btn1, e);
            }
            else if (DefaultButton == DialogButton.Button2)
            {
                Button2_Click(_btn2, e);
            }
            else if (DefaultButton == DialogButton.Button3)
            {
                Button3_Click(_btn3, e);
            }
            else
            {
                OnDialogSubmitted(new DialogEventArgs(DialogAction.Submit));
            }
        }
    }

    protected override void OnIgCloseWindowHotkeyPressed(KeyEventArgs e)
    {
        base.OnIgCloseWindowHotkeyPressed(e);

        e.Handled = true;
        OnDialogAborted();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == TitleProperty)
        {
            RaisePropertyChanged(IsTitleVisibleProperty, default, IsTitleVisible);
        }
    }

    [MemberNotNull(
        nameof(_contentEl),
        nameof(_footerEl),
        nameof(_btn1),
        nameof(_btn2),
        nameof(_btn3))]
    protected Grid CreateContentElement()
    {
        var dialogContentSlot = new ContentControl
        {
            Padding = ContentPadding,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            [!ContentControl.ContentProperty] = this[!DialogContentProperty],
        };
        _contentEl = new Grid();
        _contentEl.Children.Add(dialogContentSlot);

        var footerLeftSlot = new ContentControl
        {
            [!ContentControl.ContentProperty] = this[!DialogFooterLeftContentProperty],
        };

        _btn1 = new PhButton
        {
            MinWidth = 100,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [!PhButton.TextProperty] = this[!Button1TextProperty],
            [!PhButton.IconDataProperty] = this[!Button1IconProperty],
            [!PhButton.IsVisibleProperty] = this[!IsButton1VisibleProperty],
        };
        _btn2 = new PhButton
        {
            MinWidth = 100,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [!PhButton.TextProperty] = this[!Button2TextProperty],
            [!PhButton.IconDataProperty] = this[!Button2IconProperty],
            [!PhButton.IsVisibleProperty] = this[!IsButton2VisibleProperty],
        };
        _btn3 = new PhButton
        {
            MinWidth = 100,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [!PhButton.TextProperty] = this[!Button3TextProperty],
            [!PhButton.IconDataProperty] = this[!Button3IconProperty],
            [!PhButton.IsVisibleProperty] = this[!IsButton3VisibleProperty],
        };
        _btn1.Click += Button1_Click;
        _btn2.Click += Button2_Click;
        _btn3.Click += Button3_Click;
        var footerRightPanel = new StackPanel
        {
            Spacing = 8,
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        footerRightPanel.KeyDown += FooterContent_KeyDown;
        footerRightPanel.Children.AddRange([_btn1, _btn2, _btn3]);

        var footerWrapper = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*, Auto"),
            Margin = new Thickness(24, 17, 24, 18),
            ColumnSpacing = 20,
        };
        Grid.SetColumn(footerLeftSlot, 0);
        Grid.SetColumn(footerRightPanel, 1);
        footerWrapper.Children.AddRange([footerLeftSlot, footerRightPanel]);

        _footerEl = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footerWrapper,
        };

        var root = new Grid
        {
            MinWidth = MIN_WIDTH,
            MaxWidth = MAX_WIDTH,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            RowDefinitions = new RowDefinitions("*, Auto"),
        };
        Grid.SetRow(_contentEl, 0);
        Grid.SetRow(_footerEl, 1);

        root.Children.Add(_contentEl);
        root.Children.Add(_footerEl);

        return root;
    }

    private void FooterContent_KeyDown(object? sender, KeyEventArgs e)
    {
        var isMoveNext = e.Key == Key.Up || e.Key == Key.Right;
        var isMoveBack = e.Key == Key.Down || e.Key == Key.Left;
        if (!isMoveNext && !isMoveBack) return;
        if (sender is not StackPanel panel) return;

        var visibleButtons = panel.Children
            .OfType<Button>()
            .Where(i => i.IsVisible)
            .ToList();
        if (visibleButtons.Count == 0) return;

        var focusedIndex = -1;
        if (FocusManager?.GetFocusedElement() is Button focusedButton)
        {
            focusedIndex = visibleButtons.IndexOf(focusedButton);
        }

        if (focusedIndex == -1)
        {
            visibleButtons[0].Focus(NavigationMethod.Tab);
        }
        else
        {
            if (isMoveNext)
            {
                focusedIndex = (focusedIndex + 1) % visibleButtons.Count;
            }
            else if (isMoveBack)
            {
                focusedIndex = (focusedIndex - 1 + visibleButtons.Count) % visibleButtons.Count;
            }

            visibleButtons[focusedIndex].Focus(NavigationMethod.Tab);
        }

        e.Handled = true;
    }

    private void Button1_Click(object? sender, RoutedEventArgs e)
    {
        OnDialogSubmitted(new DialogEventArgs(DialogAction.Submit));
    }

    private void Button2_Click(object? sender, RoutedEventArgs e)
    {
        OnDialogCancelled(new DialogEventArgs(DialogAction.Cancel));
    }

    private void Button3_Click(object? sender, RoutedEventArgs e)
    {
        OnDialogApplied(new DialogEventArgs(DialogAction.Apply));
    }

    protected virtual void OnDialogAborted()
    {
        DialogResult = DialogExitCode.Abort;
        Close(DialogResult);
    }

    protected virtual void OnDialogSubmitted(DialogEventArgs e)
    {
        if (!e.CanProceed) return;
        DialogResult = DialogExitCode.OK;
        Close(DialogResult);
    }

    protected virtual void OnDialogCancelled(DialogEventArgs e)
    {
        if (!e.CanProceed) return;
        DialogResult = DialogExitCode.Cancel;
        Close(DialogResult);
    }

    protected virtual void OnDialogApplied(DialogEventArgs e)
    {
        if (!e.CanProceed) return;
        DialogResult = DialogExitCode.None;
    }

    protected void ApplyTheme()
    {
        var isDarkMode = Core.Theme.Settings.IsDarkMode;
        var bg = AppThemeColors.BgBrush.Color.NoAlpha();

        var contentAlpha = Math.Max(isDarkMode ? 180 : 220, bg.A);
        var contentBg = bg.WithAlpha(contentAlpha);
        _contentEl.Background = contentBg.ToBrush();

        var footerAlpha = _canUseBackdrop ? Math.Max(180, contentAlpha / 2) : contentAlpha;
        var footerBg = bg
            .WithBrightness(isDarkMode ? 0.075f : -0.075f)
            .WithAlpha(footerAlpha);
        _footerEl.Background = footerBg.ToBrush();

        _footerEl.BorderBrush = footerBg
            .WithBrightness(isDarkMode ? 0.075f : -0.075f)
            .ToBrush();
    }

    protected void SetDefaultButton(DialogButton btn)
    {
        if (btn == DialogButton.Button1)
        {
            _btn1.IsDefault = PressEnterToSubmit;
            _btn1.Variant = PhButtonVariant.Accent;
        }
        else if (btn == DialogButton.Button2)
        {
            _btn2.IsDefault = PressEnterToSubmit;
            _btn2.Variant = PhButtonVariant.Accent;
        }
        else if (btn == DialogButton.Button3)
        {
            _btn3.IsDefault = PressEnterToSubmit;
            _btn3.Variant = PhButtonVariant.Accent;
        }
    }

    protected void SetDefaultFocus()
    {
        if (DefaultFocus == DialogFocus.Button1)
        {
            _btn1.Focus(Avalonia.Input.NavigationMethod.Tab);
        }
        else if (DefaultFocus == DialogFocus.Button2)
        {
            _btn2.Focus(Avalonia.Input.NavigationMethod.Tab);
        }
        else if (DefaultFocus == DialogFocus.Button3)
        {
            _btn3.Focus(Avalonia.Input.NavigationMethod.Tab);
        }
    }

    public async Task<DialogExitCode> ShowAsync(PhWindow? owner)
    {
        _taskSourceExitCode = new TaskCompletionSource<DialogExitCode>();

        if (owner is not null && owner.IsVisible)
        {
            await ShowDialog(owner);
        }
        else
        {
            Show();
        }

        var exitCode = await _taskSourceExitCode.Task;

        return exitCode;
    }

}
