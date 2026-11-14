using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Printing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.UI;
using PhotoViewer.UI.Windowing;
using System;

namespace PhotoViewer.Common.Windows;

public sealed class PrintWindow : DialogWindow
{
    protected override int MIN_WIDTH => 0;
    protected override int MAX_WIDTH => int.MaxValue;
    protected override Thickness ContentPadding => new(0);

    private const double DEFAULT_WIDTH = 1000;
    private const double DEFAULT_HEIGHT = 680;
    private const double MIN_RESTORE_WIDTH = 640;
    private const double MIN_RESTORE_HEIGHT = 420;

    private readonly PrintWindowView _view;
    private readonly PhButton _systemDialogLink;
    private readonly TextBlock _summary;

    public string? CompletedMessage { get; private set; }

    public PrintWindow(PrintSession session, PrintRegion region = PrintRegion.WholeImage)
    {
        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button1;

        PressEnterToSubmit = false;

        CanResize = true;
        CanMaximize = true;
        CanMinimize = false;
        ShowInTaskbar = true;
        SizeToContent = SizeToContent.Manual;
        MinWidth = MIN_RESTORE_WIDTH;
        MinHeight = MIN_RESTORE_HEIGHT;

        RestoreWindowBounds(Core.Config.PrintWindowBounds,
            new(DEFAULT_WIDTH, DEFAULT_HEIGHT),
            new(MIN_RESTORE_WIDTH, MIN_RESTORE_HEIGHT));
        if (Core.Config.EnablePrintWindowMaximized) WindowState = WindowState.Maximized;

        _view = new PrintWindowView(session, region);
        _view.StateChanged += View_StateChanged;
        DialogContent = _view;

        _systemDialogLink = new PhButton
        {
            Variant = PhButtonVariant.Link,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = Core.PrintProvider.CanShowSystemDialog,
        };
        _systemDialogLink.Click += SystemDialogLink_Click;

        _summary = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        };

        DialogFooterLeftContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24,
            Children = { _systemDialogLink, _summary },
        };

        AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Print_Title];
        Button2Text = Core.Lang[LangId._Cancel];
        _systemDialogLink.Text = Core.Lang[LangId.Print_BtnSystemDialog];
        View_StateChanged();
    }

    protected override async void OnDialogSubmitted(DialogEventArgs e)
    {
        try
        {
            if (!e.CanProceed || _view.IsPrinting) return;

            var printer = _view.Printer;
            if (!await _view.PrintAsync(this)) return;

            CompletedMessage = printer?.Id == PrintProviderBase.PDF_PRINTER_ID
                ? Core.Lang[LangId.Print_Saved]
                : Core.Lang[LangId.Print_Sent, printer?.DisplayName];

            base.OnDialogSubmitted(e);
        }
        catch (Exception ex)
        {
            await ModalWindow.ShowErrorAsync(this, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Print_Title],
                Description = ex.Message,
                Details = BHelper.GetExceptionDetails(ex),
            });
        }
    }

    protected override void OnDialogCancelled(DialogEventArgs e)
    {
        if (_view.IsPrinting)
        {
            _view.CancelPrinting();
            return;
        }

        base.OnDialogCancelled(e);
    }

    protected override void OnDialogAborted()
    {
        if (_view.IsPrinting)
        {
            _view.CancelPrinting();
            return;
        }

        base.OnDialogAborted();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_view.IsPrinting)
        {
            _view.CancelPrinting();
            e.Cancel = true;
            return;
        }

        Core.Config.EnablePrintWindowMaximized = RestorableWindowState == WindowState.Maximized;
        if (WindowedBounds is { } bounds
            && bounds.Width >= MIN_RESTORE_WIDTH
            && bounds.Height >= MIN_RESTORE_HEIGHT)
        {
            Core.Config.PrintWindowBounds = bounds;
        }

        _view.SaveConfig();
        _view.Close();

        base.OnClosing(e);
    }

    private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.PageDown or Key.PageUp) || e.KeyModifiers != KeyModifiers.None) return;
        if (FocusManager?.GetFocusedElement() is TextBox or ComboBox { IsDropDownOpen: true }) return;

        _view.GoToPage(e.Key == Key.PageDown ? 1 : -1);
        e.Handled = true;
    }

    private void View_StateChanged()
    {
        Button1Text = _view.Printer?.Id == PrintProviderBase.PDF_PRINTER_ID
            ? Core.Lang[LangId._Save]
            : Core.Lang[LangId.Print_Title];

        _btn1.IsEnabled = _view.CanPrint;
        _systemDialogLink.IsEnabled = !_view.IsPrinting;
        _summary.Text = _view.SummaryText;
    }

    private async void SystemDialogLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_view.IsPrinting) return;
            if (!await _view.ShowSystemDialogAsync(this)) return;

            DialogResult = DialogExitCode.Cancel;
            Close(DialogResult);
        }
        catch (Exception ex)
        {
            await ModalWindow.ShowErrorAsync(this, new ModalWindowOptions
            {
                Title = Core.Lang[LangId.Print_Title],
                Description = ex.Message,
                Details = BHelper.GetExceptionDetails(ex),
            });
        }
    }
}
