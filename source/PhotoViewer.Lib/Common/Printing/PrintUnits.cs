using PhotoViewer.Common.Types;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoViewer.Common.Printing;

public static class PrintUnits
{
    public const float POINTS_PER_INCH = 72f;
    public const double MM_PER_INCH = 25.4;

    public static bool IsMetricRegion { get; } = GetIsMetricRegion();

    public static float MmToPt(double mm) => (float)(mm / MM_PER_INCH * POINTS_PER_INCH);
    public static float InToPt(double inches) => (float)(inches * POINTS_PER_INCH);
    public static double PtToMm(double pt) => pt / POINTS_PER_INCH * MM_PER_INCH;
    public static double PtToIn(double pt) => pt / POINTS_PER_INCH;
    public static float PtToPx(float pt, float dpi) => pt * dpi / POINTS_PER_INCH;

    public static string FormatSize(SKSize sizePt, bool metric)
    {
        return metric
            ? $"{FormatNumber(PtToMm(sizePt.Width), 0)}×{FormatNumber(PtToMm(sizePt.Height), 0)} mm"
            : $"{FormatNumber(PtToIn(sizePt.Width), 2)}×{FormatNumber(PtToIn(sizePt.Height), 2)} in";
    }

    private static string FormatNumber(double value, int decimals)
    {
        return Math.Round(value, decimals).ToString("0.##", CultureInfo.CurrentCulture);
    }

    private static bool GetIsMetricRegion()
    {
        try
        {
            if (BHelper.OS == OSType.Linux)
            {
                var locale = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } all ? all
                    : Environment.GetEnvironmentVariable("LC_MEASUREMENT") is { Length: > 0 } measurement ? measurement
                    : Environment.GetEnvironmentVariable("LANG");

                var name = locale?.Split('.', '@')[0].Replace('_', '-');
                return string.IsNullOrEmpty(name) || name is "C" or "POSIX" || new RegionInfo(name).IsMetric;
            }

            return RegionInfo.CurrentRegion.IsMetric;
        }
        catch
        {
            return true;
        }
    }
}

public static partial class PaperCatalog
{

    public static IReadOnlyList<PaperInfo> Papers { get; } =
    [
        Iso("A3", 297, 420),
        Iso("A4", 210, 297),
        Iso("A5", 148, 210),
        Iso("A6", 105, 148),
        Iso("B5", 176, 250),
        Us("Letter", 8.5, 11),
        Us("Legal", 8.5, 14),
        Us("Tabloid", 11, 17),
        Us("4×6 in", 4, 6, "photo-4x6in"),
        Us("5×7 in", 5, 7, "photo-5x7in"),
        Us("8×10 in", 8, 10, "photo-8x10in"),
        Iso("10×15 cm", 100, 150, "photo-10x15cm"),
        Iso("13×18 cm", 130, 180, "photo-13x18cm"),
    ];

    public static string DefaultPaperId => PrintUnits.IsMetricRegion ? "A4" : "Letter";

    public static string GetDisplayName(string name, SKSize sizePt)
    {
        if (!DimensionRegex().IsMatch(name))
        {
            var sizeText = PrintUnits.FormatSize(sizePt, PrintUnits.IsMetricRegion);
            var qualifier = QualifierRegex().Match(name);
            return qualifier.Success
                ? $"{qualifier.Groups[1].Value}({sizeText}, {qualifier.Groups[2].Value})"
                : $"{name} ({sizeText})";
        }

        var size = DimensionRegex().Replace(name, "$1×$2");
        size = InchMarkRegex().Replace(size, "$1 in");
        size = UnitRegex().Replace(size, "$1 $2");

        var match = SizeRegex().Match(size);
        if (match.Success && !match.Groups[3].Success && GetUnit(match.Groups[1].Value, match.Groups[2].Value, sizePt) is { } unit)
        {
            size = size.Insert(match.Index + match.Length, " " + unit);
        }

        return size;
    }

    private static string? GetUnit(string a, string b, SKSize sizePt)
    {
        if (!double.TryParse(a.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(b.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return null;

        var (small, large) = (Math.Min(x, y), Math.Max(x, y));
        var (w, h) = (Math.Min(sizePt.Width, sizePt.Height), Math.Max(sizePt.Width, sizePt.Height));

        bool Matches(double perPoint, double tolerance) => Math.Abs(small - w * perPoint) <= tolerance && Math.Abs(large - h * perPoint) <= tolerance;

        if (Matches(1 / PrintUnits.POINTS_PER_INCH, 0.06)) return "in";
        if (Matches(PrintUnits.MM_PER_INCH / PrintUnits.POINTS_PER_INCH, 1.5)) return "mm";
        if (Matches(PrintUnits.MM_PER_INCH / PrintUnits.POINTS_PER_INCH / 10, 0.15)) return "cm";
        return null;
    }

    [GeneratedRegex(@"(\d)\s*[x×]\s*(\d)", RegexOptions.IgnoreCase)]
    private static partial Regex DimensionRegex();

    [GeneratedRegex(@"(\d)\s*(mm|cm|in)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnitRegex();

    [GeneratedRegex(@"(\d)\s*[″""”]")]
    private static partial Regex InchMarkRegex();

    [GeneratedRegex(@"^(.+?\s*)\(([^()]+)\)$")]
    private static partial Regex QualifierRegex();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)×(\d+(?:[.,]\d+)?)(\s(?:mm|cm|in)\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    private static PaperInfo Iso(string name, double widthMm, double heightMm, string? id = null)
    {
        var size = new SKSize(PrintUnits.MmToPt(widthMm), PrintUnits.MmToPt(heightMm));
        return new PaperInfo(id ?? name, GetDisplayName(name, size), size);
    }

    private static PaperInfo Us(string name, double widthIn, double heightIn, string? id = null)
    {
        var size = new SKSize(PrintUnits.InToPt(widthIn), PrintUnits.InToPt(heightIn));
        return new PaperInfo(id ?? name, GetDisplayName(name, size), size);
    }
}
