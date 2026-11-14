using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using PhotoViewer.Common;
using PhotoViewer.Common.AppThemes;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace PhotoViewer.UI.Windowing;

public partial class PhWindow : Window
{
    protected bool _canUseBackdrop = false;

    private WindowState _stateBeforeMinimized = WindowState.Normal;

    private Rect? _windowedBounds;

    private bool _isWindowedBoundsQueued;

    private const double MIN_VISIBLE_SIZE = 64;

    protected static Color DefaultActivateBg => Core.Theme.Settings.IsDarkMode
        ? AppThemeColors.BackgroundActivateDark
        : AppThemeColors.BackgroundActivateLight;

    protected static Color DefaultInactivateBg => Core.Theme.Settings.IsDarkMode
        ? AppThemeColors.BackgroundInactivateDark
        : AppThemeColors.BackgroundInactivateLight;

    public nint Handle => GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    public double Dpi => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;

    public WindowState RestorableWindowState => WindowState == WindowState.Minimized
        ? _stateBeforeMinimized
        : WindowState;

    public Rect? WindowedBounds
    {
        get
        {
            CommitWindowedBounds();
            return _windowedBounds;
        }
    }

    public virtual bool UseCustomBackdrop { get; set; } = false;

    public virtual bool UseCustomWindowIcon { get; set; } = false;

    public BackdropStyle BackdropStyle
    {
        get => GetValue(BackdropStyleProperty);
        set => SetValue(BackdropStyleProperty, value);
    }
    public static readonly StyledProperty<BackdropStyle> BackdropStyleProperty =
        AvaloniaProperty.Register<Window, BackdropStyle>(nameof(BackdropStyle), BackdropStyle.None);

    public Hotkey[] CloseWindowHotkeys
    {
        get => GetValue(CloseWindowHotkeysProperty);
        set => SetValue(CloseWindowHotkeysProperty, value);
    }
    public static readonly StyledProperty<Hotkey[]> CloseWindowHotkeysProperty =
        AvaloniaProperty.Register<Window, Hotkey[]>(nameof(CloseWindowHotkeys), []);

    public bool ShowTitleBarIcon
    {
        get => GetValue(ShowTitleBarIconProperty);
        set => SetValue(ShowTitleBarIconProperty, value);
    }
    public static readonly StyledProperty<bool> ShowTitleBarIconProperty =
        AvaloniaProperty.Register<Window, bool>(nameof(ShowTitleBarIcon), true);

    public bool IsFrameless
    {
        get => GetValue(IsFramelessProperty);
        set => SetValue(IsFramelessProperty, value);
    }
    public static readonly StyledProperty<bool> IsFramelessProperty =
        AvaloniaProperty.Register<Window, bool>(nameof(IsFrameless), false);

    public PhWindow()
    {
        OnIgFramelessModeChanged(IsFrameless);
        if (BackdropStyle == BackdropStyle.None)
        {
            UpdateBackground(true);
        }

        Topmost = Core.Config.EnableWindowTopMost;

        Core.ThemeChanged += Core_ThemeChanged;
        Core.LanguageChanged += Core_LanguageChanged;
        Core.Config.PropertyChanged += Config_PropertyChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        OnIgLanguageChanged();

        OnIgTitleBarIconVisibilityChanged(ShowTitleBarIcon);

        UpdateTitleBarDarkMode();

        DetachImeWhenNotEditingText();

        TrackWindowedBounds();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        ActualThemeVariantChanged += PhWindow_ActualThemeVariantChanged;
        Activated += PhWindow_Activated;
        Deactivated += PhWindow_Deactivated;
        PositionChanged += PhWindow_PositionChanged;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        ActualThemeVariantChanged -= PhWindow_ActualThemeVariantChanged;
        Activated -= PhWindow_Activated;
        Deactivated -= PhWindow_Deactivated;
        PositionChanged -= PhWindow_PositionChanged;

        Core.ThemeChanged -= Core_ThemeChanged;
        Core.LanguageChanged -= Core_LanguageChanged;
        Core.Config.PropertyChanged -= Config_PropertyChanged;
    }

    private void PhWindow_PositionChanged(object? sender, PixelPointEventArgs e)
    {
        TrackWindowedBounds();
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        TrackWindowedBounds();
    }

    private void PhWindow_ActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (UseCustomBackdrop) return;

        if (IsActive) PhWindow_Activated(sender, e);
        else PhWindow_Deactivated(sender, e);
    }

    private async void PhWindow_Activated(object? sender, EventArgs e)
    {
        OnIgActivated(e);

        DetachImeWhenNotEditingText();

        if (UseCustomBackdrop) return;
        if (_canUseBackdrop)
        {
            await AnimateBackgroundColorAsync(DefaultActivateBg.A(0));
        }
    }

    private async void PhWindow_Deactivated(object? sender, EventArgs e)
    {
        OnIgDeactivated(e);

        if (UseCustomBackdrop) return;
        if (_canUseBackdrop)
        {
            await AnimateBackgroundColorAsync(DefaultInactivateBg);
        }
    }

    private void Core_ThemeChanged(object? sender, ThemePackChangedEventArgs e)
    {
        if (!UseCustomWindowIcon)
        {
            _ = UpdateWindowIconAsync();
        }

        UpdateBackground(IsActive);
        UpdateTitleBarDarkMode();
        OnIgThemeChanged(e);
    }

    private void Core_LanguageChanged(object? sender, EventArgs e)
    {
        OnIgLanguageChanged();
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Config.EnableWindowTopMost))
        {
            Topmost = Core.Config.EnableWindowTopMost;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == BackdropStyleProperty)
        {
            OnIgBackdropStyleChanged((BackdropStyle)e.NewValue!);
        }

        else if (e.Property == IsFramelessProperty)
        {
            OnIgFramelessModeChanged((bool)e.NewValue!);
        }

        else if (e.Property == ShowTitleBarIconProperty)
        {
            OnIgTitleBarIconVisibilityChanged((bool)e.NewValue!);
        }

        else if (e.Property == IconProperty)
        {
            OnIgTitleBarIconVisibilityChanged(ShowTitleBarIcon);
        }

        else if (e.Property == WindowStateProperty)
        {
            var newState = (WindowState)e.NewValue!;
            var oldState = (WindowState)e.OldValue!;

            if (newState == WindowState.Minimized && oldState != WindowState.Minimized)
            {
                _stateBeforeMinimized = oldState;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        foreach (var hk in CloseWindowHotkeys)
        {
            if (hk.IsSame(e.Key, e.KeyModifiers))
            {
                OnIgCloseWindowHotkeyPressed(e);
                if (!e.Handled)
                {
                    e.Handled = true;
                    Close();
                    return;
                }

                break;
            }
        }

        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (IsFrameless)
        {
            var p = e.GetCurrentPoint(this);
            if (p.Properties.IsLeftButtonPressed && p.Position.Y < 15) BeginMoveDrag(e);
        }
    }

    protected virtual void OnIgActivated(EventArgs e) { }

    protected virtual void OnIgDeactivated(EventArgs e) { }

    protected virtual void OnIgCloseWindowHotkeyPressed(KeyEventArgs e) { }

    protected virtual void OnIgFramelessModeChanged(bool enable)
    {
        if (enable)
        {
            ExtendClientAreaToDecorationsHint = true;
            WindowDecorations = WindowDecorations.BorderOnly;
        }
        else
        {
            ExtendClientAreaToDecorationsHint = false;
            WindowDecorations = WindowDecorations.Full;
        }

        Dispatcher.Post(
            () => OnIgTitleBarIconVisibilityChanged(ShowTitleBarIcon),
            DispatcherPriority.Background);
    }

    protected virtual void OnIgTitleBarIconVisibilityChanged(bool show) { }

    protected virtual void OnIgThemeChanged(ThemePackChangedEventArgs e) { }

    protected virtual void OnIgLanguageChanged() { }

    protected virtual void OnIgBackdropStyleChanged(BackdropStyle style)
    {
        if (style != BackdropStyle.None)
        {
            if (!UseCustomBackdrop)
            {
                WindowTransparencyLevel[] levels = style switch
                {
                    BackdropStyle.Mica => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None],
                    BackdropStyle.MicaAlt => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None],
                    BackdropStyle.Acrylic => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
                    _ => [WindowTransparencyLevel.None],
                };

                TransparencyLevelHint = levels;
            }
        }

        _canUseBackdrop = !BHelper.IsWindows10
            && !ActualTransparencyLevel.Equals(WindowTransparencyLevel.None)
            && !ActualTransparencyLevel.Equals(WindowTransparencyLevel.Transparent);

        UpdateBackground(true);
    }

    protected virtual void UpdateBackground(bool isActive)
    {
        var windowBg = isActive ? DefaultActivateBg : DefaultInactivateBg;

        if (_canUseBackdrop)
        {
            Background = windowBg.A(0).ToBrush();
        }
        else
        {
            Background = windowBg.ToBrush();
        }
    }

    protected void DetachImeWhenNotEditingText()
    {
        if (FocusManager?.GetFocusedElement() is TextBox
            or NumericUpDown
            or MaskedTextBox
            or AutoCompleteBox) return;

        Core.ShellProvider?.DetachIme(Handle);
    }

    protected void UpdateTitleBarDarkMode()
    {
        Core.ShellProvider?.SetTitleBarDarkMode(Handle, Core.Theme.Settings.IsDarkMode);
    }

    protected async Task UpdateWindowIconAsync(string? customIconPath = null)
    {
        var iconPath = Core.Theme.GetIconPath(IgThemeIcon.AppLogo);
        var useDefaultIcon = !File.Exists(iconPath);

        if (string.IsNullOrWhiteSpace(customIconPath))
        {
            if (useDefaultIcon)
            {
                Icon = Resx.GetDefaultWindowIcon();

                return;
            }
        }
        else
        {
            iconPath = customIconPath;
        }

        var size = DpiScale(64);
        var bytes = await MagickCodec.QuickDecodeAsync(iconPath, ImageMagick.MagickFormat.Ico, size, size);
        if (bytes is null) return;

        using var ms = new MemoryStream(bytes);
        Icon = new WindowIcon(ms);
    }

    protected async Task AnimateBackgroundColorAsync(Color toColor)
    {
        if (Background is not SolidColorBrush fromBrush) return;

        var fromColor = fromBrush.Color;
        var toBrush = toColor.ToBrush();
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new LinearEasing(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters = { new Setter(SolidColorBrush.ColorProperty, fromColor) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters = { new Setter(SolidColorBrush.ColorProperty, toColor) }
                }
            },
        };

        Background = toBrush;
        await animation.RunAsync(toBrush);
    }

    private void TrackWindowedBounds()
    {
        if (_isWindowedBoundsQueued || !IsVisible || WindowState != WindowState.Normal) return;
        _isWindowedBoundsQueued = true;

        Dispatcher.UIThread.Post(() =>
        {
            _isWindowedBoundsQueued = false;
            CommitWindowedBounds();
        }, DispatcherPriority.Background);
    }

    private void CommitWindowedBounds()
    {
        if (!IsVisible || WindowState != WindowState.Normal) return;

        var size = ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        _windowedBounds = new Rect(Position.X, Position.Y, (int)size.Width, (int)size.Height);
    }

    protected void RestoreWindowBounds(Rect savedBounds, Size defaultSize, Size minSize)
    {
        var size = savedBounds.Width >= minSize.Width && savedBounds.Height >= minSize.Height
            ? savedBounds.Size
            : defaultSize;
        var pos = new PixelPoint((int)savedBounds.X, (int)savedBounds.Y);

        WindowStartupLocation = WindowStartupLocation.Manual;

        var screens = Screens;
        if (screens is null || screens.All.Count == 0 || IsVisibleOnAnyScreen(screens, pos, size))
        {
            Width = size.Width;
            Height = size.Height;
            Position = pos;
            return;
        }

        var screen = screens.ScreenFromPoint(pos) ?? screens.Primary ?? screens.All[0];
        var workArea = screen.GetActualWorkingArea();
        var maxSize = workArea.Size.ToSize(screen.Scaling);

        size = new Size(Math.Min(size.Width, maxSize.Width), Math.Min(size.Height, maxSize.Height));
        Width = size.Width;
        Height = size.Height;
        Position = workArea.CenterRect(new PixelRect(PixelSize.FromSize(size, screen.Scaling))).Position;
    }

    private static bool IsVisibleOnAnyScreen(Screens screens, PixelPoint pos, Size size)
    {
        foreach (var screen in screens.All)
        {
            var winRect = new PixelRect(pos, PixelSize.FromSize(size, screen.Scaling));
            var visible = screen.GetActualWorkingArea().Intersect(winRect);
            var minVisible = PixelSize.FromSize(new Size(
                Math.Min(MIN_VISIBLE_SIZE, size.Width),
                Math.Min(MIN_VISIBLE_SIZE, size.Height)), screen.Scaling);

            if (visible.Width >= minVisible.Width && visible.Height >= minVisible.Height) return true;
        }

        return false;
    }

    public void RestoreAndActivate()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _stateBeforeMinimized;
        }

        Activate();
    }

    public double DpiScale(double value, double? scaleFactor = null) => (scaleFactor ?? Dpi) * value;

    public Size DpiScale(Size value, double? scaleFactor = null) => new Size(DpiScale(value.Width, scaleFactor), DpiScale(value.Height, scaleFactor));

    public Point DpiScale(Point value, double? scaleFactor = null) => new Point(DpiScale(value.X, scaleFactor), DpiScale(value.Y, scaleFactor));

}
