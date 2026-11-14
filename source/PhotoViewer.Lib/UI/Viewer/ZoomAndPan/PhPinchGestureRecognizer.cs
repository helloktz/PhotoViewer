using Avalonia;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using System;

namespace PhotoViewer.UI.Viewer.ZoomAndPan;

public class PhPinchGestureRecognizer : GestureRecognizer
{
    private float _initialDistance;

    private IPointer? _firstContact;
    private Point _firstPoint;
    private IPointer? _secondContact;
    private Point _secondPoint;

    private Point _origin;
    private Point _previousCenter;
    private double _previousAngle;
    private double _previousScale = 1.0;

    public event EventHandler<PhPinchEventArgs>? Pinch;
    public event EventHandler<PinchEndedEventArgs>? PinchEnded;

    protected override void PointerPressed(PointerPressedEventArgs e)
    {
        if (Target is not Visual visual) return;
        if (e.Pointer.Type == PointerType.Mouse) return;

        if (_firstContact == null)
        {
            _firstContact = e.Pointer;
            _firstPoint = e.GetPosition(visual);
            return;
        }

        if (_secondContact == null && _firstContact != e.Pointer)
        {
            _secondContact = e.Pointer;
            _secondPoint = e.GetPosition(visual);
        }
        else
        {
            return;
        }

        if (_firstContact != null && _secondContact != null)
        {
            _initialDistance = GetDistance(_firstPoint, _secondPoint);
            _origin = new Point((_firstPoint.X + _secondPoint.X) / 2.0, (_firstPoint.Y + _secondPoint.Y) / 2.0);
            _previousCenter = _origin;
            _previousAngle = GetAngleDegreeFromPoints(_firstPoint, _secondPoint);

            Capture(_firstContact);
            Capture(_secondContact);
            e.PreventGestureRecognition();
        }
    }

    protected override void PointerReleased(PointerReleasedEventArgs e)
    {
        if (RemoveContact(e.Pointer))
        {
            e.PreventGestureRecognition();
        }
    }

    protected override void PointerMoved(PointerEventArgs e)
    {
        if (Target is not Visual visual) return;

        if (_firstContact == e.Pointer)
        {
            _firstPoint = e.GetPosition(visual);
        }
        else if (_secondContact == e.Pointer)
        {
            _secondPoint = e.GetPosition(visual);
        }
        else
        {
            return;
        }

        if (_firstContact != null && _secondContact != null)
        {
            var distance = GetDistance(_firstPoint, _secondPoint);
            var degree = GetAngleDegreeFromPoints(_firstPoint, _secondPoint);

            var scale = distance / _initialDistance;
            var scaleDiff = scale / _previousScale;
            var expansion = scaleDiff - 1.0;
            _previousScale = scale;

            var currentCenter = new Point(
                (_firstPoint.X + _secondPoint.X) / 2.0,
                (_firstPoint.Y + _secondPoint.Y) / 2.0);

            var translation = currentCenter - _previousCenter;
            _previousCenter = currentCenter;

            var pinchEventArgs = new PhPinchEventArgs(expansion, scale, _origin, degree, _previousAngle - degree, translation, currentCenter);
            _previousAngle = degree;
            Pinch?.Invoke(Target, pinchEventArgs);

            e.Handled = pinchEventArgs.Handled;
            e.PreventGestureRecognition();
        }

    }

    protected override void PointerCaptureLost(IPointer pointer)
    {
        RemoveContact(pointer);
    }

    private bool RemoveContact(IPointer pointer)
    {
        if (_firstContact == pointer || _secondContact == pointer)
        {
            if (_secondContact == pointer)
            {
                _secondContact = null;
            }

            if (_firstContact == pointer)
            {
                _firstContact = _secondContact;
                _secondContact = null;
            }

            _previousScale = 1.0;
            PinchEnded?.Invoke(Target, new PinchEndedEventArgs());
            return true;
        }

        return false;
    }

    private static float GetDistance(Point a, Point b)
    {
        var length = b - a;
        return (float)new Vector(length.X, length.Y).Length;
    }

    private static double GetAngleDegreeFromPoints(Point a, Point b)
    {
        var deltaX = a.X - b.X;
        var deltaY = -(a.Y - b.Y);
        var rad = System.Math.Atan2(deltaX, deltaY);
        var degree = ((rad * (180 / System.Math.PI))) + 180;

        return degree;
    }

}

public class PhPinchEventArgs : PinchEventArgs
{

    public double Expansion { get; }

    public Vector Translation { get; }

    public Point Position { get; }

    public PhPinchEventArgs(double expansion,
        double scale,
        Point scaleOrigin,
        double angle,
        double angleDelta,
        Vector translation,
        Point position) : base(scale, scaleOrigin, angle, angleDelta)
    {
        Expansion = expansion;
        Translation = translation;
        Position = position;
    }

}
