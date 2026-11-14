using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using PhotoViewer.Common.Photoing;
using PhotoViewer.Common.ServiceProviders;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PhotoViewer.UI;

public partial class GalleryItem
{
    private const double DRAG_THRESHOLD = 6d;

    private PointerPressedEventArgs? _dragTriggerEvent;
    private Point _dragStartPoint;
    private bool _isDraggingOut;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ResetDragOutState();

        if (e.Pointer.Type == PointerType.Touch) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragTriggerEvent = e;
        _dragStartPoint = e.GetPosition(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDraggingOut || _dragTriggerEvent is null) return;

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _dragStartPoint.X) < DRAG_THRESHOLD
            && Math.Abs(point.Y - _dragStartPoint.Y) < DRAG_THRESHOLD) return;

        _isDraggingOut = true;
        _ = StartFileDragAsync(_dragTriggerEvent);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ResetDragOutState();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ResetDragOutState();
    }

    private async Task StartFileDragAsync(PointerPressedEventArgs triggerEvent)
    {
        try
        {
            var allowedEffects = GetAllowedDragEffects();
            if (allowedEffects == DragDropEffects.None) return;

            if (DataContext is not Photo photo) return;
            if (TopLevel.GetTopLevel(this) is not TopLevel topLevel) return;
            if (!File.Exists(photo.FilePath)) return;

            var file = await topLevel.StorageProvider.TryGetFileFromPathAsync(photo.FilePath);
            if (file is null) return;

            var dtItem = new DataTransferItem();
            dtItem.SetFile(file);
            dtItem.SetText(photo.FilePath);

            var dataTransfer = new DataTransfer();
            dataTransfer.Add(dtItem);

            _ = await DragDrop.DoDragDropAsync(triggerEvent, dataTransfer, allowedEffects);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(StartFileDragAsync)}: {ex.Message}");
        }
        finally
        {
            ResetDragOutState();
        }
    }

    private static DragDropEffects GetAllowedDragEffects()
    {
        if (FeatureManager.IsLocked(API.IG_CopyFiles)) return DragDropEffects.None;

        var effects = DragDropEffects.Copy | DragDropEffects.Link;
        if (!FeatureManager.IsLocked(API.IG_CutFiles)) effects |= DragDropEffects.Move;

        return effects;
    }

    private void ResetDragOutState()
    {
        _dragTriggerEvent = null;
        _isDraggingOut = false;
    }

}
