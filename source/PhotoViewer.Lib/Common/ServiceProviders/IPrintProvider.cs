using PhotoViewer.Common.Printing;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public interface IPrintProvider
{

    Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken token);

    string? SystemPrintersUnavailableReason { get; }

    Task<PrinterCapabilities> GetCapabilitiesAsync(PrinterInfo printer, CancellationToken token);

    Task<PrinterStatus> GetStatusAsync(PrinterInfo printer, CancellationToken token);

    Task<PaperInfo> MeasurePaperAsync(PrinterInfo printer, PaperInfo paper, CancellationToken token);

    Task<bool> ShowPropertiesDialogAsync(PhWindow owner, PrintJobSettings settings);

    Task PrintAsync(PrintJob job, IProgress<PrintProgress>? progress, CancellationToken token);

    bool CanShowSystemDialog { get; }

    Task<bool> ShowSystemDialogAsync(PhWindow owner, PrintJob job, CancellationToken token);

    bool CanAddPrinter { get; }

    Task OpenAddPrinterSettingsAsync(PhWindow owner);
}

public sealed class PrintJob
{
    public required PrintJobSettings Settings { get; init; }
    public required string Title { get; init; }
    public required PrintDocumentLayout Layout { get; init; }
    public required PrintSession Session { get; init; }
    public required PrintRenderOptions Render { get; init; }
}
