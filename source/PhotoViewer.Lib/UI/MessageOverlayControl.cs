using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.UI;

public class MessageOverlayControl : PhOverlay
{
    private CancellationTokenSource? _cancelMessage;
    private readonly Lock _lock = new();
    private readonly TextBlock _lblHeading;
    private readonly SelectableTextBlock _lblDescription;
    private readonly PhButton _btnCopy;
    private readonly SelectableTextBlock _lblDetails;
    private readonly Border _detailsBox;

    private bool _isSettingMessage;

    public string? Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }
    public static readonly StyledProperty<string?> HeadingProperty =
        AvaloniaProperty.Register<MessageOverlayControl, string?>(nameof(Heading));

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<MessageOverlayControl, string?>(nameof(Description));

    public string? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }
    public static readonly StyledProperty<string?> DetailsProperty =
        AvaloniaProperty.Register<MessageOverlayControl, string?>(nameof(Details));

    public bool IsHeadingVisible => !string.IsNullOrWhiteSpace(Heading);
    public static readonly DirectProperty<MessageOverlayControl, bool> IsHeadingVisibleProperty =
        AvaloniaProperty.RegisterDirect<MessageOverlayControl, bool>(nameof(IsHeadingVisible), i => i.IsHeadingVisible);

    public bool IsDescriptionVisible => !string.IsNullOrWhiteSpace(Description);
    public static readonly DirectProperty<MessageOverlayControl, bool> IsDescriptionVisibleProperty =
        AvaloniaProperty.RegisterDirect<MessageOverlayControl, bool>(nameof(IsDescriptionVisible), i => i.IsDescriptionVisible);

    public bool IsDetailsVisible => !string.IsNullOrWhiteSpace(Details);
    public static readonly DirectProperty<MessageOverlayControl, bool> IsDetailsVisibleProperty =
        AvaloniaProperty.RegisterDirect<MessageOverlayControl, bool>(nameof(IsDetailsVisible), i => i.IsDetailsVisible);

    public bool IsMessageVisible => IsHeadingVisible || IsDescriptionVisible || IsDetailsVisible;
    public static readonly DirectProperty<MessageOverlayControl, bool> IsMessageVisibleProperty =
        AvaloniaProperty.RegisterDirect<MessageOverlayControl, bool>(nameof(IsMessageVisible), i => i.IsMessageVisible);

    static MessageOverlayControl()
    {
        HorizontalAlignmentProperty.OverrideDefaultValue<MessageOverlayControl>(HorizontalAlignment.Center);
        VerticalAlignmentProperty.OverrideDefaultValue<MessageOverlayControl>(VerticalAlignment.Center);
        MarginProperty.OverrideDefaultValue<MessageOverlayControl>(new Thickness(20));
        PaddingProperty.OverrideDefaultValue<MessageOverlayControl>(new Thickness(10));
    }

    public MessageOverlayControl()
    {
        _lblHeading = new TextBlock
        {
            FontSize = Const.FONT_SIZE_SUBTITLE,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            [!TextBlock.ForegroundProperty] = Resx.CreateBinding(ResxId.SystemAccentColor),
        };
        _lblDescription = new SelectableTextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };
        _btnCopy = new PhButton
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Focusable = false,
            IconData = Resx.GetIcon(ResxIconId.IconCopy),
        };
        _btnCopy.Click += BtnCopy_Click;
        _lblDetails = new SelectableTextBlock
        {
            Padding = new Thickness(5),
            FontSize = Const.FONT_SIZE_SMALL,
            FontFamily = Const.FONT_CODE,
            FontWeight = Avalonia.Media.FontWeight.SemiLight,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        _detailsBox = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            [!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius),
            [!Border.BorderBrushProperty] = Resx.CreateBinding(ResxId.TextControlForeground),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _lblDetails,
            },
        };

        Content = CreateContentElement();

        PointerPressed += (_, e) => e.Handled = true;
        PointerReleased += (_, e) => e.Handled = true;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        _btnCopy.Text = Core.Lang[LangId._Copy];
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == HeadingProperty)
        {
            RaisePropertyChanged(IsHeadingVisibleProperty, default, IsHeadingVisible);
            RaisePropertyChanged(IsMessageVisibleProperty, default, IsMessageVisible);
        }
        else if (e.Property == DescriptionProperty)
        {
            RaisePropertyChanged(IsDescriptionVisibleProperty, default, IsDescriptionVisible);
            RaisePropertyChanged(IsMessageVisibleProperty, default, IsMessageVisible);
        }
        else if (e.Property == DetailsProperty)
        {
            RaisePropertyChanged(IsDetailsVisibleProperty, default, IsDetailsVisible);
            RaisePropertyChanged(IsMessageVisibleProperty, default, IsMessageVisible);
        }
        else
        {
            return;
        }

        if (_isSettingMessage) return;
        ApplyMessage();
    }

    private Grid CreateContentElement()
    {
        var topEl = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            Orientation = Orientation.Vertical,
            Spacing = 12,
        };
        topEl.Children.Add(_lblHeading);
        topEl.Children.Add(_lblDescription);
        topEl.Children.Add(_btnCopy);

        Grid.SetRow(topEl, 0);
        Grid.SetRow(_detailsBox, 1);

        var rootEl = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto, *"),
        };
        rootEl.Children.Add(topEl);
        rootEl.Children.Add(_detailsBox);

        return rootEl;
    }

    private void BtnCopy_Click(object? sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { Heading, Description, Details }.Where(i => !string.IsNullOrWhiteSpace(i)));

        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
    }

    private void ApplyMessage()
    {
        var isVisible = IsMessageVisible;
        if (isVisible)
        {
            _lblHeading.Text = Heading;
            _lblHeading.IsVisible = IsHeadingVisible;
            _lblDescription.Text = Description;
            _lblDescription.IsVisible = IsDescriptionVisible;
            _lblDetails.Text = Details;
            _detailsBox.IsVisible = IsDetailsVisible;
            _btnCopy.IsVisible = IsDetailsVisible;
        }

        IsHitTestVisible = isVisible && IsDetailsVisible;
        IsShown = isVisible;
    }

    private void SetMessage(string? message, string? heading = null, string? details = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isSettingMessage = true;
            try
            {
                Heading = heading;
                Description = message;
                Details = details;
            }
            finally
            {
                _isSettingMessage = false;
            }

            ApplyMessage();
        }, DispatcherPriority.Render);
    }

    public async Task ShowAsync(string? message, string? heading = null, string? details = null,
        int? durationMs = null, int delayMs = 0)
    {
        var clearMessage = message == null && heading == null && details == null;
        if (clearMessage)
        {
            lock (_lock)
            {
                _cancelMessage?.Cancel();
                _cancelMessage?.Dispose();
                _cancelMessage = null;
            }

            SetMessage(null);
            return;
        }

        CancellationTokenSource localCancelMessage;
        lock (_lock)
        {
            _cancelMessage?.Cancel();
            _cancelMessage?.Dispose();

            _cancelMessage = new CancellationTokenSource();
            localCancelMessage = _cancelMessage;
        }

        var token = localCancelMessage.Token;

        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, token);
            }

            if (token.IsCancellationRequested) return;

            SetMessage(message, heading, details);

            if (!string.IsNullOrWhiteSpace(message))
            {
                durationMs ??= Core.Config.InAppMessageDuration;

                if (durationMs > 0)
                {
                    await Task.Delay(durationMs.Value, token);
                    SetMessage(null);
                }
            }
        }
        catch { }
    }

    public async Task ClearAsync()
    {
        await ShowAsync(null);
    }

}
