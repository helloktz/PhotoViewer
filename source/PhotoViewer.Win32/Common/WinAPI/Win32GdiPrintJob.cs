using PhotoViewer.Common.Printing;
using PhotoViewer.Common.ServiceProviders;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;

namespace PhotoViewer.Win32.Common;

internal static unsafe class Win32GdiPrintJob
{
    private const long BAND_BYTES = 32L * 1024 * 1024;

    private const float MAX_RASTER_DPI = 600;

    private const int GDI_ERROR = -1;

    public static Task RunAsync(PrintJob job, byte[] devMode, int ownCopies, IProgress<PrintProgress>? progress, CancellationToken token)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var isAborted = false;
            try
            {
                Run(job, devMode, ownCopies, progress, token, ref isAborted);
                done.SetResult();
            }
            catch (OperationCanceledException)
            {
                done.SetCanceled(token);
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }

            if (isAborted && !string.IsNullOrEmpty(job.Settings.OutputPath)) DeleteAbortedOutput(job.Settings.OutputPath);
        })
        {
            IsBackground = true,
            Name = "Photo Viewer print job",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return done.Task;
    }

    private static void Run(PrintJob job, byte[] devMode, int ownCopies, IProgress<PrintProgress>? progress,
        CancellationToken token, ref bool isAborted)
    {
        var settings = job.Settings;
        HDC hdc;

        fixed (char* pName = settings.Printer.Id)
        fixed (byte* pDevMode = devMode)
        {
            hdc = PInvoke.CreateDCW(default, pName, default, (DEVMODEW*)pDevMode);
        }

        if (hdc.IsNull) throw new Win32Exception(Marshal.GetLastSystemError());

        var isStarted = false;
        try
        {
            _ = PInvoke.SetStretchBltMode(hdc, STRETCH_BLT_MODE.HALFTONE);
            _ = PInvoke.SetBrushOrgEx(hdc, 0, 0, null);
            var device = DeviceGeometry.Read(hdc);

            fixed (char* pTitle = job.Title)
            fixed (char* pOutput = settings.OutputPath)
            {
                var info = new DOCINFOW
                {
                    cbSize = sizeof(DOCINFOW),
                    lpszDocName = pTitle,
                    lpszOutput = pOutput,
                };

                if (PInvoke.StartDoc(hdc, &info) <= 0) throw new Win32Exception(Marshal.GetLastSystemError());
            }

            isStarted = true;
            var sheets = GetSheets(job.Layout.Pages.Count, ownCopies, settings.Collate, settings.Duplex != PrintDuplex.None);

            for (var i = 0; i < sheets.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(new PrintProgress(i, sheets.Count));

                if (PInvoke.StartPage(hdc) <= 0) throw new Win32Exception(Marshal.GetLastSystemError());
                if (sheets[i] is int pageIndex) DrawPage(hdc, device, job, pageIndex, token);
                if (PInvoke.EndPage(hdc) <= 0) throw new Win32Exception(Marshal.GetLastSystemError());
            }

            if (PInvoke.EndDoc(hdc) <= 0) throw new Win32Exception(Marshal.GetLastSystemError());
            isStarted = false;

            progress?.Report(new PrintProgress(sheets.Count, sheets.Count));
        }
        finally
        {
            if (isStarted)
            {
                _ = PInvoke.AbortDoc(hdc);
                isAborted = true;
            }

            _ = PInvoke.DeleteDC(hdc);
        }
    }

    private static List<int?> GetSheets(int pageCount, int copies, bool collate, bool isTwoSided)
    {
        var sheets = new List<int?>(pageCount * copies);

        if (!collate)
        {
            for (var page = 0; page < pageCount; page++)
            {
                for (var copy = 0; copy < copies; copy++) sheets.Add(page);
            }

            return sheets;
        }

        for (var copy = 0; copy < copies; copy++)
        {
            for (var page = 0; page < pageCount; page++) sheets.Add(page);
            if (isTwoSided && pageCount % 2 == 1 && copy < copies - 1) sheets.Add(null);
        }

        return sheets;
    }

    private static void DrawPage(HDC hdc, DeviceGeometry device, PrintJob job, int pageIndex, CancellationToken token)
    {
        var maxDpi = Math.Min(MAX_RASTER_DPI, Math.Min(device.DpiX, device.DpiY));

        var render = job.Render with { ColorMode = job.Settings.ColorMode, TargetDpi = maxDpi, IsPreview = false };

        foreach (var cell in job.Layout.Pages[pageIndex].Cells)
        {
            token.ThrowIfCancellationRequested();

            var dpi = PrintPageRenderer.GetCellRasterDpiAsync(cell, job.Session, render, maxDpi, token).GetAwaiter().GetResult();
            DrawCell(hdc, device, job, pageIndex, cell.RectPt, dpi, render, token);
        }
    }

    private static void DrawCell(HDC hdc, DeviceGeometry device, PrintJob job, int pageIndex,
        SKRect rectPt, float dpi, PrintRenderOptions render, CancellationToken token)
    {
        var left = device.ToDeviceX(rectPt.Left);
        var top = device.ToDeviceY(rectPt.Top);
        var right = device.ToDeviceX(rectPt.Right);
        var bottom = device.ToDeviceY(rectPt.Bottom);
        if (right <= left || bottom <= top) return;

        var width = Math.Max(1, (int)Math.Ceiling(PrintUnits.PtToPx(rectPt.Width, dpi)));
        var height = Math.Max(1, (int)Math.Ceiling(PrintUnits.PtToPx(rectPt.Height, dpi)));
        var bandRows = (int)Math.Clamp(BAND_BYTES / (width * 4L), 1, height);

        for (var y = 0; y < height; y += bandRows)
        {
            token.ThrowIfCancellationRequested();
            var rows = Math.Min(bandRows, height - y);

            var info = new SKImageInfo(width, rows, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
            using var band = new SKBitmap(info);
            using (var canvas = new SKCanvas(band))
            {
                canvas.Clear(SKColors.White);
                canvas.Translate(0, -y);
                canvas.Scale(width / rectPt.Width, height / rectPt.Height);
                canvas.Translate(-rectPt.Left, -rectPt.Top);
                canvas.ClipRect(rectPt);

                PrintPageRenderer.DrawPageAsync(canvas, job.Layout, pageIndex, job.Session, render, token).GetAwaiter().GetResult();
            }

            var destTop = top + (int)Math.Round((bottom - top) * (y / (double)height));
            var destBottom = top + (int)Math.Round((bottom - top) * ((y + rows) / (double)height));
            if (destBottom <= destTop) continue;

            var bitmapInfo = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = width,
                    biHeight = -rows,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };

            var copied = PInvoke.StretchDIBits(hdc, left, destTop, right - left, destBottom - destTop,
                0, 0, width, rows, (void*)band.GetPixels(), &bitmapInfo, DIB_USAGE.DIB_RGB_COLORS, ROP_CODE.SRCCOPY);

            if (copied == 0 || copied == GDI_ERROR) throw new Win32Exception(Marshal.GetLastSystemError());
        }
    }

    private static void DeleteAbortedOutput(string path)
    {
        for (var attempt = 0; attempt < 20 && File.Exists(path); attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Thread.Sleep(250);
        }
    }

    private readonly record struct DeviceGeometry(int DpiX, int DpiY, int OffsetX, int OffsetY)
    {
        public static DeviceGeometry Read(HDC hdc)
        {
            return new DeviceGeometry(
                PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.LOGPIXELSX),
                PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.LOGPIXELSY),
                PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.PHYSICALOFFSETX),
                PInvoke.GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.PHYSICALOFFSETY));
        }

        public int ToDeviceX(float pt) => (int)Math.Round(PrintUnits.PtToPx(pt, DpiX)) - OffsetX;

        public int ToDeviceY(float pt) => (int)Math.Round(PrintUnits.PtToPx(pt, DpiY)) - OffsetY;
    }
}
