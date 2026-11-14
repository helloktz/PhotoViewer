using System.Collections.Generic;

namespace PhotoViewer.Common.Printing;

public sealed class PrintConfig
{
    private static PrintConfig? _saved;

    public string? LastPrinterId { get; set; }

    public Dictionary<string, string> PaperByPrinter { get; set; } = [];

    public Dictionary<string, int> DpiByPrinter { get; set; } = [];

    public string LayoutId { get; set; } = PrintLayouts.FULL_PAGE_ID;
    public PrintFitMode Fit { get; set; } = PrintFitMode.Fit;
    public PrintOrientation Orientation { get; set; } = PrintOrientation.Auto;
    public PrintMarginPreset Margins { get; set; } = PrintMarginPreset.Normal;
    public bool AutoRotate { get; set; } = true;
    public bool ShowCaptions { get; set; }
    public bool FillPage { get; set; }
    public bool Collate { get; set; } = true;
    public PrintColorMode ColorMode { get; set; } = PrintColorMode.Color;
    public PrintDuplex Duplex { get; set; } = PrintDuplex.None;

    public static PrintConfig Load() => _saved?.Clone() ?? new PrintConfig();

    public void Save() => _saved = Clone();

    private PrintConfig Clone()
    {
        var copy = (PrintConfig)MemberwiseClone();
        copy.PaperByPrinter = new(PaperByPrinter);
        copy.DpiByPrinter = new(DpiByPrinter);

        return copy;
    }
}
