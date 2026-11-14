using System;
using System.ComponentModel;

namespace PhotoViewer.Common.Types;

public partial class PhDisposable : PhReactive, IDisposable
{
    protected InterlockedBool _isDisposed = new(false);

    public bool IsDisposed => _isDisposed;

    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed) return;

        if (disposing)
        {
            OnDisposing();

            CleanUpPropertyChangedEvents();
        }

        _isDisposed.SetTrue();
    }

    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~PhDisposable()
    {
        Dispose(false);
    }

    protected virtual void OnDisposing()
    {
    }

}
