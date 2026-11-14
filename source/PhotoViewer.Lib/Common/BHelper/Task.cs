
using Avalonia.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common;

public partial class BHelper
{
    private static CancellationTokenSource? _cancelGcCollect;

    private static readonly ConcurrentDictionary<object, CancellationTokenSource> _tokens = new();

    public static void Debounce(int delayMs, Action action, bool runUIThread = false)
    {
        DebounceInternal(action, delayMs, action, runUIThread);
    }

    public static void Debounce<T>(int delayMs, Action<T?> action, T? param = default, bool runUIThread = false)
    {
        DebounceInternal(action, delayMs, () => action(param), runUIThread);
    }

    private static void DebounceInternal(object key, int delayMs, Action workload, bool runUIThread)
    {
        if (_tokens.TryRemove(key, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _tokens[key] = cts;

        Task.Delay(delayMs, token).ContinueWith(t =>
        {
            if (token.IsCancellationRequested) return;

            if (_tokens.TryRemove(new KeyValuePair<object, CancellationTokenSource>(key, cts)))
            {
                cts.Dispose();
            }

            if (runUIThread)
            {
                Dispatcher.UIThread.Post(workload, DispatcherPriority.Background);
            }
            else
            {
                workload();
            }
        },
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnRanToCompletion,
        TaskScheduler.Default);
    }

    public static void GcCollect(int delayMs = 500)
    {
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _cancelGcCollect, newCts);

        oldCts?.Cancel();
        oldCts?.Dispose();

        _ = GCCollectAsync(delayMs, newCts.Token);
    }

    private static async Task GCCollectAsync(int delayMs, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        await Task.Delay(delayMs, token);

        if (token.IsCancellationRequested) return;

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

}
