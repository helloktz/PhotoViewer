using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PhotoViewer.Common.Loggers;

public static class PhotoTrace
{
    private static readonly Stopwatch _sw = Stopwatch.StartNew();
    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private static StreamWriter? _writer;
    private static int _sessionCounter;

    private const string MEMORY_KEY = "<memory>";

    private sealed class Session(int id, long beginMs)
    {
        public int Id { get; } = id;
        public long BeginMs { get; } = beginMs;
        public long LastMs { get; set; } = beginMs;
    }

    public static bool Enabled { get; private set; }

    public static void EnableFromArgs(string[]? args)
    {
        if (Enabled || args is null) return;

        foreach (var arg in args)
        {
            if (string.Equals(arg, AppCmds.PHOTO_TRACE, StringComparison.OrdinalIgnoreCase))
            {
                Enabled = true;
                return;
            }
        }
    }

    public static void Begin(string? file, string? detail = null)
    {
        if (!Enabled) return;

        var key = NormalizeKey(file);
        lock (_lock)
        {
            var ms = _sw.ElapsedMilliseconds;
            var id = ++_sessionCounter;
            _sessions[key] = new Session(id, ms);

            Write($"{Environment.NewLine}======== [#{id}] BEGIN  {key} ========"
                + (detail is null ? "" : $"  ({detail})"));
        }
    }

    public static void Mark(string stage, string? file = null, string? detail = null)
    {
        if (!Enabled) return;

        lock (_lock)
        {
            var ms = _sw.ElapsedMilliseconds;
            var tid = Environment.CurrentManagedThreadId;

            var id = "-";
            var delta = string.Empty;
            if (file is not null && _sessions.TryGetValue(NormalizeKey(file), out var s))
            {
                id = s.Id.ToString();
                delta = $"(+{ms - s.LastMs,5})";
                s.LastMs = ms;
            }

            var line = $"{ms,7} ms  {delta,-8}  [t{tid}]  [#{id}]  {stage}"
                + (detail is null ? "" : $" = {detail}");
            Write(line);
        }
    }

    public static void End(string? file, string? detail = null)
    {
        if (!Enabled) return;

        var key = NormalizeKey(file);
        lock (_lock)
        {
            var ms = _sw.ElapsedMilliseconds;
            if (_sessions.Remove(key, out var s))
            {
                var elapsed = $"elapsed={ms - s.BeginMs} ms";
                Write($"======== [#{s.Id}] END    {key} ========  "
                    + $"({elapsed}{(detail is null ? "" : $", {detail}")})");
            }
        }
    }

    private static string NormalizeKey(string? file)
    {
        return string.IsNullOrEmpty(file) ? MEMORY_KEY : file;
    }

    private static void Write(string line)
    {
        Debug.WriteLine($"[IG-PHOTO] {line}");

        try
        {
            _writer ??= new StreamWriter(BHelper.ConfigDir(Dir.Logs, "ig_photo_trace.log"), append: true)
            {
                AutoFlush = true,
            };
            _writer.WriteLine(line);
        }
        catch { }
    }
}
