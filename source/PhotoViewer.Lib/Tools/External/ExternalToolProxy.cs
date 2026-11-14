using PhotoViewer.Common;
using PhotoViewer.Common.Types;
using ImageGlass.SDK.Tools;
using PhotoViewer.UI.Viewer;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Tools;

internal readonly record struct ToolLaunchResult(bool Success, string? Error)
{
    public static ToolLaunchResult Ok { get; } = new(true, null);
    public static ToolLaunchResult Fail(string? error) => new(false, error);
}

internal sealed class ExternalToolProxy : ITool
{
    private readonly ExternalTool _tool;
    private readonly ToolProcessManager _processManager;

    public string ToolId => _tool.ToolId;

    public bool IsHosted => false;

    public object? Settings => null;

    public ViewerControl Viewer { get; set; } = null!;

    internal ExternalTool Tool => _tool;

    public ExternalToolProxy(ExternalTool tool, ToolProcessManager processManager)
    {
        _tool = tool;
        _processManager = processManager;
    }

    public async Task<ToolLaunchResult> TryLaunchAsync(ToolExecutionContext context)
    {
        if (!_tool.IsIntegrated)
        {
            return TryLaunchDetached(context);
        }

        var info = _processManager.GetRunningTool(ToolId);
        if (info is null)
        {
            var (started, error) = await _processManager.StartToolAsync(_tool);

            if (started is null) return error is null ? ToolLaunchResult.Ok : ToolLaunchResult.Fail(error);
            info = started;

            info.PipeHandler.SendEvent(MessageTypes.INIT, new ToolInitPayload
            {
                ToolId = ToolId,
                DataDirectory = Path.GetDirectoryName(BHelper.ResolvePath(_tool.Executable)) ?? string.Empty,
                PipeName = info.PipeName,
                ThemeInfo = new ThemeInfo
                {
                    IsDarkMode = Core.Theme.Settings.IsDarkMode,
                    AccentColor = Core.AccentColor.ToString(),
                    BackgroundColor = Core.Config.BackgroundColor,
                },
            });

            _ = Task.Run(() => info.PipeHandler.RunMessageLoopAsync(CancellationToken.None));
        }

        info.PipeHandler.SendEvent(MessageTypes.EXECUTE);
        return ToolLaunchResult.Ok;
    }

    private ToolLaunchResult TryLaunchDetached(ToolExecutionContext context)
    {
        if (string.IsNullOrEmpty(_tool.Executable)) return ToolLaunchResult.Fail(null);

        var exe = BHelper.ResolvePath(_tool.Executable).Trim();
        var filePath = Core.Photos?.Current?.FilePath ?? string.Empty;

        try
        {
            if (exe.EndsWith(':'))
            {
                var built = BHelper.BuildExeArgList(exe, _tool.Arguments, filePath);
                _ = BHelper.RunExeAsync(built.Executable, built.Args);
                return ToolLaunchResult.Ok;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            };
            foreach (var arg in BHelper.BuildArgumentList(_tool.Arguments, filePath))
            {
                psi.ArgumentList.Add(arg);
            }

            BHelper.ApplyFlatpakHostSpawn(psi);

            return Process.Start(psi) is not null ? ToolLaunchResult.Ok : ToolLaunchResult.Fail(null);
        }
        catch (Exception ex)
        {
            return ToolLaunchResult.Fail(ex.Message);
        }
    }
}
