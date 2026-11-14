using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PhotoViewer.Common.Loggers;

public static class StartupTrace
{
    private static readonly Stopwatch _sw = Stopwatch.StartNew();
    private static readonly List<(long Ms, string Name, int Tid)> _marks = new(32);
    private static readonly Lock _lock = new();
    private static int _flushedCount;

    public static bool Enabled { get; private set; }

    public static void EnableFromArgs(string[]? args)
    {
        if (Enabled || args is null) return;

        foreach (var arg in args)
        {
            if (string.Equals(arg, AppCmds.STARTUP_TRACE, StringComparison.OrdinalIgnoreCase))
            {
                Enabled = true;
                return;
            }
        }
    }

    public static void Mark(string name)
    {
        lock (_lock)
        {
            _marks.Add((_sw.ElapsedMilliseconds, name, Environment.CurrentManagedThreadId));
        }
    }

    public static void Flush()
    {
        if (!Enabled) return;

        lock (_lock)
        {
            if (_flushedCount >= _marks.Count) return;

            var lines = new List<string>(_marks.Count - _flushedCount + 2);

            if (_flushedCount == 0)
            {
                lines.Add($"===== Photo Viewer startup trace @ PID {Environment.ProcessId} =====");
                try
                {
                    var startToNow = DateTime.Now - Process.GetCurrentProcess().StartTime;
                    lines.Add($"   (process start -> first mark ~ {startToNow.TotalMilliseconds,7:0.0} ms)");
                }
                catch { }
            }

            var prev = _flushedCount > 0 ? _marks[_flushedCount - 1].Ms : 0;
            for (var i = _flushedCount; i < _marks.Count; i++)
            {
                var (ms, name, tid) = _marks[i];
                lines.Add($"{ms,7} ms  (+{ms - prev,6} ms)  [t{tid}]  {name}");
                prev = ms;
            }
            _flushedCount = _marks.Count;

            foreach (var line in lines)
            {
                Debug.WriteLine($"[IG-STARTUP] {line}");
            }

            try
            {
                var logPath = BHelper.ConfigDir(Dir.Logs, "ig_startup_trace.log");
                File.AppendAllText(logPath, string.Join(Environment.NewLine, lines)
                    + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
