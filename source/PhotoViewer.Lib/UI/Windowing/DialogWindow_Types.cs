using System;

namespace PhotoViewer.UI.Windowing;

public enum DialogExitCode
{

    None = 0,

    OK = 1,

    Cancel = 2,

    Abort = 3,
}

public enum DialogButton
{
    Button1,
    Button2,
    Button3,

    None,
}

public enum DialogAction
{
    Submit,
    Cancel,
    Apply,
}

public enum DialogFocus
{
    Default,
    Button1,
    Button2,
    Button3,
}

public class DialogEventArgs(DialogAction actionType) : EventArgs
{
    public DialogAction ActionType => actionType;
    public bool CanProceed { get; set; } = true;
}
