using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Printing;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Windowing;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.ServiceProviders;

public abstract class PrintProviderBase : IPrintProvider
{

    public const string PDF_PRINTER_ID = "ig:pdf";

    public static PrinterInfo SaveAsPdfPrinter => new(PDF_PRINTER_ID, Core.Lang[LangId.Print_SaveAsPdf]) { IsVirtual = true, OutputFileExtension = ".pdf" };

    public static PrinterCapabilities PdfCapabilities { get; } = new()
    {
        Papers = PaperCatalog.Papers,
        DefaultPaperId = PaperCatalog.DefaultPaperId,
        SupportsColor = true,
        MaxCopies = 1,
        ResolutionsDpi = [150, 300, 600],
        DefaultDpi = 300,
    };

    public async Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken token)
    {
        var printers = new List<PrinterInfo>();

        try
        {
            printers.AddRange(await GetSystemPrintersAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(PrintProviderBase)}.{nameof(GetPrintersAsync)}: {ex.Message}");
        }

        printers.Add(SaveAsPdfPrinter);
        return printers;
    }

    public virtual string? SystemPrintersUnavailableReason => null;

    public Task<PrinterCapabilities> GetCapabilitiesAsync(PrinterInfo printer, CancellationToken token)
    {
        return printer.Id == PDF_PRINTER_ID
            ? Task.FromResult(PdfCapabilities)
            : GetSystemCapabilitiesAsync(printer, token);
    }

    public Task<PrinterStatus> GetStatusAsync(PrinterInfo printer, CancellationToken token)
    {
        return printer.Id == PDF_PRINTER_ID
            ? Task.FromResult(new PrinterStatus(PrinterState.Ready))
            : GetSystemStatusAsync(printer, token);
    }

    public Task<PaperInfo> MeasurePaperAsync(PrinterInfo printer, PaperInfo paper, CancellationToken token)
    {
        return paper.IsPrintableAreaMeasured || printer.Id == PDF_PRINTER_ID
            ? Task.FromResult(paper)
            : MeasureSystemPaperAsync(printer, paper, token);
    }

    public Task<bool> ShowPropertiesDialogAsync(PhWindow owner, PrintJobSettings settings)
    {
        return settings.Printer.Id == PDF_PRINTER_ID
            ? Task.FromResult(false)
            : ShowSystemPropertiesDialogAsync(owner, settings);
    }

    public Task PrintAsync(PrintJob job, IProgress<PrintProgress>? progress, CancellationToken token)
    {
        if (job.Settings.Printer.Id != PDF_PRINTER_ID) return PrintToSystemAsync(job, progress, token);

        var path = job.Settings.OutputPath;
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Save as PDF needs a file path.");

        return SaveAsPdfAsync(job, path, false, progress, token);
    }

    public virtual bool CanShowSystemDialog => false;

    public virtual Task<bool> ShowSystemDialogAsync(PhWindow owner, PrintJob job, CancellationToken token) => Task.FromResult(false);

    public virtual bool CanAddPrinter => false;

    public virtual Task OpenAddPrinterSettingsAsync(PhWindow owner) => Task.CompletedTask;

    protected abstract Task<IReadOnlyList<PrinterInfo>> GetSystemPrintersAsync(CancellationToken token);

    protected abstract Task<PrinterCapabilities> GetSystemCapabilitiesAsync(PrinterInfo printer, CancellationToken token);

    protected virtual Task<PrinterStatus> GetSystemStatusAsync(PrinterInfo printer, CancellationToken token)
    {
        return Task.FromResult(new PrinterStatus(PrinterState.Unknown));
    }

    protected virtual Task<PaperInfo> MeasureSystemPaperAsync(PrinterInfo printer, PaperInfo paper, CancellationToken token)
    {
        return Task.FromResult(paper with { IsPrintableAreaMeasured = true });
    }

    protected virtual Task<bool> ShowSystemPropertiesDialogAsync(PhWindow owner, PrintJobSettings settings) => Task.FromResult(false);

    protected abstract Task PrintToSystemAsync(PrintJob job, IProgress<PrintProgress>? progress, CancellationToken token);

    protected static async Task SaveAsPdfAsync(PrintJob job, string path, bool portraitPagesOnly,
        IProgress<PrintProgress>? progress, CancellationToken token)
    {
        var tempPath = $"{path}.{Environment.ProcessId}.tmp";

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var options = new PdfWriteOptions
                {
                    Title = job.Title,
                    Dpi = job.Settings.Dpi,
                    PortraitPagesOnly = portraitPagesOnly,
                    Render = job.Render with { ColorMode = job.Settings.ColorMode },
                };

                await PdfPrintWriter.WriteAsync(stream, job.Layout, job.Session, options, progress, token).ConfigureAwait(false);
            }

            File.Move(tempPath, path, true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    protected static async Task<string> WriteTempPdfAsync(PrintJob job, bool portraitPagesOnly,
        IProgress<PrintProgress>? progress, CancellationToken token)
    {
        var path = BHelper.ConfigDir(Dir.Temporary, $"ig_print_{Guid.NewGuid():N}.pdf");
        await SaveAsPdfAsync(job, path, portraitPagesOnly, progress, token).ConfigureAwait(false);

        return path;
    }

    protected static async Task<string?> WriteShownImagePngAsync(PrintJob job, CancellationToken token)
    {
        var region = job.Session.Items.FirstOrDefault()?.Region ?? PrintRegion.WholeImage;
        using var img = await job.Session.BuildFullImageAsync(job.Session.State.FrameIndex, region, token).ConfigureAwait(false);
        if (img.IsDisposed()) return null;

        var path = BHelper.ConfigDir(Dir.Temporary, $"ig_print_{Guid.NewGuid():N}.png");
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        await using var stream = File.Create(path);
        data.SaveTo(stream);

        return path;
    }

    protected static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

}
