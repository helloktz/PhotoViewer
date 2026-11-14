using Avalonia.Threading;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Windowing;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Windows;

public partial class ExportFramesWindow : ModalWindow
{
    private CancellationTokenSource _cancel = new();

    private string _srcFilePath;
    private string _destDirPath;
    private bool _isDone = false;

    public ExportFramesWindow(string srcFilePath, string destDirPath)
    {
        if (string.IsNullOrEmpty(srcFilePath)) throw new ArgumentNullException(nameof(srcFilePath));
        if (string.IsNullOrEmpty(destDirPath)) throw new ArgumentNullException(nameof(destDirPath));

        _srcFilePath = srcFilePath;
        _destDirPath = destDirPath;
        ShowInTaskbar = true;
        SizeToContent = Avalonia.Controls.SizeToContent.Height;
        Width = 600;

        Description = srcFilePath;
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

        Title = Core.Lang[LangId._Title];
        Heading = Core.Lang[LangId._Title];
        Button1Text = Core.Lang[LangId._Start];
        Button2Text = Core.Lang[LangId._Cancel];
    }

    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        if (_isDone)
        {
            BHelper.OpenFolderPath(_destDirPath);
        }
        else
        {
            _ = RunAsync(_srcFilePath, _destDirPath);
        }
    }

    protected override void OnDialogCancelled(DialogEventArgs e)
    {
        _cancel?.Cancel();

        base.OnDialogCancelled(e);
    }

    protected override void OnDialogAborted()
    {
        _cancel?.Cancel();

        base.OnDialogAborted();
    }

    private async Task RunAsync(string srcFilePath, string destDirPath)
    {
        _isDone = false;
        _btn1.IsEnabled = false;
        _btn2.Focus(Avalonia.Input.NavigationMethod.Tab);

        IsButton1Visible = false;
        IsButton2Visible = true;
        Button2Text = Core.Lang[LangId._Cancel];

        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        Note = null;

        var exportedCount = 0;

        try
        {
            await Task.Factory.StartNew(async () =>
            {
                await foreach (var info in PhotoFrameExporter.SaveFramesAsync(srcFilePath, destDirPath, _cancel.Token))
                {
                    exportedCount++;

                    Dispatcher.UIThread.Post(() =>
                    {
                        var percent = Math.Round((info.FrameNumber * 100f) / info.FrameCount, 0);

                        IsProgressIndeterminate = false;
                        ProgressValue = percent;
                        Title = $"{Core.Lang[LangId._Title]} ({percent}%)";

                        if (info.FrameNumber == info.FrameCount)
                        {
                            _btn1.IsEnabled = true;
                            _btn1.Focus(Avalonia.Input.NavigationMethod.Tab);
                            IsButton1Visible = true;
                            Button1Text = Core.Lang[LangId._OpenOutputFolder];

                            Button2Text = Core.Lang[LangId._Close];
                            Description = string.Format(Core.Lang[LangId._ExportDone],
                                info.FrameNumber,
                                $"\"{destDirPath}\"");

                            IsProgressVisible = false;
                            _isDone = true;
                        }

                        else
                        {
                            var frameFilePath = Path.Combine(destDirPath, info.FileName);

                            Description = string.Format(Core.Lang[LangId._Exporting],
                                info.FrameNumber,
                                info.FrameCount,
                                $"\"{frameFilePath}\"");
                        }
                    });
                }
            }, _cancel.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return;
        }

        if (exportedCount == 0 && !_cancel.IsCancellationRequested)
        {
            ShowError(Core.Lang[LangId._NotSupported]);
        }
    }

    private void ShowError(string message)
    {
        IsProgressVisible = false;
        IsProgressIndeterminate = false;

        Title = Core.Lang[LangId._Title];
        Heading = message;
        Description = _srcFilePath;

        Thumbnail = null;
        ThumbnailIcon = StockIconId.Error;
        LoadThumbnailIconSource();

        IsButton1Visible = false;
        IsButton2Visible = true;
        Button2Text = Core.Lang[LangId._Close];
        _btn2.Focus(Avalonia.Input.NavigationMethod.Tab);
    }

}
