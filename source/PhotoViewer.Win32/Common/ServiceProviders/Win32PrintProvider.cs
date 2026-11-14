using PhotoViewer.Common;
using PhotoViewer.Common.Printing;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Win32.Common.ServiceProviders;

public class Win32PrintProvider : PrintProviderBase
{

    protected override Task<IReadOnlyList<PrinterInfo>> GetSystemPrintersAsync(CancellationToken token)
    {
        return Task.Run<IReadOnlyList<PrinterInfo>>(Win32PrinterApi.GetPrinters, token).WaitAsync(token);
    }

    protected override Task<PrinterCapabilities> GetSystemCapabilitiesAsync(PrinterInfo printer, CancellationToken token)
    {
        return Task.Run(() => Win32PrinterApi.GetCapabilities(printer.Id, token), token).WaitAsync(token);
    }

    protected override Task<PrinterStatus> GetSystemStatusAsync(PrinterInfo printer, CancellationToken token)
    {
        return Task.Run(() => Win32PrinterApi.GetStatus(printer.Id), token).WaitAsync(token);
    }

    protected override Task<PaperInfo> MeasureSystemPaperAsync(PrinterInfo printer, PaperInfo paper, CancellationToken token)
    {
        return Task.Run(() => Win32PrinterApi.MeasurePaper(printer.Id, paper), token).WaitAsync(token);
    }

    protected override Task<bool> ShowSystemPropertiesDialogAsync(PhWindow owner, PrintJobSettings settings)
    {
        var handle = owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        return Task.FromResult(Win32PrinterApi.ShowPropertiesDialog(handle, settings));
    }

    protected override async Task PrintToSystemAsync(PrintJob job, IProgress<PrintProgress>? progress, CancellationToken token)
    {
        var settings = job.Settings;
        var name = settings.Printer.Id;

        var copies = Math.Max(1, settings.Copies);
        var (maxCopies, canCollate) = Win32PrinterApi.GetCopySupport(name);
        var isDriverCopies = copies <= maxCopies && (!settings.Collate || canCollate || job.Layout.Pages.Count == 1);

        var devMode = Win32PrinterApi.BuildDevMode(name, settings, isDriverCopies ? copies : 1);
        await Win32GdiPrintJob.RunAsync(job, devMode, isDriverCopies ? 1 : copies, progress, token).ConfigureAwait(false);
    }

    public override bool CanShowSystemDialog => true;

    public override async Task<bool> ShowSystemDialogAsync(PhWindow owner, PrintJob job, CancellationToken token)
    {
        var path = await WriteShownImagePngAsync(job, token);
        if (string.IsNullOrEmpty(path)) return false;

        Win32PrintApi.OpenPrintDialog(BHelper.GetRealPlatformPath(path));
        return true;
    }

    public override bool CanAddPrinter => true;

    public override Task OpenAddPrinterSettingsAsync(PhWindow owner)
    {
        return BHelper.OpenSystemSettingsAsync(owner, "ms-settings:printers");
    }
}
