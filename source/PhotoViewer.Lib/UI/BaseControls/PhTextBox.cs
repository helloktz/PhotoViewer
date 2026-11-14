using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PhotoViewer.UI;

public partial class PhTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    private static Regex Regex_IntValueOnly => new Regex(
            pattern: $"^[{Const.SIGN_POSITIVE}{Const.SIGN_NEGATIVE}]?[0-9]+$",
            options: RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static Regex Regex_FloatValueOnly => new Regex(
            pattern: $"^([0-9]+([{Const.DECIMAL_SEPARATOR}][0-9]*)?|[{Const.DECIMAL_SEPARATOR}][0-9]+)$",
            options: RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static Regex Regex_UnsignedFloatValueOnly => new Regex(
            pattern: $"^[{Const.SIGN_POSITIVE}]?([0-9]+([{Const.DECIMAL_SEPARATOR}][0-9]*)?|[{Const.DECIMAL_SEPARATOR}][0-9]+)$",
            options: RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [GeneratedRegex("^[0-9]+$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex Regex_UnsignedIntValueOnly();

    [GeneratedRegex(@"^\s*\.?[A-Za-z0-9]+\s*$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex Regex_FileExtensionValueOnly();

    [GeneratedRegex(@"^\s*\.(\*|[A-Za-z0-9]+)(\s*;\s*\.(\*|[A-Za-z0-9]+))*\s*$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex Regex_FileExtensionsValueOnly();

    public TextBoxAcceptValue AcceptValue
    {
        get => GetValue(AcceptValueProperty);
        set => SetValue(AcceptValueProperty, value);
    }
    public static readonly StyledProperty<TextBoxAcceptValue> AcceptValueProperty =
        AvaloniaProperty.Register<PhTextBox, TextBoxAcceptValue>(nameof(AcceptValue), TextBoxAcceptValue.Any);

    public bool IsRequired
    {
        get => GetValue(IsRequiredProperty);
        set
        {
            SetValue(IsRequiredProperty, value);
            ValidateAndShowError();
        }
    }
    public static readonly StyledProperty<bool> IsRequiredProperty =
        AvaloniaProperty.Register<PhTextBox, bool>(nameof(IsRequired));

    public string? RegexPattern
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            ValidateAndShowError();
        }
    }

    public bool ValidateByPressingEnter { get; set; } = true;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        TextChanged += PhTextBox_TextChanged;
        Core.LanguageChanged += Core_LanguageChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        TextChanged -= PhTextBox_TextChanged;
        Core.LanguageChanged -= Core_LanguageChanged;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (AcceptsReturn) return;

        if (ValidateByPressingEnter && e.Key == Key.Enter)
        {
            var isValid = ValidateAndShowError();
            if (!isValid) _ = AnimateValidationErrorAsync();
        }
    }

    private void PhTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ValidateAndShowError();
    }

    private void Core_LanguageChanged(object? sender, EventArgs e)
    {
        ValidateAndShowError();
    }

    protected (bool IsValid, ValidationException? Error) Validate()
    {
        ValidationException? error = null;
        var isValid = true;
        var textValue = Text ?? string.Empty;
        var isEmpty = string.IsNullOrEmpty(textValue);

        if (isEmpty)
        {
            isValid = !IsRequired;
            if (!isValid) error = new ValidationException(Core.Lang[LangId._Validation_Required]);
        }
        else
        {
            switch (AcceptValue)
            {
                case TextBoxAcceptValue.RegexPattern:
                    if (!string.IsNullOrEmpty(RegexPattern))
                    {
                        isValid = Regex.IsMatch(textValue, RegexPattern);
                        if (!isValid) error = new ValidationException(Core.Lang[LangId._Validation_RegexPattern]);
                    }
                    break;

                case TextBoxAcceptValue.IntValueOnly:
                    isValid = Regex_IntValueOnly.IsMatch(textValue);
                    if (!isValid) error = new ValidationException(Core.Lang[LangId._Validation_IntValueOnly]);
                    break;

                case TextBoxAcceptValue.UnsignedIntValueOnly:
                    isValid = Regex_UnsignedIntValueOnly().IsMatch(textValue);
                    if (!isValid) error = new ValidationException(Core.Lang[LangId._Validation_UnsignedIntValueOnly]);
                    break;

                case TextBoxAcceptValue.FloatValueOnly:
                    isValid = Regex_FloatValueOnly.IsMatch(textValue);
                    if (!isValid) error = new ValidationException(Core.Lang[LangId._Validation_FloatValueOnly]);
                    break;

                case TextBoxAcceptValue.UnsignedFloatValueOnly:
                    isValid = Regex_UnsignedFloatValueOnly.IsMatch(textValue);
                    if (!isValid) error = new ValidationException(Core.Lang[LangId._Validation_UnsignedFloatValueOnly]);
                    break;

                case TextBoxAcceptValue.FileExtensionValueOnly:
                    isValid = Regex_FileExtensionValueOnly().IsMatch(textValue);
                    if (!isValid) error = new ValidationException(Core.Lang[LangId._Validation_FileExtensionValueOnly]);
                    break;

                case TextBoxAcceptValue.FileExtensionsValueOnly:
                    isValid = Regex_FileExtensionsValueOnly().IsMatch(textValue);
                    if (!isValid) error = new ValidationException(Core.Lang[LangId._Validation_FileExtensionsValueOnly]);
                    break;

                case TextBoxAcceptValue.FileNameValueOnly:
                    var badChars = Path.GetInvalidFileNameChars();

                    foreach (var c in badChars.AsSpan())
                    {
                        if (textValue.Contains(c))
                        {
                            isValid = false;
                            if (!isValid) error = new ValidationException(Core.Lang[LangId._Validation_FileNameValueOnly]);
                            break;
                        }
                    }
                    break;

                case TextBoxAcceptValue.FilePathValueOnly:
                    foreach (var c in Path.GetInvalidPathChars().AsSpan())
                    {
                        if (textValue.Contains(c))
                        {
                            isValid = false;
                            error = new ValidationException(Core.Lang[LangId._Validation_FilePathValueOnly]);
                            break;
                        }
                    }
                    break;

                default:
                    break;
            }
        }

        return (IsValid: isValid, Error: error);
    }

    public bool ValidateAndShowError()
    {
        DataValidationErrors.ClearErrors(this);

        var result = Validate();
        if (!result.IsValid)
        {
            DataValidationErrors.SetError(this, result.Error);
        }

        return result.IsValid;
    }

    public async Task AnimateValidationErrorAsync()
    {
        var distance = 5;
        var duration = 30;

        await AnimateMarginAsync(this,
                new Thickness(Margin.Left + distance, Margin.Top, Margin.Right, Margin.Bottom),
                duration);
        await AnimateMarginAsync(this,
                new Thickness(Margin.Left - distance, Margin.Top, Margin.Right, Margin.Bottom),
                duration);
        await AnimateMarginAsync(this, Margin, duration);
    }

    private static async Task AnimateMarginAsync(Control control, Thickness toMargin, int durationMs)
    {
        var fromMargin = control.Margin;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters = { new Setter(Layoutable.MarginProperty, fromMargin) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters = { new Setter(Layoutable.MarginProperty, toMargin) }
                }
            }
        };

        await animation.RunAsync(control);
    }

}

public enum TextBoxAcceptValue
{
    Any,
    RegexPattern,
    IntValueOnly,
    UnsignedIntValueOnly,
    FloatValueOnly,
    UnsignedFloatValueOnly,
    FileNameValueOnly,
    FilePathValueOnly,
    FileExtensionValueOnly,
    FileExtensionsValueOnly,
}
