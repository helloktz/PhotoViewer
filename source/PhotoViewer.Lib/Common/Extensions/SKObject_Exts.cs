using SkiaSharp;
using System;
using System.Diagnostics.CodeAnalysis;

namespace PhotoViewer.Common.Extensions;

public static class SKObject_Exts
{

    public static bool IsDisposed([NotNullWhen(false)] this SKObject? obj)
    {
        return obj == null || obj.Handle == IntPtr.Zero;
    }
}
