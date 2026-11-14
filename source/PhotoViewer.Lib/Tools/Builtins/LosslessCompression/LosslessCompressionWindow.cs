using PhotoViewer.Common;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.UI.Windowing;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

public partial class LosslessCompressionWindow : ModalWindow
{
    private const int MAX_ATTEMPTS = 5;
    private const int RETRY_DELAY_MS = 300;

    private readonly CancellationTokenSource _cancel = new();
    private readonly FileInfo _srcFileInfo;
    private bool _isRunning;

    public LosslessCompressionWindow(string srcFilePath)
    {
        if (string.IsNullOrEmpty(srcFilePath)) throw new ArgumentNullException(nameof(srcFilePath));

        _srcFileInfo = new FileInfo(srcFilePath);

        ShowInTaskbar = true;
        Note = $"""
            {srcFilePath}

            {Core.Photos.CurrentMetadata?.FileSizeFormatted}
            """;
        NoteStyle = InfoBarSeverity.Info;
        Thumbnail = Core.Photos.Current?.GalleryThumbnail;

        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Button1;
    }

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Menu_MnuLosslessCompression];
        Heading = Core.Lang[LangId.Menu_MnuLosslessCompression_Confirm];
        Description = Core.Lang[LangId.Menu_MnuLosslessCompression_Description];
        Button1Text = Core.Lang[LangId._Yes];
        Button2Text = Core.Lang[LangId._No];
    }

    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        if (_isRunning) return;
        _isRunning = true;

        _ = RunAsync(_srcFileInfo);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        _cancel.Cancel();
        _cancel.Dispose();
    }

    private async Task RunAsync(FileInfo fi)
    {
        var oldFileLength = fi.Length;
        var oldFileSizeFormatted = Core.Photos.CurrentMetadata?.FileSizeFormatted
            ?? BHelper.FormatSize(oldFileLength);
        var token = _cancel.Token;

        _btn1.IsEnabled = false;
        _btn1.IsDefault = false;
        _btn2.Focus(Avalonia.Input.NavigationMethod.Tab);

        IsButton1Visible = false;
        IsButton2Visible = true;
        Button2Text = Core.Lang[LangId._Cancel];
        DefaultButton = DialogButton.Button2;

        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;

        Heading = Core.Lang[LangId.Menu_MnuLosslessCompression_Compressing];
        Description = Core.Lang[LangId.Menu_MnuLosslessCompression_Description];
        Note = $"""
            {fi.FullName}

            {oldFileSizeFormatted}
            """;

        try
        {
            await CompressAsync(fi.FullName, token);
            await Task.Delay(200);
            if (token.IsCancellationRequested) return;

            var newFi = new FileInfo(fi.FullName);
            var percent = Math.Round((1 - (newFi.Length * 1f / oldFileLength)) * 100f, 2);

            Heading = Core.Lang[LangId.Menu_MnuLosslessCompression_Done];
            Note = $"""
                {newFi.FullName}

                {oldFileSizeFormatted} ⇒ {BHelper.FormatSize(newFi.Length)} (↓ {percent}%)
                """;
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;

            Heading = Core.Lang[LangId.Menu_MnuLosslessCompression_Error];
            NoteStyle = InfoBarSeverity.Danger;
            Note = $"""
                {fi.FullName}

                {ex.Message}
                """;
            Details = BHelper.GetExceptionDetails(ex);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                ProgressValue = 100;
                IsProgressIndeterminate = false;
                IsProgressVisible = false;

                Button2Text = Core.Lang[LangId._Close];
                SetDefaultButton(DialogButton.Button2);
                _btn2.Focus(Avalonia.Input.NavigationMethod.Tab);
            }
        }
    }

    private static async Task<bool> CompressAsync(string filePath, CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                return await Task.Factory.StartNew(() => MagickCodec.LosslessCompress(filePath),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                    .ConfigureAwait(false);
            }
            catch (IOException) when (attempt < MAX_ATTEMPTS)
            {
                await Task.Delay(RETRY_DELAY_MS * attempt).ConfigureAwait(false);
            }
        }
    }

}
