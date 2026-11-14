using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoViewer.Common;
using PhotoViewer.Common.Photoing;
using System;
using System.Diagnostics;
using System.Threading;

namespace PhotoViewer.UI;

public partial class GalleryItem : PhToolButton
{
    private CancellationTokenSource? _thumbnailCts;
    private Photo? _tooltipReadyPhoto;
    public Photo VM => (Photo)DataContext!;

    public GalleryItem()
    {
        InitializeComponent();
        ToolTip.AddToolTipOpeningHandler(this, ToolTip_Opening);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        LoadThumbnail();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        CancelThumbnailLoading();
        base.OnUnloaded(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == DataContextProperty)
        {
            _tooltipReadyPhoto = null;

            if (DataContext is Photo && IsLoaded) LoadThumbnail();
            else CancelThumbnailLoading();
        }
    }

    internal async void LoadThumbnail(bool useCache = true)
    {
        CancelThumbnailLoading();
        if (DataContext is not Photo photo) return;

        _thumbnailCts = new CancellationTokenSource();
        try
        {
            var thumbSize = Core.Config.ThumbnailSize * Dpi * 2;
            await photo.LoadThumbnailAsync(thumbSize, useCache, _thumbnailCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(LoadThumbnail)}: {ex.Message}");
        }
    }

    internal void CancelThumbnailLoading()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
    }

    private async void ToolTip_Opening(object? sender, CancelRoutedEventArgs e)
    {
        if (DataContext is not Photo photo) return;
        if (ReferenceEquals(_tooltipReadyPhoto, photo) || photo.Metadata.FrameCount > 0) return;

        e.Cancel = true;
        await photo.LoadMetadataAsync(true);

        if (!ReferenceEquals(DataContext, photo) || !IsPointerOver || !ToolTip.GetServiceEnabled(this)) return;

        _tooltipReadyPhoto = photo;
        ToolTip.SetIsOpen(this, true);
    }

}
