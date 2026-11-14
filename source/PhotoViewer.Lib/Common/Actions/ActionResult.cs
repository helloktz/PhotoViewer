using System;

namespace PhotoViewer.Common.Actions;

public class ActionResult(ActionExitCode exitCode, Exception? error = null)
{
    public ActionExitCode ExitCode { get; set; } = exitCode;
    public Exception? Error { get; set; } = error;
}

public enum ActionExitCode
{
    Success,
    Error,
    Cancelled,
    ApiNotFound,
    Locked,
}
