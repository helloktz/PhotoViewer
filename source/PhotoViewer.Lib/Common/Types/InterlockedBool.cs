using System.Threading;

namespace PhotoViewer.Common.Types;

public struct InterlockedBool(bool initialValue = false)
{
    private int _value = initialValue ? 1 : 0;

    public bool Value => Volatile.Read(ref _value) == 1;

    public bool Set(bool value)
    {
        if (value) return SetTrue();
        else return SetFalse();
    }

    public bool SetTrue() => Interlocked.Exchange(ref _value, 1) == 0;

    public bool SetFalse() => Interlocked.Exchange(ref _value, 0) == 1;

    public static implicit operator bool(InterlockedBool b) => b.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();
}
