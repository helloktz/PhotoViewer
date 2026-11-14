using System;

namespace PhotoViewer.Common.Types;

public class ValidationException(string? message) : Exception(message)
{
    public override string ToString() => Message;
}
