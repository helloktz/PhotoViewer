using Avalonia.Media;
using PhotoViewer.Common.Types;

namespace PhotoViewer.UI.Windowing;

public enum InfoBarSeverity
{
    Info,
    Success,
    Warning,
    Danger,
}

public enum ModalWindowButton
{
    OK,
    Close,
    Yes_No,
    OK_Cancel,
    OK_Close,
    LearnMore_Close,
    Continue_Quit,
}

public class ModalWindowResult
{

    public DialogExitCode ExitCode { get; internal set; } = DialogExitCode.None;

    public string InputValue { get; internal set; } = "";

    public bool IsRememberOptionChecked { get; internal set; } = false;
}

public record ModalWindowOptions
{
    public string? Title { get; set; }
    public string? Heading { get; set; }
    public string? Description { get; set; }
    public string? Details { get; set; }
    public string? Note { get; set; }
    public InfoBarSeverity? NoteStyle { get; set; }
    public IImage? Thumbnail { get; set; }
    public StockIconId? ThumbnailIcon { get; set; }
    public bool IsRememberOptionVisible { get; set; }
    public bool? IsInputVisible { get; set; }
    public string InputValue { get; set; } = string.Empty;
    public string? InputPlaceholder { get; set; }
    public TextBoxAcceptValue AcceptValue { get; set; } = TextBoxAcceptValue.Any;
    public bool? ShowInTaskbar { get; set; }
}
