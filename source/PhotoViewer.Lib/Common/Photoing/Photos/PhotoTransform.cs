using PhotoViewer.Common.Types;
using System;

namespace PhotoViewer.Common.Photoing;

public class PhotoTransform
{
    private FlipOptions _flipOptions = FlipOptions.None;
    private float _rotation = 0;
    private bool _isColorInverted = false;

    public event TEventHandler<PhotoTransform, EventArgs>? Changed;

    public FlipOptions Flips
    {
        get => _flipOptions;
        set
        {
            if (_flipOptions != value)
            {
                _flipOptions = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public float Rotation
    {
        get => _rotation;
        set
        {
            if (_rotation != value)
            {
                _rotation = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsColorInverted
    {
        get => _isColorInverted;
        set
        {
            if (_isColorInverted != value)
            {
                _isColorInverted = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int FrameIndex { get; set; } = -1;

    public ImageOrientation Orientation
    {
        get => ImageOrientation.From(_flipOptions, _rotation);
        set
        {
            if (Orientation == value) return;

            _flipOptions = value.Flips;
            _rotation = value.Rotation;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasChanges => Flips != FlipOptions.None
        || Rotation != 0
        || IsColorInverted;

    public void Clear()
    {
        _flipOptions = FlipOptions.None;
        _rotation = 0;
        _isColorInverted = false;
        FrameIndex = -1;

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
