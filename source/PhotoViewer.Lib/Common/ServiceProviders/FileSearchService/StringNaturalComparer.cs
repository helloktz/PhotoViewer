
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PhotoViewer.Common.ServiceProviders.FileSearchService;

public sealed class StringNaturalComparer(
    bool orderByAsc = true,
    StringComparison compareMode = StringComparison.Ordinal,
    bool compareExtensionLast = false,
    CompareInfo? collation = null) : IComparer<string?>, IComparer<ReadOnlyMemory<char>>
{
    private static readonly bool _isNumericCollationSupported =
        CultureInfo.InvariantCulture.CompareInfo.Compare("a2", "a10", CompareOptions.NumericOrdering) < 0;

    public bool OrderByAsc { get; set; } = orderByAsc;

    public StringComparison CompareMode { get; set; } = compareMode;

    public bool CompareExtensionLast { get; set; } = compareExtensionLast;

    public CompareInfo? Collation { get; set; } = collation;

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;

        if (OrderByAsc)
        {
            if (x is null) return -1;
            if (y is null) return 1;

            return Compare__(x.AsSpan(), y.AsSpan());
        }

        if (x is null) return 1;
        if (y is null) return -1;

        return Compare__(y.AsSpan(), x.AsSpan());
    }

    public int Compare(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        if (OrderByAsc)
        {
            return Compare__(x, y);
        }

        return Compare__(y, x);
    }

    public int Compare(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        if (OrderByAsc)
        {
            return Compare__(x.Span, y.Span);
        }

        return Compare__(y.Span, x.Span);
    }

    private int Compare__(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        var compareResult = CompareExtensionLast
            ? CompareFileName(x, y, CompareMode, Collation)
            : Compare(x, y, CompareMode, Collation);

        if (compareResult == 0 && Collation is not null)
        {
            compareResult = Compare(x, y, StringComparison.Ordinal);
        }

        return compareResult;
    }

    public static int Compare(ReadOnlySpan<char> x, ReadOnlySpan<char> y, StringComparison stringComparison, CompareInfo? collation = null)
    {
        if (collation is not null && _isNumericCollationSupported)
        {
            var ignoreCase = stringComparison is StringComparison.OrdinalIgnoreCase
                or StringComparison.CurrentCultureIgnoreCase
                or StringComparison.InvariantCultureIgnoreCase;

            return collation.Compare(x, y, CompareOptions.NumericOrdering
                | (ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None));
        }

        var length = Math.Min(x.Length, y.Length);

        for (var i = 0; i < length; i++)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[i]))
            {
                var xOut = GetNumber(x.Slice(i), out var xNumAsSpan);
                var yOut = GetNumber(y.Slice(i), out var yNumAsSpan);

                var compareResult = CompareNumValues(xNumAsSpan, yNumAsSpan);

                if (compareResult != 0)
                {
                    return compareResult;
                }

                i = -1;
                length = Math.Min(xOut.Length, yOut.Length);

                x = xOut;
                y = yOut;
                continue;
            }

            var charCompareResult = x.Slice(i, 1).CompareTo(y.Slice(i, 1), stringComparison);
            if (charCompareResult != 0)
            {
                return charCompareResult;
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    public static int CompareFileName(ReadOnlySpan<char> x, ReadOnlySpan<char> y, StringComparison stringComparison, CompareInfo? collation = null)
    {
        var xDotIndex = FindExtensionSeparator(x);
        var yDotIndex = FindExtensionSeparator(y);
        var xBaseLength = xDotIndex < 0 ? x.Length : xDotIndex;
        var yBaseLength = yDotIndex < 0 ? y.Length : yDotIndex;

        var compareResult = Compare(x.Slice(0, xBaseLength), y.Slice(0, yBaseLength), stringComparison, collation);
        if (compareResult != 0 || (xDotIndex < 0 && yDotIndex < 0))
        {
            return compareResult;
        }

        return Compare(x.Slice(xBaseLength), y.Slice(yBaseLength), stringComparison, collation);
    }

    private static int FindExtensionSeparator(ReadOnlySpan<char> fileName)
    {
        for (var i = fileName.Length - 1; i > 0; i--)
        {
            if (fileName[i] != '.') continue;

            if (char.IsAsciiDigit(fileName[i - 1])
                && i + 1 < fileName.Length
                && char.IsAsciiDigit(fileName[i + 1])) continue;

            return i;
        }

        return -1;
    }

    private static ReadOnlySpan<char> GetNumber(ReadOnlySpan<char> span, out ReadOnlySpan<char> number)
    {
        var i = 0;
        while (i < span.Length && char.IsDigit(span[i]))
        {
            i++;
        }

        number = span.Slice(0, i);
        return span.Slice(i);
    }

    private static int CompareNumValues(ReadOnlySpan<char> numValue1, ReadOnlySpan<char> numValue2)
    {
        var num1AsSpan = numValue1.TrimStart('0');
        var num2AsSpan = numValue2.TrimStart('0');

        if (num1AsSpan.Length < num2AsSpan.Length)
        {
            return -1;
        }

        if (num1AsSpan.Length > num2AsSpan.Length)
        {
            return 1;
        }

        var compareResult = num1AsSpan.CompareTo(num2AsSpan, StringComparison.Ordinal);

        if (compareResult != 0)
        {
            return Math.Sign(compareResult);
        }

        if (numValue2.Length == numValue1.Length)
        {
            return compareResult;
        }

        return numValue2.Length < numValue1.Length ? -1 : 1;
    }

}
