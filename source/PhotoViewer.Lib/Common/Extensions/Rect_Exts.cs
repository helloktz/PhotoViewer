using Avalonia;
using SkiaSharp;
using System;

namespace PhotoViewer.Common.Extensions;

public static class Rect_Exts
{
    private static readonly string _delimiter = ";";

    extension(Rect rect)
    {

        public bool IsEmpty => rect.Size.IsEmpty;

        public Rect Normalize()
        {
            var x = double.IsFinite(rect.X) ? rect.X : 0;
            var y = double.IsFinite(rect.Y) ? rect.Y : 0;
            var w = double.IsFinite(rect.Width) ? rect.Width : 0;
            var h = double.IsFinite(rect.Height) ? rect.Height : 0;

            return new Rect(x, y, w, h);
        }

        public bool IntersectsWith(Rect rect2)
        {
            if (rect.Width < 0f || rect2.Width < 0f) return false;

            if (rect2.X <= rect.X + rect.Width
                && rect2.X + rect2.Width >= rect.X
                && rect2.Y <= rect.Y + rect.Height)
            {
                return rect2.Y + rect2.Height >= rect.Y;
            }

            return false;
        }

        public Rect GetIntersection(double width, double height)
        {
            return rect.GetIntersection(new Rect(0, 0, width, height));
        }

        public Rect GetIntersection(Rect rect2)
        {
            if (!rect.IntersectsWith(rect2)) return rect;

            var x = Math.Max(rect.X, rect2.X);
            var y = Math.Max(rect.Y, rect2.Y);
            var w = Math.Max(Math.Min(rect.X + rect.Width, rect2.X + rect2.Width) - x, 0);
            var h = Math.Max(Math.Min(rect.Y + rect.Height, rect2.Y + rect2.Height) - y, 0);

            return new Rect(x, y, w, h);
        }

        public Rect CenterRectEx(Rect rect2, bool limitRect1Size = false)
        {
            var centeredRect = rect.CenterRect(rect2);
            if (!limitRect1Size) return centeredRect;

            var x = Math.Max(rect.X, centeredRect.X);
            var y = Math.Max(rect.Y, centeredRect.Y);
            var width = Math.Min(rect.Width, rect2.Width);
            var height = Math.Min(rect.Height, rect2.Height);

            return new Rect(x, y, width, height);
        }

        public double[] ToArray()
        {
            return [rect.X, rect.Y, rect.Width, rect.Height];
        }

        public string ToStringDelimiter()
        {
            var str = string.Join(_delimiter, rect.ToArray());

            return str;
        }

        public SKRectI ToSKRectI()
        {
            return new SKRectI((int)rect.X, (int)rect.Y, (int)rect.Right, (int)rect.Bottom);
        }

    }

}
