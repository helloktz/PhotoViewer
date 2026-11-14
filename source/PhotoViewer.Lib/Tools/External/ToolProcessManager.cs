using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using ImageGlass.SDK.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

internal sealed class ToolProcessInfo
{

    public required ExternalTool Tool { get; init; }

    public required Process Process { get; init; }

    public required NamedPipeServerStream PipeServer { get; init; }

    public required string PipeName { get; init; }

    public required ToolPipeServer PipeHandler { get; init; }
}

public sealed class ToolProcessManager : PhDisposable
{
    private readonly Dictionary<string, ToolProcessInfo> _processes = [];
    private readonly Lock _lock = new();

    internal async Task<(ToolProcessInfo? Info, string? Error)> StartToolAsync(ExternalTool tool)
    {
        if (string.IsNullOrEmpty(tool.Executable)) return (null, null);

        var pipeName = CreatePipeName();

        var pipeServer = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var exe = BHelper.ResolvePath(tool.Executable);
        var workingDir = Path.GetDirectoryName(exe) ?? string.Empty;

        Process? process;
        try
        {
            var filePath = Core.Photos?.Current?.FilePath ?? string.Empty;
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = workingDir,
            };

            foreach (var arg in BHelper.BuildArgumentList(tool.Arguments, filePath))
            {
                psi.ArgumentList.Add(arg);
            }
            psi.ArgumentList.Add("--pipe");
            psi.ArgumentList.Add(pipeName);

            BHelper.ApplyFlatpakHostSpawn(psi);

            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            await pipeServer.DisposeAsync();
            return (null, ex.Message);
        }

        if (process is null)
        {
            await pipeServer.DisposeAsync();
            return (null, null);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await pipeServer.WaitForConnectionAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            await pipeServer.DisposeAsync();
            return (null, null);
        }

        if (Core.PipeSecurityProvider is { } pipeSec
            && !pipeSec.VerifyClientProcess(pipeServer.SafePipeHandle, process.Id))
        {
            try { process.Kill(); } catch { }
            await pipeServer.DisposeAsync();
            return (null, null);
        }

        var pipeHandler = new ToolPipeServer(pipeServer, tool.ToolId);

        var info = new ToolProcessInfo
        {
            Tool = tool,
            Process = process,
            PipeServer = pipeServer,
            PipeName = pipeName,
            PipeHandler = pipeHandler,
        };

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => CleanupExitedProcess(tool.ToolId, info);

        lock (_lock)
        {
            _processes[tool.ToolId] = info;
        }

        return (info, null);
    }

    private static string CreatePipeName()
    {
        var id = $"ig_{Guid.NewGuid():N}";
        if (BHelper.OS != OSType.Linux) return id;

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "ImageGlass", "tools");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, id);
    }

    public async Task StopToolAsync(string toolId)
    {
        ToolProcessInfo? info;
        lock (_lock)
        {
            if (!_processes.Remove(toolId, out info)) return;
        }

        try
        {
            if (!info.Process.HasExited)
            {
                info.PipeHandler.SendEvent(MessageTypes.SHUTDOWN);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                if (!info.Process.HasExited)
                {
                    await info.Process.WaitForExitAsync(cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                try { info.Process.Kill(); } catch { }
            }
        }
        catch { }
        finally
        {
            DisposeProcessInfo(info);
        }
    }

    public async Task StopAllAsync()
    {
        string[] ids;
        lock (_lock)
        {
            ids = [.. _processes.Keys];
        }

        foreach (var id in ids)
        {
            await StopToolAsync(id);
        }
    }

    internal ToolProcessInfo? GetRunningTool(string toolId)
    {
        ToolProcessInfo? exitedInfo = null;

        lock (_lock)
        {
            _processes.TryGetValue(toolId, out var info);
            if (info is not null && info.Process.HasExited)
            {
                _processes.Remove(toolId);
                exitedInfo = info;
                info = null;
            }

            if (exitedInfo is null)
            {
                return info;
            }
        }

        DisposeProcessInfo(exitedInfo);
        return null;
    }

    public bool IsRunning(string toolId)
    {
        return GetRunningTool(toolId) is not null;
    }

    public void BroadcastToAll(string type, object? payload = null)
    {
        ToolProcessInfo[] infos;
        lock (_lock)
        {
            infos = [.. _processes.Values];
        }

        foreach (var info in infos)
        {
            if (info.Process.HasExited)
            {
                CleanupExitedProcess(info.Tool.ToolId, info);
                continue;
            }

            try
            {
                info.PipeHandler.SendEvent(type, payload);
            }
            catch
            {
                CleanupExitedProcess(info.Tool.ToolId, info);
            }
        }
    }

    public void BroadcastToSubscribed(string type, object? payload, Func<ToolEventSubscriptions, bool> filter)
    {
        ToolProcessInfo[] infos;
        lock (_lock)
        {
            infos = [.. _processes.Values];
        }

        foreach (var info in infos)
        {
            if (info.Process.HasExited)
            {
                CleanupExitedProcess(info.Tool.ToolId, info);
                continue;
            }

            try
            {
                if (filter(info.PipeHandler.Subscriptions))
                {
                    info.PipeHandler.SendEvent(type, payload);
                }
            }
            catch
            {
                CleanupExitedProcess(info.Tool.ToolId, info);
            }
        }
    }

    private void CleanupExitedProcess(string toolId, ToolProcessInfo info)
    {
        var shouldDispose = false;

        lock (_lock)
        {
            if (_processes.TryGetValue(toolId, out var current) && ReferenceEquals(current, info))
            {
                _processes.Remove(toolId);
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            DisposeProcessInfo(info);
        }
    }

    private static void DisposeProcessInfo(ToolProcessInfo info)
    {
        try { info.PipeHandler.Dispose(); } catch { }
        try { info.PipeServer.Dispose(); } catch { }
        try { info.Process.Dispose(); } catch { }
    }

    protected override void OnDisposing()
    {
        lock (_lock)
        {
            foreach (var info in _processes.Values)
            {
                try
                {
                    info.PipeHandler.Dispose();
                    info.PipeServer.Dispose();
                    try { info.Process.Kill(); } catch { }
                    info.Process.Dispose();
                }
                catch { }
            }
            _processes.Clear();
        }
    }
}
