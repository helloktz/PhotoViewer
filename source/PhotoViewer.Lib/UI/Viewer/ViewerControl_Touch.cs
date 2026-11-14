using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using PhotoViewer.Common;
using PhotoViewer.Common.ServiceProviders;
using PhotoViewer.Common.Types;
using PhotoViewer.UI.Viewer.ZoomAndPan;

namespace PhotoViewer.UI.Viewer;

public partial class ViewerControl
{
    private PhPinchGestureRecognizer _pinchGesture = new();

    private void RegisterTouchGestures()
    {
        GestureRecognizers.Add(new ScrollGestureRecognizer
        {
            CanHorizontallyScroll = true,
            CanVerticallyScroll = true,
            IsScrollInertiaEnabled = true,
        });

        _pinchGesture.Pinch += OnTouchPinched;
        GestureRecognizers.Add(_pinchGesture);

        ScrollGesture += ViewerControl_ScrollGesture;
        ScrollGestureEnded += ViewerControl_ScrollGestureEnded;

        PointerTouchPadGestureMagnify += ViewerControl_PointerTouchPadGestureMagnify;
    }

    private void UnregisterTouchGestures()
    {
        _pinchGesture.Pinch -= OnTouchPinched;

        ScrollGesture -= ViewerControl_ScrollGesture;
        ScrollGestureEnded -= ViewerControl_ScrollGestureEnded;

        PointerTouchPadGestureMagnify -= ViewerControl_PointerTouchPadGestureMagnify;
    }

    private void ViewerControl_ScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        var args = (ScrollGestureEventArgs)e;

        if (EnableSelection && CurrentSelectionAction != SelectionAction.None)
        {
        }
        else
        {
            if (!_enablePanningVelocity) args.ShouldEndScrollGesture = true;

            if (!FeatureManager.IsPanLocked()) _ = PanTo(args.Delta.X, args.Delta.Y, null);
        }

        e.Handled = true;
    }

    private void ViewerControl_ScrollGestureEnded(object? sender, ScrollGestureEndedEventArgs e)
    {
        _enablePanningVelocity = true;
    }

    private void OnTouchPinched(object? sender, PhPinchEventArgs e)
    {
        var delta = e.Expansion * ZoomInfo.MAX_ZOOM_SPEED;

        var isPintching = delta != 0;
        if (!isPintching) return;

        if (!FeatureManager.IsPanLocked())
            PanTo(-e.Translation.X, -e.Translation.Y, e.Position, !isPintching);

        if (isPintching && !FeatureManager.IsZoomLocked())
        {
            _ = ZoomByDeltaToPoint(delta, e.Position);
        }

        e.Handled = true;
    }

    private void ViewerControl_PointerTouchPadGestureMagnify(object? sender, PointerDeltaEventArgs e)
    {
        var position = e.GetPosition(this);
        var delta = e.Delta.X;

        if (BHelper.OS == OSType.Mac)
        {
            delta *= 1000;
        }

        if (!FeatureManager.IsZoomLocked()) ZoomByDeltaToPoint(delta, position);
        e.Handled = true;
    }

}
