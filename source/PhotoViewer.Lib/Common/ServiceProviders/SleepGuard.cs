using System.Collections.Generic;
using System.Threading;

namespace PhotoViewer.Common.ServiceProviders;

public static class SleepGuard
{
    private static readonly Lock _lock = new();
    private static readonly HashSet<string> _owners = [];

    public static bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _owners.Count > 0;
            }
        }
    }

    public static void Acquire(string owner, string reason)
    {
        lock (_lock)
        {
            var isFirstOwner = _owners.Count == 0;
            var isNewOwner = _owners.Add(owner);
            if (!isNewOwner) return;
            if (!isFirstOwner) return;

            Core.ShellProvider?.PreventSleep(reason);
        }
    }

    public static void Release(string owner)
    {
        lock (_lock)
        {
            var isRemoved = _owners.Remove(owner);
            if (!isRemoved) return;

            var hasOtherOwners = _owners.Count > 0;
            if (hasOtherOwners) return;

            Core.ShellProvider?.AllowSleep();
        }
    }

    public static void ReleaseAll()
    {
        lock (_lock)
        {
            var hasOwners = _owners.Count > 0;
            if (!hasOwners) return;

            _owners.Clear();
            Core.ShellProvider?.AllowSleep();
        }
    }
}
