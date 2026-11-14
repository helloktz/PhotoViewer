using SkiaSharp;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PhotoViewer.Common.Printing;

public enum PrintLayoutKind
{
    FullPage,
    Grid,
    FixedSize,
    Wallet,
    Passport,
    ContactSheet,
}

public sealed record PrintLayout
{

    public required string Id { get; init; }

    public required PrintLayoutKind Kind { get; init; }

    public int CellCount { get; init; } = 1;

    public SKSize PrintSizePt { get; init; }

    public (int Columns, int Rows) Grid { get; init; } = (1, 1);

    public string? FixedName { get; init; }
}

public static class PrintLayouts
{
    public const string FULL_PAGE_ID = "full";

    public static PrintLayout FullPage { get; } = new() { Id = FULL_PAGE_ID, Kind = PrintLayoutKind.FullPage };

    public static IReadOnlyList<PrintLayout> GetAll(bool metric)
    {
        var list = new List<PrintLayout> { FullPage, GridOf(2), GridOf(4), GridOf(6), GridOf(9) };

        if (metric)
        {
            list.Add(FixedCm(9, 13));
            list.Add(FixedCm(10, 15));
            list.Add(FixedCm(13, 18));
            list.Add(FixedCm(20, 25));
        }
        else
        {
            list.Add(FixedIn(3.5, 5));
            list.Add(FixedIn(4, 6));
            list.Add(FixedIn(5, 7));
            list.Add(FixedIn(8, 10));
        }

        list.Add(new() { Id = "wallet", Kind = PrintLayoutKind.Wallet, PrintSizePt = new(PrintUnits.InToPt(2.5), PrintUnits.InToPt(3.5)) });
        list.Add(metric
            ? new() { Id = "passport-35x45mm", Kind = PrintLayoutKind.Passport, PrintSizePt = new(PrintUnits.MmToPt(35), PrintUnits.MmToPt(45)), FixedName = "35×45\u00A0mm" }
            : new() { Id = "passport-2x2in", Kind = PrintLayoutKind.Passport, PrintSizePt = new(PrintUnits.InToPt(2), PrintUnits.InToPt(2)), FixedName = "2×2\u00A0in" });

        list.Add(new() { Id = "contact", Kind = PrintLayoutKind.ContactSheet, Grid = (5, 7) });
        return list;
    }

    public static PrintLayout Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return FullPage;

        return GetAll(true).Concat(GetAll(false)).FirstOrDefault(i => i.Id == id) ?? FullPage;
    }

    private static PrintLayout GridOf(int count) => new() { Id = $"grid-{count}", Kind = PrintLayoutKind.Grid, CellCount = count };

    private static PrintLayout FixedCm(double w, double h) => new()
    {
        Id = string.Create(CultureInfo.InvariantCulture, $"fixed-{w}x{h}cm"),
        Kind = PrintLayoutKind.FixedSize,
        PrintSizePt = new(PrintUnits.MmToPt(w * 10), PrintUnits.MmToPt(h * 10)),
        FixedName = $"{w}×{h}\u00A0cm",
    };

    private static PrintLayout FixedIn(double w, double h) => new()
    {
        Id = string.Create(CultureInfo.InvariantCulture, $"fixed-{w}x{h}in"),
        Kind = PrintLayoutKind.FixedSize,
        PrintSizePt = new(PrintUnits.InToPt(w), PrintUnits.InToPt(h)),
        FixedName = $"{w}×{h}\u00A0in",
    };
}
