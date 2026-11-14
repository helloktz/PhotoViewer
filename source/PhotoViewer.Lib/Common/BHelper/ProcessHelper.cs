using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using PhotoViewer.Common.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoViewer.Common;

public partial class BHelper
{
    private static readonly TaskFactory _taskFactory = new(
        CancellationToken.None, TaskCreationOptions.None,
        TaskContinuationOptions.None, TaskScheduler.Default);

    public static bool IsFlatpakSandbox { get; } = OS == OSType.Linux && File.Exists("/.flatpak-info");

    public static bool IsAppImage { get; } = OS == OSType.Linux
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"));

    public static void ApplyFlatpakHostSpawn(ProcessStartInfo psi)
    {
        if (!IsFlatpakSandbox
            || psi.UseShellExecute
            || string.IsNullOrEmpty(psi.FileName)
            || psi.FileName == "flatpak-spawn")
        {
            return;
        }

        var hostArgs = new List<string>(psi.ArgumentList.Count + 2)
        {
            "--host",
            GetRealPlatformPath(psi.FileName),
        };
        hostArgs.AddRange(psi.ArgumentList);

        psi.FileName = "flatpak-spawn";
        psi.WorkingDirectory = string.Empty;
        psi.ArgumentList.Clear();
        foreach (var arg in hostArgs) psi.ArgumentList.Add(arg);
    }

    public static void RunProcess(string fileName, string arguments)
    {
        using var proc = new Process();
        proc.StartInfo.FileName = fileName;
        proc.StartInfo.Arguments = arguments;
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        proc.Start();
    }

    public static string RunProcessAndReadOutput(string fileName, string arguments)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = fileName;
            proc.StartInfo.Arguments = arguments;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.Start();

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static (string Executable, string Args) BuildExeArgs(string executable, string arguments, string currentFilePath = "")
    {
        var exe = executable.Trim();
        var isAppProtocol = exe.EndsWith(':');

        var filePath = isAppProtocol ? currentFilePath : $"\"{currentFilePath}\"";

        var args = arguments.Replace(Const.FILE_MACRO, filePath);

        return (Executable: exe, Args: args);
    }

    public static (string Executable, List<string> Args) BuildExeArgList(string executable, string? arguments, string currentFilePath = "")
    {
        var exe = executable.Trim();

        if (exe.EndsWith(':'))
        {
            var tail = (arguments ?? string.Empty).Replace(Const.FILE_MACRO, currentFilePath);
            var protocolArgs = new List<string>();
            if (tail.Length > 0) protocolArgs.Add(tail);
            return (exe, protocolArgs);
        }

        return (exe, BuildArgumentList(arguments, currentFilePath));
    }

    public static List<string> BuildArgumentList(string? argsTemplate, string filePath)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(argsTemplate)) return result;

        var token = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var ch in argsTemplate)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (hasToken)
                {
                    result.Add(token.ToString().Replace(Const.FILE_MACRO, filePath));
                    token.Clear();
                    hasToken = false;
                }
                continue;
            }

            token.Append(ch);
            hasToken = true;
        }

        if (hasToken)
        {
            result.Add(token.ToString().Replace(Const.FILE_MACRO, filePath));
        }

        return result;
    }

    public static async Task<IgExitCode> RunExeCmd(string exePath, IReadOnlyList<string>? args = null, bool waitForExit = true, bool showError = false)
    {
        try
        {
            var code = (IgExitCode)await RunExeAsync(exePath, args, asAdmin: false, waitForExit, showError);

            if (code == IgExitCode.AdminRequired)
            {
                code = (IgExitCode)await RunExeAsync(exePath, args, asAdmin: true, waitForExit);
            }

            return code;
        }
        catch
        {
            return IgExitCode.Error;
        }
    }

    public static async Task<int> RunExeAsync(string path, IReadOnlyList<string>? args = null, bool asAdmin = false, bool waitForExit = false, bool showError = false)
    {
        using var proc = new Process();
        ConfigureExeStart(proc.StartInfo, path.Trim(), args ?? [], asAdmin);
        proc.StartInfo.ErrorDialog = showError && proc.StartInfo.UseShellExecute;

        try
        {
            proc.Start();

            if (waitForExit)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode;
            }

            return (int)IgExitCode.Done;
        }
        catch (Win32Exception ex)
        {
            return (int)(ex.NativeErrorCode switch
            {
                2 => IgExitCode.Error_FileNotFound,
                740 => IgExitCode.AdminRequired,
                _ => IgExitCode.Error,
            });
        }
        catch
        {
            return (int)IgExitCode.Error;
        }
    }

    private static void ConfigureExeStart(ProcessStartInfo psi, string path, IReadOnlyList<string> args, bool asAdmin)
    {
        if (path.EndsWith(':'))
        {
            psi.FileName = $"{path}{string.Concat(args)}";
            psi.UseShellExecute = true;
            return;
        }

        if (!asAdmin)
        {
            psi.FileName = path;
            psi.UseShellExecute = true;
            AddArgs(psi, args);
            return;
        }

        switch (BHelper.OS)
        {
            case OSType.Mac:
                var macCmd = string.Join(" ", args.Prepend(path).Select(ShellQuote));
                psi.FileName = "osascript";
                psi.UseShellExecute = false;
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add($"do shell script \"{EscapeAppleScript(macCmd)}\" with administrator privileges");
                break;

            case OSType.Linux:
                psi.FileName = "pkexec";
                psi.UseShellExecute = false;
                psi.ArgumentList.Add(path);
                AddArgs(psi, args);
                break;

            default:
                psi.FileName = path;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                AddArgs(psi, args);
                break;
        }
    }

    private static void AddArgs(ProcessStartInfo psi, IReadOnlyList<string> args)
    {
        foreach (var arg in args) psi.ArgumentList.Add(arg);
    }

    private static string ShellQuote(string s) => $"'{s.Replace("'", "'\\''")}'";

    private static string EscapeAppleScript(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static TResult RunSync<TResult>(Func<Task<TResult>> func)
    {
        var cultureUi = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.CurrentCulture;

        return _taskFactory.StartNew(() =>
        {
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = cultureUi;
            return func();
        }).Unwrap().GetAwaiter().GetResult();
    }

    public static void RunSync(Func<Task> func)
    {
        var cultureUi = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.CurrentCulture;

        _taskFactory.StartNew(() =>
        {
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = cultureUi;
            return func();
        }).Unwrap().GetAwaiter().GetResult();
    }

    public static bool HasOtherInstances()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            var procs = Process.GetProcessesByName(current.ProcessName);

            var hasOther = false;
            foreach (var proc in procs)
            {
                if (proc.Id != current.Id) hasOther = true;
                proc.Dispose();
            }

            return hasOther;
        }
        catch { return false; }
    }

    public static void CloseOtherInstances()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var proc in Process.GetProcessesByName(current.ProcessName))
            {
                try
                {
                    if (proc.Id != current.Id) proc.Kill();
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }
    }

    public static void RestartApp(bool suppressQuickSetup = false)
    {
        Core.AppInstance.Dispose();

        IReadOnlyList<string> args = suppressQuickSetup ? [AppCmds.NO_QUICK_SETUP] : [];
        _ = RunExeAsync(AppRelaunchPath, args);
        ExitApp(false);
    }

    public static void ExitApp(bool forced, int exitCode = 0)
    {
        if (forced)
        {
            Environment.Exit(exitCode);
            return;
        }

        var appLf = Application.Current?.ApplicationLifetime;

        if (appLf is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = desktop.TryShutdown(exitCode);
        }
    }

}
