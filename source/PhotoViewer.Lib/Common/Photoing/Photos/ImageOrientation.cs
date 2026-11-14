using System;

namespace PhotoViewer.Common.Photoing;

public readonly record struct ImageOrientation(int Rotation, bool IsMirrored)
{

    public static ImageOrientation Identity { get; } = new(0, false);

    public bool IsIdentity => Rotation == 0 && !IsMirrored;

    public bool SwapsAxes => Rotation is 90 or 270;

    public FlipOptions Flips => IsMirrored ? FlipOptions.Horizontal : FlipOptions.None;

    public ImageOrientation Rotate(int degrees) => new(Normalize(Rotation + degrees), IsMirrored);

    public ImageOrientation Flip(FlipOptions options)
    {
        var result = this;

        if (options.HasFlag(FlipOptions.Horizontal))
        {
            result = new(Normalize(-result.Rotation), !result.IsMirrored);
        }

        if (options.HasFlag(FlipOptions.Vertical))
        {
            result = new(Normalize(180 - result.Rotation), !result.IsMirrored);
        }

        return result;
    }

    public static ImageOrientation From(FlipOptions flips, double rotation)
    {
        return Identity.Flip(flips).Rotate((int)Math.Round(rotation));
    }

    public (int Width, int Height) GetSize(int width, int height)
    {
        return SwapsAxes ? (height, width) : (width, height);
    }

    private static int Normalize(int degrees)
    {
        var turns = (int)Math.Round(degrees / 90d);
        return (turns % 4 + 4) % 4 * 90;
    }
}
